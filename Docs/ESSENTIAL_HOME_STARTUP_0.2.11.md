# Essential Home startup — working revision for 0.2.11

Status: implemented at the project owner's request on 10 November 2026. Formal SRS/STD acceptance wording and adviser approval remain to be reconciled. This document does not grant or claim adviser approval.

## Scope and readiness

| Observation | Completion boundary | What it does not establish |
|---|---|---|
| `startup.desktop`, mode `essential_home` | Desktop process start through native Home essentials, splash exit, React commit and two animation frames. CPU warm-up must finish or be explicitly unavailable; failed essential requests are incomplete. | Full inventory readiness, saved-summary completion, repair success or overall acceptance. |
| `startup.summary.desktop`, mode `essential_home` | The same process start through visible saved-check summary readiness and splash exit. Leaving Home, a failed summary or a hidden startup is incomplete. | An additional phase to add to the essential duration; it is another end-to-end total. No completed timing target is assigned to this observation. |
| Full device inventory | Loaded on opening My Device Info or preparing current device details for reports. Existing full provider implementation is retained. | It is no longer included in essential startup. Its on-demand wait still needs installed acceptance measurement. |

The five-second value remains the essential startup test goal. Versions up to 0.2.10 waited for the full profile and summary; their completed over-target observations remain over target. New essential timings are not a like-for-like improvement claim for the original full-inventory startup boundary. Before declaring the formal startup requirement passed, align the SRS, SDD, STD and RTM with the approved behavior and test protocol.

## Product behavior

- Home reads actual Windows processor busy time from native system-time deltas, actual physical-memory usage, mounted fixed-volume file space and local adapter configuration. Warm-up, unavailable values and failed requests are explicit. Measured zero remains zero.
- Temperature, GPU details, RAM-slot inventory, display inventory and detailed device/event observations are loaded in device details or checks. Home does not fabricate them. Network-link configuration does not certify DNS resolution, Internet reachability, latency or packet loss; a network check measures those.
- Native rate samples are primed while the API prepares its schema and recovery state. The splash has no fixed minimum hold; it exits after the essential result or explicit failure is ready.
- Full sensor streaming waits for an authenticated subscriber. Device details, reports and checks request streaming when needed; no-subscriber startup does not construct the expensive sensor service.
- Saved-check summary has a loading/error panel. Pending or failed history does not render a false first-use/no-alerts result. It has its own end-to-end timing.
- Reports wait for full inventory by default. If it fails, the user may retry or explicitly choose saved results only. The latter supplies no current hardware snapshot, so old current-device readings cannot be presented as fresh.
- WPF window construction overlaps the API readiness wait on the appropriate threads. The window still waits for local HTML/CSS readiness, and the process-start clock is retained.

## Data, security and evidence

The existing full inventory, diagnostics, actions, preview/approval gate, recovery checks and client isolation are retained. The new `/api/home/overview` route uses the existing loopback/token/origin middleware and standard rate limit. Tests demonstrate unauthorized requests are rejected (401), untrusted origins are rejected (403), and an authorized overview does not resolve full inventory or sensors. No new action-execution route is added.

No saved checks or older timings are reset. New launches record `essential_home`; version 0.2.11 separates their setup group from older versions. Startup timing still describes observed software latency, not diagnostic accuracy or successful repair. The original process-start definition does not include a person's delay in an external UAC prompt.

The 0.2.10 installed baseline was 8.87 s at 02:07:41 local time on 10 November 2026 (0/1 within five seconds). Its displayed phases were service wait 3.96 s, window preparation 0.47 s, Home load 2.90 s, API start 0.09 s; figures are rounded observations supplied by the tester. That full-profile observation remains a failure of the original target.

## Validation and documentation actions

- Automated Release regression: 1,176 backend tests and 306 frontend tests passed. Production UI compiles; the existing bundle-size and legacy `any` warnings remain.
- Browser regression uses synthetic visible fixtures and a separate browser/API instance. It covers CPU warm-up, no full inventory/streaming on Home, explicit pending summary, a separate summary event, deferred inventory, failed-report refresh, saved-only fallback, retry, mobile width, and essential failure recorded as incomplete. Fixture screenshots are UI evidence, not measurements of this PC's hardware or participant results.
- The API benchmark uses a private copy of saved history and excludes WPF/WebView initialization and actual desktop painting. It is not an installed startup pass. Installed timings for 0.2.11 are pending.
- Retest first launch, repeat launches, unavailable/permission-limited readings, on-demand device details, saved-history access, report preview/PDF, and controlled offline behavior. Retain slow, failed and incomplete observations.
- Update the feature matrix, RTM and STD with separate essential, summary and full-inventory boundaries. Record version, installer hash, timestamps, conditions and observations before calculating acceptance.
- Existing user-survey findings concern the version and workflow actually evaluated. Do not attribute those responses to the changed 0.2.11 flow without follow-up evidence. This startup revision does not resolve the separate custom-survey/SUS criteria question.
