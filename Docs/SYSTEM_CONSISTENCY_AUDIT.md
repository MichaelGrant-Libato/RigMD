# Whole-System Consistency Audit

## October 23 Scan-Scope Update

This update supersedes the earlier direct memory-action behaviour on crash-check screens. Historical results below retain their original dates and coverage limits.

- Memory actions require a full check, a selected-memory component check, or a relevant slow-system/freezing scenario, plus verified saved and current memory evidence. Explicit unrelated, invalid or empty scopes cannot gain access through the highest-priority diagnosis category.
- Incidental memory warnings appear under a collapsed Other findings section. Open memory check preselects memory in a new check; it neither scans nor executes automatically. Running it saves a separate session, leaving the original crash result and its follow-up intact.
- Original crash counts and follow-up counts use explicit time-window wording. A completed crash query has a separate evidence label from unavailable supporting sensors.
- No-new-records crash follow-ups use a neutral overall panel. Individual memory and unavailable-reading statuses remain visible.
- Preview and execution enforce the saved scope on the backend. Application selection, current evidence checks, explicit consent and single-use approval remain required. No force-close behaviour was introduced.
- Fresh verification: 748 backend tests, 88 frontend tests, TypeScript, isolated frontend build and Windows desktop Debug build passed. The frontend large-chunk warning remains. Browser workflow evidence and manual acceptance instructions are recorded in the conversation workspace's October 23 scope-consistency test notes.

Source fixes are not installed automatically. Rebuild the Debug app before manual acceptance. No real app closure, Windows repair, production-data modification, installer release or live AI request was performed during these automated checks.

Reviewed: October 21, 2026. This is a source and isolated-test audit, not production certification or a completion percentage. Existing staged and unstaged work was preserved. No real app was closed, cleanup executed, Windows repair run, production database changed, installer launched or live AI request made during this review.

## Rules That Must Agree Everywhere

1. The selected check remains the primary question. An Application Crashes check reports Windows crash records first; a memory warning is a separate finding with its own optional action.
2. Each measurement has its own identity, value, recording time and availability. Missing temperature cannot turn a verified memory reading into unknown, or turn a crash-record query into a failed query.
3. CPU at 80%, RAM at 82% and storage use at 80% are workload review policies, not universal hardware failure limits. Browser working-set size does not lower the RAM threshold.
4. Original checks are historical snapshots. Latest saved follow-up evidence is separate, timestamped, and compared against the original check, not silently substituted into it.
5. Initial seven-day crash history and records since diagnosis are different periods. Zero new matching records does not erase history or establish a repair. Failed queries cannot be displayed as zero.
6. Evidence availability is not a probability that a cause is correct. Older High/Medium/Low ratings remain unvalidated, not converted into newly verified confidence.
7. Preview, explicit consent, exact selected action/app arguments, current safety checks and a single-use approval precede a write action. Selecting an action or checking consent alone never executes it.
8. Both selected-app and legacy agent-selected app tools use normal close requests only. A request accepted, app exit observed, lower app working set and lower system memory are separate outcomes. No force-stop fallback or guaranteed RAM-freed claim is permitted.
9. Action execution, per-app outcomes, history-saving success and original-condition verification are separate. Saving failure after execution warns against rerunning just to save history.
10. A successful recheck stays available for later updates. Disable it while checking; show when the latest saved evidence was collected. Unsupported sensors do not become available through repeated clicks.

## Confirmed Corrections In This Pass

| Mismatch | Correction | Source |
| --- | --- | --- |
| Crash check headed by a RAM warning | Selected scenario heading, crash-record result first, other measurements in their own group | `frontend/src/lib/checkPresentation.ts`, `frontend/src/components/CheckReadings.tsx` |
| Header, latest panel and history used different outcome labels | Shared evidence-based outcome function, including separate memory status | `DiagnosticSessionDetailView.tsx`, `DiagnosticHistoryView.tsx`, `NewDiagnosisView.tsx` |
| Legacy resolved status could imply verified crash resolution without records/window | Missing, failed, contradictory or wrong-period follow-up evidence remains unverified | `checkPresentation.ts`, `frontend/tests/checkPresentation.test.mjs` |
| Comparison said "since last check" but used original baseline | Label now says "Compared with the original check" | Detail and new-result views |
| Historical advice appeared current after a follow-up | Original check and original advice explicitly labelled | Detail and new-result views |
| Tiny app-memory decrease could hide that the app still ran | Explicit per-app closed/still-running/unverified status before technical numbers | `AutonomyController.cs`, `AutonomyRemediationPanel.tsx` |
| Selected-app route did not save normal action history | Typed action result saved through the existing remediation repository, including execution proof; proof returned and displayed on reopening | `AutonomyController.cs`, `SessionModels.cs`, `DiagnosticSessionRepository.cs` |
| Older agent-selected path still force-killed apps | Force-stop fallback removed; tool description and preview now match normal-close behavior | `TerminateProcessesAction.cs`, `TerminateProcessesTool.cs` |
| Unknown report drives defaulted to SSD/Healthy | Unknown type/health stays Not available/Not assessed; warning takes precedence | `ShareReportView.tsx` |
| Export omitted separate latest outcomes | Selected check, original result, follow-up status/time and separate memory result included | `ShareReportView.tsx` |
| Home could say no warnings despite a live warning | Card now says "No saved alerts", distinguishing saved alerts from live readings | `AlertsSummaryCard.tsx` |
| Documentation promised wrong tool tiers and guaranteed telemetry | Corrected current README/policy; historical roadmap completion is no longer release acceptance | `README.md`, `REMEDIATION_POLICY.md`, `IMPLEMENTATION_ROADMAP.md` |

## Whole-System Coverage And Remaining Acceptance

| Area | Evidence inspected | Remaining acceptance, not established by screenshots or unit-test counts |
| --- | --- | --- |
| Full/component/scenario scanning | Scanner and resolution tests; existing scanner audit covers 13 components and 11 scenarios | Repeat on a representative Windows hardware matrix; unsupported sensors and absent desktop batteries are expected cases |
| CPU/RAM freshness | Shared threshold/sample tests, Windows readers and timestamped evidence | Compare steady workloads against near-simultaneous Windows readings, including warm-up, sleep/resume and unavailable counters |
| Storage/device/network/battery checks | Independent metric and missing-evidence tests | Multiple physical drives, removable disks, AMD/Intel, graphics variants, wired/Wi-Fi, offline/DNS failure, standard user/admin |
| Selected problem and evidence | Shared presentation tests and browser workflow | Nontechnical users must understand the primary result and distinguish unrelated findings without explanation |
| Controlled remediation | Preview/consent/single-use approval tests; real implementation reviewed; normal-close source guard | Supervised disposable-app tests, cancellation, background apps, permission denial, unsaved-work prompts and partial outcomes; do not run OS repairs just to exercise UI |
| Original versus latest history | SQLite round trips, original/follow-up separation, per-app persisted proof | Restart/upgrade recovery, disk full, file permission errors, corrupt data and interrupted writes on test copies |
| Repeated problems | Stable metric identity, distinct saved-check counting and risk-first service/tests | Review actual repeated episodes separately from repeated observations of one condition; never infer rising risk solely from scan count |
| Reports | Text/preview/download workflow; original/follow-up fields and unknown-drive defaults | Compare copied/downloaded/printed output with the same saved session; PDF export and page layout require installed acceptance |
| Local security | Loopback credential, HTTP/SignalR, direct-write rejection and approval tests | Independent security review; same-user malware is outside the local credential's protection; no blanket security certification |
| Cloud account/AI proxy | Existing source tests and scoped user-reported deployment evidence in `CLOUD_AI_DEPLOYMENT.md` | Retest staging authorization, quotas, fallback and sign-out for the release candidate; do not treat earlier scoped completion as all-system readiness |
| Optional database sync | `DatabaseSyncService` reads configured PostgreSQL tables at startup; installer source emits an empty `DATABASE_URL` | Release blocker if enabled with shared credentials: establish tenant/installation scope and review credentials, remote authorization, sync deletion/recovery. No live DB connection or secret was inspected |
| Packaging/distribution | Desktop build and installer source reviewed, not installed | Clean Windows install/upgrade/uninstall, WebView2 availability, supported OS/architecture, signing, antivirus scan, offline startup and recovery |
| Performance | Functional tests are not benchmarks | SRS save <=2s, history <=3s for >=50 sessions and report preparation <=5s need timed acceptance on target hardware (SRS pp. 47-48) |
| Academic validation | SRS pp. 51-53 inspected, not inferred from a survey-generation script | At least 2 expert evaluators, >=20 controlled scenarios and >=80% acceptance; >=10 target users with mean SUS >=75; measured decision-confidence improvement >=25%; retain raw anonymized results |

The SRS targets nontechnical Windows desktop users. Laptop screenshots do not establish desktop compatibility. A database, installer or survey file existing is implementation evidence, not successful evaluation. Prior hardening documents retain dated, scoped results; they are not current whole-system percentages.

## Next Acceptance Process

1. Run the latest Debug build, not an older installed shortcut. Preserve a backup of test history before install/upgrade testing.
2. Select Check My Device > Check a problem > Application Crashes > Check this problem. Confirm that the heading stays Application crashes check, regardless of the RAM result.
3. Read Crash check results first. Confirm the query period and count or the reason it could not be checked. Read memory/CPU/sensors under Other readings and findings.
4. Click Check Current Status. Compare the original and latest recording times. Zero records means only no matching records in that period; unavailable temperature stays a separate unknown.
5. Open Past Checks, reopen the same record, and export its report. Primary follow-up labels must agree; any memory warning remains a separate line. Recheck must remain available after success.
6. If the latest memory warning is supported, open Review apps using memory. Verify empty selection, explicit per-app warnings, selection-change consent reset and disabled execution without approval. Cancel for the read-only acceptance pass.
7. Only in a supervised disposable test, approve an unused app after saving work. Compare each app's result, not just the overall banner. Reopen the record to confirm the same app proof survives. Leave background apps alone rather than retrying repeatedly.
8. Complete hardware, recovery, packaging, performance and human-evaluation rows above. Record build identifier, Windows version, device configuration, expected/actual result and pass/fail evidence for each case. Only then calculate progress against an agreed requirement checklist.

## Verification Record

Final verification: 731 backend tests passed, 84 frontend tests passed, TypeScript passed, isolated frontend build passed and Windows desktop Debug build passed with zero errors. Mocked browser workflows passed at 1440x1000, 1100x850 and 390x844 with zero browser errors; desktop and narrow screenshots were visually reviewed. Checks included selection/consent reset, denied action, separate measurements, crash versus memory results, saved per-app history, report agreement, persistent recheck and failed-recheck recovery. The CRLF-aware whitespace check passed.

Tests use synthetic profiles, controlled local SQLite data and mocked browser actions. They do not certify every Windows device, a repaired physical fault or a permanent end to intermittent crashes. The real process-close success path was not executed: result construction/persistence and safety rejection were tested without closing actual apps. A source guard checks both app-close implementations for force-stop calls; supervised disposable-app acceptance remains required.

Known build warnings: NuGet vulnerability metadata could not be retrieved in the isolated build; the frontend bundle is large. Neither is evidence of a passed dependency-vulnerability audit or measured startup performance.
