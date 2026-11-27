import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { release, releaseDownloadUrl, resolveReleaseDownload, resolveWebsiteDownload } from '../server/releasePolicy.js';
import { isInstallerDownload, parseReleaseVersion, releaseAssetUrl, websiteDownloadRecordUrl } from '../src/lib/downloadRelease.js';
import handler from '../api/download-url.js';

test('a release is unavailable until explicitly enabled, and stale URL overrides cannot select an older installer', () => {
  for (const environment of [{}, { RIGMD_RELEASE_VERSION: '0.2' },
    { RIGMD_RELEASE_VERSION: release.version, RIGMD_DOWNLOAD_URL: 'https://github.com/MichaelGrant-Libato/RigMD/releases/download/v0.2/RigMD-Setup.exe' },
    { RIGMD_RELEASE_VERSION: release.version, RIGMD_DOWNLOAD_URL: releaseDownloadUrl + '?other=1' }]) {
    const result = resolveReleaseDownload(environment); assert.equal(result.available, false); assert.equal(result.url, undefined);
  }
  assert.equal(resolveReleaseDownload({ RIGMD_RELEASE_VERSION: release.version }).url, releaseDownloadUrl);
  assert.equal(resolveReleaseDownload({ RIGMD_RELEASE_VERSION: release.version, RIGMD_DOWNLOAD_URL: releaseDownloadUrl }).available, true);
});
test('desktop, API, package, lockfile and installer versions match the release manifest', () => {
  const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
  assert.equal(JSON.parse(read('../package.json')).version, release.version);
  const lock = JSON.parse(read('../package-lock.json')); assert.equal(lock.version, release.version); assert.equal(lock.packages[''].version, release.version);
  for (const project of ['RigMD.Api', 'RigMD.Desktop']) assert.equal(/<Version>([^<]+)<\/Version>/.exec(read(`../../backend-dotnet/${project}/${project}.csproj`))[1], release.version);
  assert.equal(/#define MyAppVersion "([^"]+)"/.exec(read('../../installer/RigMD-Setup.iss'))[1], release.version);
});
test('download configuration rejects mutation requests and never caches a version pointer', () => {
  const res = { headers: {}, setHeader(key, value) { this.headers[key] = value; }, status(code) { this.code = code; return this; }, json(body) { this.body = body; return this; } };
  handler({ method: 'POST' }, res); assert.equal(res.code, 405); assert.equal(res.headers['Cache-Control'], 'no-store'); assert.equal(res.body.url, undefined);
});

test('runtime release supplies the displayed version and exact link independently of the desktop build', async () => {
  const [major, minor, patch] = parseReleaseVersion(release.version);
  const version = `${major}.${minor}.${patch + 1}`;
  const asset = `RigMD-Setup-v${version}.exe`;
  const record = { version, asset, sha256: 'a'.repeat(64), sourceRevision: 'b'.repeat(40), prerelease: true };
  const resolve = async (url, options) => {
    assert.equal(String(url), websiteDownloadRecordUrl);
    assert.equal(options.headers, undefined);
    assert.equal(options.redirect, 'error');
    return Response.json(record);
  };
  const result = await resolveWebsiteDownload({ RIGMD_RELEASE_VERSION: release.version }, resolve);
  assert.deepEqual(result, { available: true, version, url: releaseAssetUrl(version, asset), prerelease: true });
  assert.equal(isInstallerDownload(result.version, result.url), true);
  assert.equal(isInstallerDownload(release.version, result.url), false);
  assert.equal(isInstallerDownload(result.version, result.url + '?asset=other'), false);
  assert.equal(isInstallerDownload(result.version, result.url.replace('github.com', 'example.com')), false);
});

test('missing, unavailable or invalid runtime data keeps the deployment fallback', async () => {
  const environment = { RIGMD_RELEASE_VERSION: release.version };
  for (const request of [
    () => new Response(null, { status: 404 }),
    () => Response.json({ version: '0.2.22', asset: 'Other.exe' }),
    () => { throw new Error('Timed out'); },
  ]) {
    assert.deepEqual(await resolveWebsiteDownload(environment, request), resolveReleaseDownload(environment));
  }
});

test('an explicitly pinned fallback remains available when the build version advances', async () => {
  const version = '0.2.19';
  const environment = {
    RIGMD_RELEASE_VERSION: version,
    RIGMD_DOWNLOAD_URL: releaseAssetUrl(version, 'RigMD-Setup-v0.2.19.exe'),
  };
  const fallback = await resolveWebsiteDownload(environment, () => new Response(null, { status: 404 }));
  assert.deepEqual(fallback, { available: true, version, url: environment.RIGMD_DOWNLOAD_URL });
  assert.equal(resolveReleaseDownload({
    ...environment, RIGMD_DOWNLOAD_URL: 'https://example.com/RigMD-Setup.exe',
  }).available, false);
});
