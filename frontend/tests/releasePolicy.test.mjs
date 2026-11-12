import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { release, releaseDownloadUrl, resolveReleaseDownload } from '../server/releasePolicy.js';
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
