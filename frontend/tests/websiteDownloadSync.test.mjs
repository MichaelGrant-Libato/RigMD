import test from 'node:test';
import assert from 'node:assert/strict';
import { promoteRelease, verifyRelease, verifyWebsiteDownload } from '../scripts/sync-website-download.mjs';
import { downloadConfigKey, releaseAssetUrl } from '../src/lib/downloadRelease.js';

const version = '0.2.21';
const tag = 'v' + version;
const sourceRevision = 'a164419b98ad862a9b48041c1f801de0ba6e0473';
const mergedRevision = '0837fd2ab475f3598aa42e9c9dec0d066c9c4fae';
const sha256 = '03053cd6f899652a87110b9497974c87a4dc9f5fb96052f00b8f1278c644600c';
const bytes = 135278833;
const candidate = { version, asset: 'RigMD-Setup-v0.2.21.exe', sha256, sourceRevision, prerelease: false };

function fixture({ manifest = {}, asset = {}, commit = sourceRevision, prerelease = false,
                   includeManifest = true, downloadStatus = 200, annotated = false } = {}) {
  const tagObject = 'a'.repeat(40);
  return async (url, options) => {
    assert.equal(options.headers?.Authorization, url.startsWith('https://api.github.com/') ? 'Bearer test-token' : undefined);
    if (url.endsWith('/releases/tags/' + tag)) return Response.json({
      draft: false,
      prerelease,
      published_at: '2026-11-26T23:07:57Z',
      tag_name: tag,
      assets: [
        { name: candidate.asset, browser_download_url: releaseAssetUrl(version, candidate.asset),
          state: 'uploaded', size: bytes, digest: 'sha256:' + sha256, ...asset },
        ...(includeManifest ? [{ name: 'release-manifest.json', size: 512, state: 'uploaded',
          browser_download_url: releaseAssetUrl(version, 'release-manifest.json') }] : []),
      ],
    });
    if (url.endsWith('/git/ref/tags/' + tag)) return Response.json({
      object: annotated ? { type: 'tag', sha: tagObject } : { type: 'commit', sha: commit },
    });
    if (url.endsWith('/git/tags/' + tagObject)) return Response.json({ object: { type: 'commit', sha: commit } });
    if (url === releaseAssetUrl(version, 'release-manifest.json')) {
      return new Response('\uFEFF' + JSON.stringify({ version, sha256: sha256.toUpperCase(), bytes, sourceRevision, sourceDirty: false, ...manifest }));
    }
    if (url === releaseAssetUrl(version, candidate.asset)) {
      assert.equal(options.method, 'HEAD');
      return new Response(null, { status: downloadStatus });
    }
    throw new Error('Unexpected fetch ' + url);
  };
}

const verify = (settings, allowPrereleases = false) => verifyRelease(tag, {
  request: fixture(settings), githubToken: 'test-token', allowPrereleases,
});

test('verification uses the published asset digest and resolves an annotated tag to the build commit', async () => {
  assert.deepEqual(await verify({ annotated: true }), candidate);
  assert.equal(await verify({ prerelease: true }), null);
  assert.deepEqual(await verify({ prerelease: true }, true), { ...candidate, prerelease: true });
});

test('the existing 0.2.21 release fails with either its missing manifest or its mismatched source tag', async () => {
  await assert.rejects(verify({ includeManifest: false }), /Missing uploaded asset: release-manifest/);
  await assert.rejects(verify({ commit: mergedRevision }), /points to 0837fd2.*built from a164419/);
});

test('dirty builds, wrong digests, changed byte counts and broken links are rejected', async () => {
  await assert.rejects(verify({ manifest: { sourceDirty: true } }), /clean build/);
  await assert.rejects(verify({ asset: { digest: 'sha256:' + 'f'.repeat(64) } }), /checksum or byte count/);
  await assert.rejects(verify({ asset: { size: bytes + 1 } }), /checksum or byte count/);
  await assert.rejects(verify({ downloadStatus: 404 }), /download returned HTTP 404/);
});

test('promotion writes one verified record and ignores a replay or an older release', async () => {
  let items = [];
  let writes = 0;
  const request = async (url, options) => {
    assert.equal(String(url), 'https://api.vercel.com/v1/global-config/ec_test/items?teamId=team_test');
    assert.equal(options.headers.Authorization, 'Bearer write-test-token');
    if (options.method !== 'PATCH') return Response.json(items);
    const { items: requested } = JSON.parse(options.body);
    assert.deepEqual(requested, [{ operation: 'upsert', key: downloadConfigKey, value: candidate }]);
    items = requested;
    writes++;
    return Response.json({ status: 'ok' });
  };
  const options = { request, configId: 'ec_test', teamId: 'team_test', vercelToken: 'write-test-token' };
  assert.equal(await promoteRelease(candidate, options), 'promoted');
  assert.equal(await promoteRelease(candidate, options), 'already active');
  assert.equal(await promoteRelease({ ...candidate, version: '0.2.20', asset: 'RigMD-Setup.exe' }, options), 'skipped older version');
  await assert.rejects(promoteRelease({ ...candidate, sha256: 'f'.repeat(64) }, options), /active version already/);
  assert.equal(writes, 1);
});

test('visibility check waits for the website response to match the promoted version and link', async () => {
  const expected = { available: true, version, url: releaseAssetUrl(version, candidate.asset), prerelease: false };
  let requests = 0;
  let waits = 0;
  await verifyWebsiteDownload(candidate, {
    request: async url => {
      assert.equal(url, 'https://www.rig-md.com/api/download-url');
      return Response.json(++requests === 1 ? { ...expected, version: '0.2.20' } : expected);
    },
    wait: async () => { waits++; },
  });
  assert.equal(requests, 2);
  assert.equal(waits, 1);
  await assert.rejects(verifyWebsiteDownload(candidate, {
    request: async () => Response.json({ ...expected, url: releaseAssetUrl('0.2.20', 'RigMD-Setup.exe') }),
    wait: async () => {},
  }), /site has not reported the approved release/);
});
