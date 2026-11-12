import release from '../release.json' with { type: 'json' };

export { release };
export const releaseDownloadUrl = `https://github.com/${release.repository}/releases/download/v${release.version}/${release.installer}`;

export function resolveReleaseDownload(environment) {
  // Publishing is an explicit deployment step after the matching artifact has been uploaded/tested.
  // Old server variables must never silently direct this UI to an unrelated release.
  if (environment.RIGMD_RELEASE_VERSION !== release.version ||
    environment.RIGMD_DOWNLOAD_URL && environment.RIGMD_DOWNLOAD_URL !== releaseDownloadUrl)
    return { available: false, version: release.version, message: 'The current download is being prepared. Please try again later.' };
  return { available: true, version: release.version, url: releaseDownloadUrl };
}
