# RigMD 0.2.9 release candidate

This identifies local candidate code, not a published or installed-release certification.

## Changes

- Uninstall and personal-file/Recycle Bin operation intents and receipts are stored in local SQLite before Windows work is launched. A unique database index prevents two active storage intents from separate service/store instances. Receipt payloads do not store approval tokens or executable uninstall targets. Terminal history is bounded; interrupted records are retained.
- On API restart, interrupted work is unverified and never replayed. Further mutations are blocked while a prior action may still be running. Windows' LastBootUpTime is used to distinguish a later Windows restart; an unknown boot cannot clear the lock. A later restart releases the pending-process restriction, not a claim that removal succeeded. Fresh previews and current Windows evidence are still required.
- Durable receipts cover actions started by 0.2.9 and later. Lost operation records from older builds cannot be reconstructed by this upgrade.
- Failed intent persistence prevents execution. If saving a final result fails, the observed result remains visible with a warning, further changes are blocked, and the action is not repeated. A damaged/unavailable journal keeps the app read-only for mutations.
- The unsafe, unscoped direct PostgreSQL import is disabled. Cloud AI/account proxy access remains separate from local diagnostic history.
- Desktop/API/package/installer version checks use frontend/release.json as the release baseline. The website uses the server's version-checked download response and has no generic latest-release fallback.
- Installer builds use fresh isolated publish directories and record version/hash/source/signature metadata. Prior published build directories are retained.

## Validate

From PowerShell in the repository: `powershell -NoProfile -File installer\validate-release.ps1`.
On an appropriate Windows test machine also run with `-IncludeNativeJunctionTest`. Default validation explicitly excludes that environment-blocked fixture and records the exclusion. Existing dependencies, .NET 10 SDK and supported Windows are required. Automated validation is not installed acceptance.

Build the installer with `powershell -NoProfile -File installer\build-installer.ps1` after installing the Inno Setup compiler. Record output/release-manifest.json and the exact source revision. Complete clean install/upgrade/uninstall and actual Windows prompt/cancel/interruption acceptance on disposable test data/apps before public distribution.

## Publish only after acceptance

1. Commit/review the intended source, including new files, and identify the tested candidate.
2. Upload the matching RigMD-Setup.exe and manifest/hash to the v0.2.9 GitHub release. Verify its bytes and publisher/signature status; a hash is not a publisher signature.
3. Set Vercel `RIGMD_RELEASE_VERSION=0.2.9`. Remove the stale `RIGMD_DOWNLOAD_URL`, or set it exactly to `https://github.com/MichaelGrant-Libato/RigMD/releases/download/v0.2.9/RigMD-Setup.exe`.
4. Deploy the matching website/API. The new download endpoint returns unavailable if the enabled version or override differs. Confirm real account/download/desktop/cloud flows separately.

This work does not automatically publish, upload, install software, execute cleanup/uninstall operations, obtain a signing certificate or establish expert/user evaluation results.
