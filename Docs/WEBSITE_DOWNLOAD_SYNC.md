# RigMD website download sync

GitHub hosts the installer and its approved download record. The Download RigMD button and version label use the same response from `/api/download-url`. Once this website code deploys, routine release updates require only GitHub access.

## First deployment

1. Merge the website changes into the production branch connected to Vercel. Keep the existing `RIGMD_RELEASE_VERSION` and optional `RIGMD_DOWNLOAD_URL` settings as the fallback. The fallback remains available while the download record is missing or unreachable.
2. Confirm the live endpoint returns the working download. Use **Sync website download** in GitHub Actions with **Verify the release without updating the website** selected to check a published candidate.

## Publish a release

1. Build from a clean Git commit. Create the version tag from that exact commit.
2. Attach `RigMD-Setup.exe` or `RigMD-Setup-vX.Y.Z.exe`, plus the unedited `release-manifest.json` or `release-manifest-vX.Y.Z.json` produced by the installer build. Publish the release once both uploads finish.
3. The workflow checks the published tag, manifest source revision, installer size, GitHub asset digest and download URL. Once those checks pass, the workflow sets `website-download.json` on the dedicated `website-download` branch using the built-in GitHub Actions token. The workflow creates that branch on the first promotion. Rerunning the same build is safe. Older versions and conflicting replacements leave the active record in place.
4. The workflow checks the live download endpoint for the approved version and URL. If GitHub has updated the record and the website still reports the previous release, check the production deployment and rerun. Confirm the page label and Windows download agree.

The workflow skips GitHub prereleases by default. To use the site as the capstone prerelease channel, set the Actions variable `RIGMD_PROMOTE_PRERELEASES=true`. The release's GitHub prerelease flag controls this selection. The website includes a prerelease label when the promoted record carries that flag.

Use **Run workflow** from the default branch to verify or sync an already-published release. For a sync, provide the tag and clear the verify-only checkbox. This can also retry a run after a missing attachment has been uploaded. To roll back, restore the previous `website-download.json` content on the download branch. When a release fails verification, the workflow leaves the record in place.

## Current 0.2.21 release

The published tag `v0.2.21` resolves to `0837fd2`, and the installer manifest identifies `a164419` as the build commit. The public release also lacks the manifest attachment. Uploading the manifest alone will expose the source mismatch, so the workflow will continue to reject it.

Keep the published tag as a historical reference. Prepare a new version from a clean, reviewed commit, then publish the installer and matching manifest on a new tag that points to the build commit.
