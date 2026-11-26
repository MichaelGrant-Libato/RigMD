import { pathToFileURL } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import release from '../release.json' with { type: 'json' };
import {
  installerNames, isVerifiedRelease, parseReleaseVersion, releaseAssetUrl, websiteDownloadBranch, websiteDownloadFile,
} from '../src/lib/downloadRelease.js';

const githubApi = `https://api.github.com/repos/${release.repository}`;
const sha1 = /^[a-f\d]{40}$/i;
const sha256 = /^[a-f\d]{64}$/i;
const apiHeaders = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' };

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
  const headers = { ...apiHeaders };
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

async function readDownloadRecord(request, headers, url) {
  const response = await request(url, {
    headers, redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Could not read the GitHub download record (HTTP ${response.status}).`);
  const file = await response.json();
  if (file.type !== 'file' || file.encoding !== 'base64' || !sha1.test(file.sha) || typeof file.content !== 'string') {
    throw new Error('The current GitHub download record is invalid.');
  }
  return { value: JSON.parse(Buffer.from(file.content, 'base64').toString('utf8')), sha: file.sha };
}

async function ensureDownloadBranch(request, headers, sourceRevision) {
  const response = await request(`${githubApi}/git/ref/heads/${websiteDownloadBranch}`, {
    headers, redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  if (response.ok) return;
  if (response.status !== 404) throw new Error(`Could not read the website download branch (HTTP ${response.status}).`);

  const created = await request(`${githubApi}/git/refs`, {
    method: 'POST',
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify({ ref: `refs/heads/${websiteDownloadBranch}`, sha: sourceRevision }),
    redirect: 'error',
    signal: AbortSignal.timeout(15_000),
  });
  if (!created.ok) throw new Error(`Could not create the website download branch (HTTP ${created.status}).`);
}

export async function promoteRelease(candidate, { request = fetch, githubToken } = {}) {
  if (!isVerifiedRelease(candidate)) throw new Error('Missing verified release metadata.');
  if (!githubToken) throw new Error('GitHub Actions needs a write token for the website download branch.');

  const headers = { ...apiHeaders, Authorization: `Bearer ${githubToken}` };
  const url = `${githubApi}/contents/${websiteDownloadFile}`;
  const current = await readDownloadRecord(request, headers, `${url}?ref=${websiteDownloadBranch}`);
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

  if (!current) await ensureDownloadBranch(request, headers, candidate.sourceRevision);
  const payload = {
    branch: websiteDownloadBranch,
    message: `Promote RigMD v${candidate.version} website download`,
    content: Buffer.from(JSON.stringify(candidate, null, 2) + '\n').toString('base64'),
    ...(current ? { sha: current.sha } : {}),
  };
  const response = await request(url, {
    method: 'PUT',
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    redirect: 'error',
    signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok) throw new Error(`GitHub rejected the website download record update (HTTP ${response.status}).`);
  return 'promoted';
}

export async function verifyWebsiteDownload(candidate, { request = fetch, wait = delay } = {}) {
  const expectedUrl = releaseAssetUrl(candidate.version, candidate.asset);
  for (let attempt = 0; attempt < 8; attempt++) {
    if (attempt) await wait(10_000);
    try {
      const response = await request('https://www.rig-md.com/api/download-url', {
        redirect: 'error', signal: AbortSignal.timeout(5000),
      });
      if (!response.ok) continue;
      const download = await response.json();
      if (download?.available === true && download.version === candidate.version && download.url === expectedUrl &&
          download.prerelease === candidate.prerelease) return;
    } catch {
      // The new record may still be propagating through GitHub's cache.
    }
  }
  throw new Error('The site has not reported the approved release. Check the production deployment, then rerun.');
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
    githubToken: process.env.GITHUB_TOKEN,
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
