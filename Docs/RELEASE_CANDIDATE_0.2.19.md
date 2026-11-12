# RigMD 0.2.19 - installed-test follow-up candidate

Prepared 12 November 2026 (Asia/Singapore). This is a release candidate, not a production-readiness certification.

## Changes

- Selected-app preview and execution now distinguish `memory_warning_cleared` from `memory_reading_unavailable`. A cleared warning includes the actual available usage percentage and the unchanged 82% review boundary. Missing/invalid measurements never appear as cleared readings or fabricated percentages.
- Existing hardware-inspection, saved-warning and scope guards remain enforced. Requests rejected before closing apps do not consume approval or save a completed remediation run. Stable reason codes and preview/execution stage are returned to the client and written to the API information log, without approval tokens.
- Past-action proof labels, descriptions and detail controls now use explicit readable colors in the dark modal. Historical process observations remain separate from current readings.
- Candidate version is aligned across frontend metadata, Desktop, API and installer. No dependency upgrade or diagnostic threshold change is included.

## Evidence and limits

The full backend regression suite passed 1,331 tests, including 17 additional cases for distinct reasons, invalid/unavailable evidence, active warning boundaries, no execution/persistence on guard rejection and approval preservation. Frontend and packaged-browser results accompany the frozen candidate.

The user's installed 0.2.18 evidence includes five completed launches, four within 5 seconds (median 2.23 seconds; longest 5.62 seconds); two eligible 50-check loads within 3 seconds; report preview 0.60 seconds; PDF generation/write 0.18 seconds; retained history/feedback; approval/cancellation tests; and the connected-network scenario. Those observations are not relabeled as 0.2.19 results.

The first 0.2.18 selected-app request returned HTTP 409 before close calls. Its combined message did not preserve whether memory pressure cleared or the reading was unavailable; this cannot be reconstructed retrospectively. The second request returned HTTP 200 and sent normal close requests for Chrome and Discord. Recorded background processes remained (Chrome 22 to 10, Discord 6 to 6). A separate recheck measured 70.8% memory. A completed request is not proof of full exit or hardware repair.

The old RigMDAgent service was manually stopped/disabled during user acceptance. The current installer does not install it or automatically retire leftovers from older architectures. The current app's Desktop/API functions were checked with that service disabled.

## Still required

Install and smoke-test this candidate after backing up user data. Verify retained checks/feedback, readable Past actions and exact current-reading messages. Repeat startup on fresh supported test environments. Startup's prior 5.62-second outlier and the 5.51-second normal 10-row history load remain recorded performance concerns. Disconnected-network behavior, clean-machine deployment, and actual Update-service/Explorer recovery acceptance on a disposable environment remain pending. The installer remains unsigned. Printed-report dark margins and limited recorded-action identification remain presentation/evidence issues. Final website/download deployment must match the frozen installer version and hash before distribution.

No real app closure, service stop/start, Explorer restart, uninstall or personal-file deletion is performed by this candidate's automated verification. No automatic commit, installation or deployment.
