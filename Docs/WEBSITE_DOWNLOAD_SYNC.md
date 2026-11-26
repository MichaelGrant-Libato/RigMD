# RigMD website download sync

GitHub hosts the installer. Vercel Global Config holds the single approved download record that the website reads at runtime. The Download RigMD button and version label use the same response from `/api/download-url`. Routine release updates do not require a website redeploy.

## One time setup

1. Deploy the website changes from the repository through the existing Vercel project. Create a Global Config connected to that project’s Production environment and redeploy once so the server receives its read-only `GLOBAL_CONFIG` connection string. Keep the existing working `RIGMD_RELEASE_VERSION` and optional `RIGMD_DOWNLOAD_URL` settings as the fallback for the initial deployment.
2. In the RigMD GitHub repository, set the Actions variable `RIGMD_GLOBAL_CONFIG_ID` to the same config ID. If it belongs to a Vercel team, set `VERCEL_TEAM_ID`. Add an Actions secret named `VERCEL_TOKEN` with access to update that Global Config. Keep these values in the repository's existing secret and variable settings.
3. Confirm the live endpoint still returns the current working download while the config has no record. Run **Sync website download** with **Verify the release without updating the website** selected to check a published candidate.

## Publish a release

1. Build from a clean Git commit. Create the version tag from that exact commit.
2. Attach `RigMD-Setup.exe` or `RigMD-Setup-vX.Y.Z.exe`, plus the unedited `release-manifest.json` or `release-manifest-vX.Y.Z.json` produced by the installer build. Publish the release once both uploads finish.
3. The workflow checks the published tag, manifest source revision, installer size, GitHub asset digest and download URL. It writes the `rigmdDownloadRelease` record only after the checks pass. The same version and build can be rerun safely. Older versions and conflicting replacements leave the active download in place.
4. After publishing, the workflow checks the live download endpoint for the approved version and URL. If the record has updated but the endpoint still reports the previous release, check the production deployment and connection, then rerun. Confirm the page label and the Windows download agree.

The workflow skips GitHub prereleases by default. To use the site as the capstone prerelease channel, set the Actions variable `RIGMD_PROMOTE_PRERELEASES=true`. The release's GitHub prerelease flag controls this selection. The website includes a prerelease label when the promoted record carries that flag.

Use **Run workflow** from the default branch to verify or sync an already-published release. For a sync, provide the tag and clear the verify-only checkbox. This can also retry a run after a missing attachment has been uploaded. To roll back, restore the previous Global Config value for `rigmdDownloadRelease`. When a release fails verification, the workflow leaves that value in place.

## Current 0.2.21 release

The published tag `v0.2.21` resolves to `0837fd2`, and the installer manifest identifies `a164419` as the build commit. The public release also lacks the manifest attachment. Uploading the manifest alone will expose the source mismatch, so the workflow will continue to reject it.

Keep the published tag as a historical reference. Prepare a new version from a clean, reviewed commit, then publish the installer and matching manifest on a new tag that points to the build commit.
