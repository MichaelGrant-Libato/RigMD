# RigMD diagnostics reliability plan

Prepared November 27, 2026. The plan starts from `main` at `669a72e`. The recon also reviewed the separate Installed Apps prototype at `c729110` on `feature/installed-apps-in-storage`. This branch starts with the plan for review. Implementation can follow in focused commits after the scope is agreed.

## 1. Verify and clarify Gemini explanations

**Finding.** The ten most recent saved checks reviewed on one PC used built-in guidance. The deployed proxy responded and required sign-in. The reason for the fallback is still unconfirmed. Settings shows account status, and the saved check shows which source supplied the explanation. Neither shows why a cloud explanation was unavailable.

**Work**

- Run one authenticated staging check with synthetic diagnostic data and a dedicated verified test account. Use the server-side outcome to locate any failure in sign-in, quota, proxy configuration, provider response, or response parsing.
- Save and display the Windows check as soon as the local result is ready. Give the optional cloud explanation a bounded total time across retries. Attach a successful explanation to the same saved check.
- Keep account status in Settings. In Technical result details, record whether Gemini supplied the explanation and, when known, a short fallback reason. Use fixed messages for local-only mode, missing sign-in, rate limits, timeouts, and service errors.
- Decide when a check should request Gemini and align the Settings wording with that behavior. Review which fields the proxy receives and retain just the evidence needed for the explanation.

**Acceptance.** One authenticated test returns a complete explanation from the deployed provider and the saved check identifies it as Gemini. Signed-out and local-only checks avoid cloud requests. Simulated quota, timeout, and failure cases keep the local check usable and show the corresponding state. Reopening the same check preserves its explanation and source.

## 2. Improve the evidence behind device checks

**Finding.** Several checks collect status or a snapshot and mark the actual function unverified. Some Help descriptions imply a deeper diagnosis. Implement one evidence source at a time, starting with boot, battery, and storage.

| Check | Planned work | Acceptance |
| --- | --- | --- |
| Slow Boot | Read available Windows boot history and show the recorded duration and date. Show per-app startup impact only where the source supports it. | Compare readings with the matching Windows record. Missing or unreadable history is labelled unavailable. |
| Rapid Battery Drain | Use the existing Battery Health collector to make design capacity, full-charge capacity, and recorded discharge available in the scenario. | Compare values with a Windows battery report on a laptop. Explain the difference between charge, wear, and time-based drain. |
| Storage | Link a storage finding to Drive Health for the correct physical drive. Preserve separate labels for volume space and reliability counters. | Verify drive and volume mapping on a multi-drive PC. Distinguish permission denial, missing counters, and a measured zero. |
| App crashes and What Changed? | Show the inspected event sources and time window. Link the saved check to relevant events and preserve the limit notice. | Compare known update, driver, and crash events with Windows records. A historical event supports an investigation. A cause requires further evidence. |
| Windows files, audio, USB, display, and cooling | Update Help and add a short way to open the relevant Windows tool or follow a functional check. Keep physical or user-observed checks labelled unverified until the user tests them. | Each incomplete result names the missing evidence and presents the applicable next step. Any repair uses the existing approval flow. |

Reuse the diagnostic panels in the relevant check results before adding agent tools. Evaluate a new tool by the gap it closes and the evidence it can return. The number of registered tools is a separate measure.

## 3. Make results and repair claims consistent

- **Share Report.** Include the saved explanation source and the model when recorded. Export the specific saved next step. Older records can fall back to the action category when the recommendation is missing. Verify the same fields in preview, copied text, download, and PDF.
- **Action outcomes.** Review each supported action's preview, approval, execution receipt, recovery behavior, and recheck. Keep the documentation specific about which operations can be reversed. Verify cancellation, permission denial, interrupted actions, and unknown outcomes on disposable test data.
- **Documentation.** Remove the obsolete claim that `DATABASE_URL` enables background history sync. Align README and Help descriptions with verified scan coverage and the actual account and fallback behavior. Record corresponding changes for the SRS, SDD, and STD against the current agreed scope.

## 4. Validate hardware coverage and the release

- Compare CPU, memory, storage, graphics, and network readings with the corresponding Windows views near the same time. Cover idle, a steady workload, startup, and sleep or resume. Use a desktop with dedicated graphics, a device with integrated graphics, and a battery-powered laptop. Record the Windows build, app commit, hardware, and which readings were unavailable. Include an AMD and an Intel laptop as devices become available.
- Keep Installed Apps validation on its own branch until the inventory is reviewed. The initial desktop sample returned 155 entries, 121 logos, and 20 descriptions. Check the list against Windows Installed apps, including apps with missing locations, different Windows accounts, and multiple drives. Document registered path, estimated size, and metadata availability in any release that includes it.
- Build the next installer from a clean reviewed commit. Point the new version tag at that commit and attach the matching installer and manifest. Verify the website's version, URL, and installer digest before promoting the release. The current `0.2.21` download works. Its manifest and tag mismatch are documented in the release sync notes.
- Test clean installation, upgrade with saved history, offline startup, uninstall, and installer trust prompts. Record the measured startup time against the agreed target and report unresolved results alongside the installer version and build commit.

## Review order

1. Confirm the Gemini failure stage and the status we want the tester to see.
2. Review the boot, battery, and storage work as separate changes with their Windows comparisons.
3. Reconcile the report, repair descriptions, and research documentation with the verified behavior.
4. Review the exact installer, website download, and acceptance evidence before deciding whether to merge.

## References

- [Cloud AI deployment and acceptance](CLOUD_AI_DEPLOYMENT.md)
- [System consistency audit](SYSTEM_CONSISTENCY_AUDIT.md)
- [Remediation policy](REMEDIATION_POLICY.md)
- [MVP acceptance status](MVP_ACCEPTANCE_STATUS.md)
- [Website download sync](WEBSITE_DOWNLOAD_SYNC.md)
