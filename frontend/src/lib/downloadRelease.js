import release from '../../release.json' with { type: 'json' };

export const downloadConfigKey = 'rigmdDownloadRelease';

export function parseReleaseVersion(version) {
  return typeof version === 'string' && /^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$/.test(version)
    ? version.split('.').map(Number)
    : null;
}

export function installerNames(version) {
  return [release.installer, `RigMD-Setup-v${version}.exe`];
}

export function releaseAssetUrl(version, asset) {
  return `https://github.com/${release.repository}/releases/download/v${version}/${asset}`;
}

export function isInstallerDownload(version, url) {
  return parseReleaseVersion(version) !== null &&
    installerNames(version).some(asset => url === releaseAssetUrl(version, asset));
}

export function isVerifiedRelease(record) {
  return Boolean(record && parseReleaseVersion(record.version) &&
    installerNames(record.version).includes(record.asset) &&
    typeof record.sha256 === 'string' && /^[a-f0-9]{64}$/i.test(record.sha256) &&
    typeof record.sourceRevision === 'string' && /^[a-f0-9]{40}$/i.test(record.sourceRevision) &&
    typeof record.prerelease === 'boolean');
}
