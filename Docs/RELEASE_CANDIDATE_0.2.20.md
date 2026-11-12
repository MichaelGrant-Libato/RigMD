# RigMD 0.2.20 - network action and report evidence candidate

Prepared 13 November 2026 (Asia/Singapore). Release candidate; installed acceptance is pending. This is not a production-readiness certification.

## Changes

- DNS cache refresh requires a saved failed DNS lookup plus a reported active IPv4 adapter, address, gateway and DNS-server configuration. Live connection and lookup evidence are checked again before preview and execution. Offline adapters, missing connection settings, cleared lookup failures and unavailable evidence receive guidance rather than a DNS action. Blocked actions do not execute or consume approval.
- Share Report loads action history for the selected saved check only. Exports include the action name, recorded/finished time, saved run status, verification result, execution summary and available before/after evidence. Completion is separate from verification and later diagnostic readings. Missing history is labeled unavailable, with the legacy summary retained when present.
- Selection changes and refresh cancel superseded history reads. Export is blocked during loading; a late response cannot attach another check's actions. All report formats use the same report document, and stored text remains escaped in HTML.
- Printed reports explicitly use a light paper scheme and white page background. Long action evidence can continue across pages without splitting individual table rows.
- Version metadata is aligned at 0.2.20. No dependency, diagnostic threshold, startup-boundary or startup implementation change.

## Validation

Automated results and exact installer/source identity accompany this candidate. Browser evidence uses synthetic records; no real app closure, DNS change, service restart, Explorer restart, uninstall or personal-file deletion is performed by the agent.

## Outstanding acceptance

- 0.2.19 installed startup was 5.54 seconds against a 5-second target. This remains an open performance issue; this candidate does not claim to fix startup.
- The user's offline/reconnected network recheck is saved. Reconnection was manual, and DNS refresh was not established as the cause.
- Verify the new installed report, PDF save/cancel and history retention after backing up data. Prior 0.2.19/0.2.18 timings and exports remain observations of those versions, not 0.2.20 acceptance.
- Clean-machine and supported-machine acceptance, security/recovery evidence, disposable-environment Update/Explorer checks and final deployment remain pending. The installer is unsigned. The legacy RigMDAgent service is not installed or re-enabled by this candidate.
- No commit, push, installation or deployment is automatic.
