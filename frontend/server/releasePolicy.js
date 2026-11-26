import release from '../release.json' with { type: 'json' };
import { downloadConfigKey, isInstallerDownload, isVerifiedRelease, parseReleaseVersion, releaseAssetUrl } from '../src/lib/downloadRelease.js';

export { release };
export const releaseDownloadUrl = releaseAssetUrl(release.version, release.installer);

export function resolveReleaseDownload(environment) {
  const version = environment.RIGMD_RELEASE_VERSION;
  // Global Config deployments retain their explicitly enabled installer as the fallback,
  // even when a later desktop version has reached the website source tree.
  if (parseReleaseVersion(version) && (environment.GLOBAL_CONFIG || version === release.version)) {
    const url = environment.RIGMD_DOWNLOAD_URL || releaseAssetUrl(version, release.installer);
    if (isInstallerDownload(version, url)) return { available: true, version, url };
  }

  return { available: false, version: release.version, message: 'The current download is being prepared. Please try again later.' };
}

export async function resolveWebsiteDownload(environment, request = fetch) {
  const fallback = resolveReleaseDownload(environment);
  if (!environment.GLOBAL_CONFIG) return fallback;

  try {
    const connection = new URL(environment.GLOBAL_CONFIG);
    if (connection.origin !== 'https://global-config.vercel.com' ||
        !/^\/[A-Za-z0-9_-]+\/?$/.test(connection.pathname) ||
        connection.username || connection.password) return fallback;

    const token = connection.searchParams.get('token');
    if (!token) return fallback;
    const url = new URL(`${connection.pathname.replace(/\/$/, '')}/item/${downloadConfigKey}`, connection.origin);
    const response = await request(url, {
      headers: { Authorization: `Bearer ${token}` },
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
