import release from '../release.json' with { type: 'json' };
import { isInstallerDownload, isVerifiedRelease, parseReleaseVersion, releaseAssetUrl, websiteDownloadRecordUrl } from '../src/lib/downloadRelease.js';

export { release };
export const releaseDownloadUrl = releaseAssetUrl(release.version, release.installer);

export function resolveReleaseDownload(environment) {
  const version = environment.RIGMD_RELEASE_VERSION;
  // Keep the explicitly enabled fallback available across desktop version bumps.
  if (parseReleaseVersion(version)) {
    const url = environment.RIGMD_DOWNLOAD_URL || releaseAssetUrl(version, release.installer);
    if (isInstallerDownload(version, url)) return { available: true, version, url };
  }

  return { available: false, version: release.version, message: 'The current download is being prepared. Please try again later.' };
}

export async function resolveWebsiteDownload(environment, request = fetch) {
  const fallback = resolveReleaseDownload(environment);
  try {
    const response = await request(websiteDownloadRecordUrl, {
      cache: 'no-cache',
      redirect: 'error',
      signal: AbortSignal.timeout(2500),
    });
    if (!response.ok) return fallback;

    const current = await response.json();
    if (!isVerifiedRelease(current)) return fallback;
    return {
      available: true,
      version: current.version,
      url: releaseAssetUrl(current.version, current.asset),
      prerelease: current.prerelease,
    };
  } catch {
    return fallback;
  }
}
