import { pathToFileURL } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import release from '../release.json' with { type: 'json' };
import {
  downloadConfigKey, installerNames, isVerifiedRelease, parseReleaseVersion, releaseAssetUrl,
} from '../src/lib/downloadRelease.js';

const githubApi = `https://api.github.com/repos/${release.repository}`;
const sha1 = /^[a-f\d]{40}$/i;
const sha256 = /^[a-f\d]{64}$/i;

async function getJson(request, url, headers, service) {
  const response = await request(url, {
    headers, redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok) throw new Error(`${service} returned HTTP ${response.status}.`);
  return response.json();
}

function findAsset(assets, version, names) {
  for (const name of names) {
    const url = releaseAssetUrl(version, name);
    const asset = assets.find(item => item.name === name);
    if (asset?.state === 'uploaded' && asset.browser_download_url === url &&
        Number.isSafeInteger(asset.size) && asset.size > 0) return asset;
  }
  throw new Error(`Missing uploaded asset: ${names.join(' or ')}.`);
}

async function resolveTagCommit(github, tag) {
  let { object } = await github(`git/ref/tags/${tag}`);
  for (let depth = 0; depth < 6 && object && sha1.test(object.sha); depth++) {
    if (object.type === 'commit') return object.sha.toLowerCase();
    if (object.type !== 'tag') break;
    ({ object } = await github(`git/tags/${object.sha}`));
  }
  throw new Error(`Could not resolve ${tag} to a commit.`);
}

export async function verifyRelease(tag, { request = fetch, githubToken, allowPrereleases = false } = {}) {
  if (typeof tag !== 'string' || !tag.startsWith('v') || !parseReleaseVersion(tag.slice(1))) {
    throw new Error('Provide a RigMD release tag such as v0.2.22.');
  }
  const version = tag.slice(1);
  const headers = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' };
  if (githubToken) headers.Authorization = `Bearer ${githubToken}`;
  const github = path => getJson(request, `${githubApi}/${path}`, headers, 'GitHub');

  const published = await github(`releases/tags/${tag}`);
  if (published.tag_name !== tag || published.draft !== false || !published.published_at ||
      typeof published.prerelease !== 'boolean') throw new Error(`${tag} is not a published release.`);
  if (published.prerelease && !allowPrereleases) return null;

  const installer = findAsset(published.assets, version, installerNames(version));
  const manifestAsset = findAsset(published.assets, version, ['release-manifest.json', `release-manifest-v${version}.json`]);
  if (manifestAsset.size > 65_536) throw new Error('The release manifest is too large.');

  // Public asset requests do not send repository credentials to the redirect destination.
  const response = await request(manifestAsset.browser_download_url, {
    signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok) throw new Error(`Could not download the release manifest (HTTP ${response.status}).`);
  const manifest = JSON.parse(await response.text());
  if (manifest?.version !== version || manifest.sourceDirty !== false ||
      typeof manifest.sourceRevision !== 'string' || !sha1.test(manifest.sourceRevision)) {
    throw new Error('The manifest must identify a clean build of the tagged version.');
  }
  const sourceRevision = await resolveTagCommit(github, tag);
  if (sourceRevision !== manifest.sourceRevision.toLowerCase()) {
    throw new Error(`${tag} points to ${sourceRevision}, but the installer was built from ${manifest.sourceRevision}.`);
  }
  if (typeof manifest.sha256 !== 'string' || !sha256.test(manifest.sha256) ||
      installer.digest?.toLowerCase() !== `sha256:${manifest.sha256.toLowerCase()}` ||
      installer.size !== manifest.bytes) {
    throw new Error('The uploaded installer checksum or byte count differs from the build manifest.');
  }
  const download = await request(installer.browser_download_url, {
    method: 'HEAD', signal: AbortSignal.timeout(20_000),
  });
  if (!download.ok) throw new Error(`The installer download returned HTTP ${download.status}.`);

  return { version, asset: installer.name, sha256: manifest.sha256.toLowerCase(), sourceRevision, prerelease: published.prerelease };
}

export async function promoteRelease(candidate, { request = fetch, configId, vercelToken, teamId } = {}) {
  if (!isVerifiedRelease(candidate)) throw new Error('Missing verified release metadata.');
  if (!configId || !/^[A-Za-z\d_-]+$/.test(configId) || !vercelToken) {
    throw new Error('Configure RIGMD_GLOBAL_CONFIG_ID and VERCEL_TOKEN to promote the download.');
  }

  const headers = { Authorization: `Bearer ${vercelToken}` };
  const url = new URL(`https://api.vercel.com/v1/global-config/${configId}/items`);
  if (teamId) url.searchParams.set('teamId', teamId);
  const items = await getJson(request, url, headers, 'Vercel');
  const current = items.find(item => item.key === downloadConfigKey);
  if (current) {
    if (!isVerifiedRelease(current.value)) throw new Error('The current download record is invalid.');
    const previousVersion = parseReleaseVersion(current.value.version);
    const nextVersion = parseReleaseVersion(candidate.version);
    const difference = nextVersion.map((part, index) => part - previousVersion[index]).find(Boolean) ?? 0;
    if (difference < 0) return 'skipped older version';
    if (difference === 0) {
      if (['asset', 'sha256', 'sourceRevision', 'prerelease'].some(key => current.value[key] !== candidate[key])) {
        throw new Error('The active version already points to a different build.');
      }
      return 'already active';
    }
  }

  const response = await request(url, {
    method: 'PATCH',
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify({ items: [{ operation: 'upsert', key: downloadConfigKey, value: candidate }] }),
    redirect: 'error',
    signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok) throw new Error(`Vercel rejected the website update (HTTP ${response.status}).`);
  return 'promoted';
}

export async function verifyWebsiteDownload(candidate, { request = fetch, wait = delay } = {}) {
  const expectedUrl = releaseAssetUrl(candidate.version, candidate.asset);
  for (let attempt = 0; attempt < 6; attempt++) {
    if (attempt) await wait(5000);
    try {
      const response = await request('https://www.rig-md.com/api/download-url', {
        redirect: 'error', signal: AbortSignal.timeout(5000),
      });
      if (!response.ok) continue;
      const download = await response.json();
      if (download?.available === true && download.version === candidate.version && download.url === expectedUrl &&
          download.prerelease === candidate.prerelease) return;
    } catch {
      // The Global Config update may still be propagating.
    }
  }
  throw new Error('The site has not reported the approved release. Check the production deployment and Global Config connection, then rerun.');
}

async function main() {
  const tag = process.env.RIGMD_RELEASE_TAG;
  const candidate = await verifyRelease(tag, {
    githubToken: process.env.GITHUB_TOKEN,
    allowPrereleases: process.env.RIGMD_PROMOTE_PRERELEASES === 'true',
  });
  if (!candidate) {
    console.log(`Skipped ${tag}: prerelease downloads are disabled.`);
    return;
  }
  if (process.env.RIGMD_VERIFY_ONLY === 'true') {
    console.log(`Verified ${tag} from ${candidate.sourceRevision}.`);
    return;
  }
  const result = await promoteRelease(candidate, {
    configId: process.env.RIGMD_GLOBAL_CONFIG_ID,
    vercelToken: process.env.VERCEL_TOKEN,
    teamId: process.env.VERCEL_TEAM_ID,
  });
  console.log(`${tag}: ${result}.`);
  if (result !== 'skipped older version') {
    await verifyWebsiteDownload(candidate);
    console.log(`Verified ${tag} on rig-md.com.`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
