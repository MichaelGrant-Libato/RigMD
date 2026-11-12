# Browser-cache cleanup scope correction

Date: November 9, 2026. This records source changes only; the installed app, previously generated portable archive, and published releases were not replaced.

## Problem and corrected behavior

The browser-cache preview previously honored a browsers selection, while execution ignored it and processed Chrome, Edge and Firefox. The tool now strictly validates exact browser identifiers and captures only eligible files in the selected cache folders. A missing selection includes all three supported browsers; an empty, duplicate, unsupported or incorrectly typed selection is rejected.

The server-issued approval holds a private snapshot across requests, expires after five minutes, and can be used only once. Re-previewing during execution reuses the original snapshot instead of enumerating new targets. Execution validates the browser set, account folder, cleanup-root identities and each file's identity, size and timestamps. File checks and deletion use the same held handle; directory handles protect parent chains. New files and profiles, changed files and inaccessible or locked candidates are not deleted. Replaced cleanup roots stop execution. Legacy execution without a matching snapshot fails closed.

Temporary-file and browser-cache deletion share the existing identity-checking executor. The temporary-file tool retains its current scope, age filtering, approval and receipt behavior. Both actions remain Tier 2, require explicit approval and report logical deleted bytes rather than guaranteed recovered drive space. No browser is closed.

Coverage remains limited to Chrome/Edge Default-profile Cache folders and Firefox local-profile cache2 folders. The preview discloses that other browsers/profiles are outside this review and that snapshots may be partial. Windows Update cleanup is a separate remaining audit item; it was not changed in this step.

## Files changed in this step

- Infrastructure/Remediation/Tools/Remediation/ClearBrowserCacheTool.cs: strict selection, private approved context and execution binding.
- Infrastructure/Remediation/Tools/Remediation/BrowserCacheSpecification.cs (new): browser argument validation and canonical selection.
- Infrastructure/Remediation/Actions/BrowserCachePlan.cs (new): discovery restricted to supported cache folders and bounded identity snapshot.
- Infrastructure/Remediation/Actions/ApprovedFileCleanup.cs (new): shared identity checks, held-handle deletion and accurate receipts, extracted from the existing temporary-file implementation.
- Infrastructure/Remediation/Actions/ClearBrowserCacheAction.cs: approved-snapshot execution and blocked legacy dispatch.
- Infrastructure/Remediation/Actions/ClearTempFilesAction.cs: delegates its existing approved plan to the shared executor.
- Tests/Infrastructure/BrowserCacheSnapshotTests.cs (new): 26 cases covering selection, approval, scope changes, cancellation, file changes, protected profile data, bounds, private context and native junction behavior.
- Tests/Services/Autonomy/AgentToolLayerTests.cs: empty or unverifiable real browser caches no longer imply an executable preview.
- Docs/REMEDIATION_POLICY.md: browser-cache selection, approval and deletion rules.
- This verification note.

Infrastructure and Tests paths above are under backend-dotnet/RigMD.Infrastructure and backend-dotnet/RigMD.Tests respectively.

## Validation actually performed

- First focused run: 45 passed; two native junction cases failed while creating disposable test junctions with Access is denied. No application assertion was reached in those two cases.
- Final backend regression run: 1,123 passed, zero failed, zero skipped. The two known environment-blocked native junction cases were explicitly excluded, not counted as passed.
- All 25 non-junction browser-cache cases passed in the final run. The additional native browser junction test is still pending on the user's Windows environment.
- The existing temporary-file snapshot tests, except its native junction case, passed after the executor extraction.
- Backend projects and tests compiled in Release. No new compiler warnings were emitted in the run.
- Scoped Git whitespace checks passed. Existing unrelated changes and generated frontend assets were preserved. No changes were staged or committed.
- Test execution used disposable fixture directories and isolated build outputs. No actual browser cache or personal files were cleaned. The frontend was not changed, so frontend tests were not rerun for this backend-only step.

Final regression evidence is in the Codex workspace at work/validation/browser-results/browser-cleanup-regression.trx.

## User verification and rebuilding

Close the running RigMD window yourself before launching a source build. In Command Prompt:

```bat
cd /d "C:\Users\Michael Grant\OneDrive\Documents\GitHub\RigMD-Project\backend-dotnet"
dotnet test RigMD.Tests\RigMD.Tests.csproj -c Release
dotnet build RigMD.Desktop\RigMD.Desktop.csproj -c Release
start "" "RigMD.Desktop\bin\Release\net10.0-windows10.0.19041.0\RigMD.Desktop.exe"
```

The unfiltered suite should discover 1,125 tests with the source at this step. Confirm that both native junction tests pass on this PC. If not, keep the full output and distinguish an environment failure from a failed application assertion.

For a non-destructive UI check, review browser-cache cleanup and verify the selected browser coverage, permanent-deletion notice, approval requirement and measured preview. Do not approve real cleanup merely to test the UI. Automated deletion checks already use disposable data.

Next incremental task: replace assumed RAM-slot and connected-display values with measured or explicitly unavailable states.
