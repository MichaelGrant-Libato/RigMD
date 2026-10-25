# MVP Acceptance Status

Reviewed October 25, 2026 against source commit `cfbd76c` plus the uncommitted presentation/public-site changes described below. This is an evidence checklist, not a release approval, accuracy score, or completion percentage.

## Baseline Correction

The user clarified after this review that the SRS and SDD have not been updated yet. They are historical references, not the current approved MVP baseline. The 53 identifiers below are a provisional crosswalk from the supplied documents/inventory, not confirmation that the present MVP has exactly 53 agreed requirements. Their timing, survey, evaluation and deployment targets need confirmation before being used for acceptance decisions.

Use the latest explicitly agreed product behavior and observed test evidence for current implementation work. Do not add features solely to satisfy an outdated diagram or infer a current product defect from a draft-document mismatch. The verified fixes and test results below remain valid within their stated scope; they do not prove compliance with an as-yet unconfirmed current baseline. Website checklist additions are supplementary, not newly imposed desktop MVP requirements.

## Decision

The main MVP workflows are implemented and have substantial automated coverage. Whole-MVP acceptance **cannot be established from these outdated documents alone**. Confirm the current scope before assigning overall completion or acceptance status. Do not equate a passing build, the inventory's percentages, or a successful close request with verified hardware diagnosis, completed app exit, or a release-ready installer.

The next user check is read-only: reopen an existing saved action and confirm that its process observations are explicitly historical. Do not close more apps or run Windows repairs merely to collect another screenshot.

## Baseline And Evidence

- Historical requirements: the locally supplied, not-yet-updated `RigMD - SRS .docx (2).pdf`, sections 3.2-3.5 and 4, and `RigMD - Requirements_Inventory.xlsx`, Requirements Inventory and Traceability Matrix. These references contain 11 FR, 31 NFR, and 11 VR identifiers; current approval of the inventory was not established. This review does not approve or overwrite their existing percentage/status fields.
- Historical design: the not-yet-updated `RigMD - SDD (2).pdf`, including the architecture and SRS-to-SDD mappings. The user's later clarification supersedes the earlier assumption that SRS/SDD could define the current baseline. They are not raw survey responses.
- Test plan and reported results: `RigMD - STD (6).pdf`. Its reported outcomes are claims to reconcile, not fresh test results for this source revision.
- All four local reference files were downloaded/modified October 22. That file date is not evidence of formal approval or of new survey data; the SDD still references September validation.
- Existing source audits: `Docs/SYSTEM_CONSISTENCY_AUDIT.md`, `Docs/REMEDIATION_POLICY.md`, `Docs/CLOUD_AI_DEPLOYMENT.md` and their dated verification limits.
- Native acceptance supplied in this conversation on October 24: cancellation did not close Chrome; a Discord close request left background processes; manual tray Quit removed Discord; a later Chrome process query found no processes; other unselected apps remained available. Saved action observations and follow-up readings were distinct. These are user-reported tests on one laptop, not a hardware matrix or proof of permanent memory improvement.

Reference SHA-256 values identify the exact local documents reviewed:

| Reference | SHA-256 |
| --- | --- |
| SRS | `D511BB70695E1C9EC8544A0CAECF8170EE9AF45C64F32203C1E0235AE0302685` |
| SDD | `072C50D302B9FF6F1B3DAC536ACA865789E0848CC0F2A0074FB083BFF10BECEC` |
| STD | `D871F9F7645E5335A16D5E82F8B25EA9D6E70B93A0650740A0AFC46AEF455344` |
| Requirements Inventory | `F2866C9D8C471B026B04CD28B66BC3BE5A67290FDB4058BEB61791465796F749` |

## Corrections In This Change

- Render legacy and new per-app observations as what was observed at the action check. Explain that later memory rechecks do not update that saved process observation. Preserve original statuses, numbers, action history, execution controls, and the original diagnostic result.
- Add distinct public-route titles/descriptions and a missing-page view with working return/login links. Desktop unknown routes keep their original behavior. This is a client-side missing-page view; the current SPA rewrite still returns HTTP 200. Do not call it a verified server-side 404 response.
- Handle rejected sign-in/registration promises with a visible, announced connection error and restore the submit button. Existing validation and confirmation states remain.
- Present the account form before introductory content on narrow screens; preserve the desktop arrangement. No sticky overlay is needed to reach the form.
- Correct the public download copy that described the current local worker as an installed Windows service. This does not alter runtime behavior or install a service.

## Functional Requirements

"Implemented; acceptance open" means code/tests exist, not that the full target-device/user evaluation passed.

| ID | Evidence in source/tests | Remaining acceptance |
| --- | --- | --- |
| FR-1 Profile | `SystemProfileView.tsx`; Windows providers; hardware/storage/temperature tests | Compare supported fields on representative desktops and laptops, including multiple drives and missing sensors. |
| FR-2 Full/component/scenario checks | `NewDiagnosisView.tsx`; scanner scope tests; prior native check screenshots | Repeat the agreed component/scenario matrix on the release candidate. |
| FR-3 Fresh evidence | Provider sample/availability tests; timestamps and explicit unavailable readings | Near-simultaneous Windows comparison, cold start, warm-up, sleep/resume, unavailable counters. |
| FR-4 Evidence-based interpretation | `AutomaticDiagnosisService`, `ScannerEvidencePolicy`; application tests | Expert-reviewed expected cause/action cases, not just successful scans on a healthy device. |
| FR-5 Explanation | Shared diagnostic-language/check presentation; current/recorded evidence separation | Nontechnical comprehension and expert assessment; no validated probability claim. |
| FR-6 Advisory categories | Deterministic classification and shared action presentation | Confirm visible advice matches saved scope and latest supported findings in each category. |
| FR-7 Safety and consent | Approval-store/controller/tool tests; user cancellation/background-app tests | Disposable-app permission denial, unsaved-work prompt, partial result, and failed-history-save cases. |
| FR-8 No arbitrary commands | Allowlisted tools, fixed arguments; security and executor tests | Independent review and packaged negative tests. Do not execute OS repairs for coverage alone. |
| FR-9 Report | `reportFormatting.ts`, `reportData.ts`; browser preview tests | Same-session copy/download/print/PDF comparison in the installed release. |
| FR-10 History | SQLite repository tests; saved proof in detail view; original/latest separation | Restart/upgrade/recovery and interrupted-write cases on test data copies. |
| FR-11 Repeated problems | `RecurringPatternService`; recurring-evidence tests | Distinguish separate episodes from repeated observations of one episode with expert/user review. |

## Non-Functional Requirements

| ID | Implementation/test evidence | Unclosed acceptance or limitation |
| --- | --- | --- |
| NFR-1 Startup <=5 s | Desktop builds and starts through the existing test launcher | Timed cold/warm installed launch on minimum supported hardware, dashboard usable rather than splash visible. |
| NFR-2 Responsive scans | Busy/progress states; browser workflows | Timed interactions while real telemetry is being collected. |
| NFR-3 >=50 history records <=3 s | Repository and history view | Timed target-device run with at least 50 local records. |
| NFR-4 Report <=5 s | Shared formatter and report tests | Measure end-to-end report preparation on target hardware. |
| NFR-5 Optional-AI fallback | Local interpretation and offline explanation paths/tests | Installed offline startup/scan/history/report and cloud-outage acceptance. |
| NFR-6 Clear errors | Input validation, failed-recheck preservation, public-form errors | Packaged negative cases and accessibility review of all error surfaces. |
| NFR-7 Unavailable backend/agent | Local transport guards and unavailable states | Real disconnection/recovery with no incomplete action; public auth startup/recovery still needs staging tests. |
| NFR-8 Preserve results | SQLite persistence and original/follow-up tests | Restart, upgrade, disk-full, permissions, corruption and interrupted-write recovery on copies. |
| NFR-9 Private credentials | Environment separation and installer exclusions; local API credential tests | Inspect the actual final installer and deployed assets; no live secret/configuration audit was performed here. |
| NFR-10 Authorized workflows | Loopback HTTP/SignalR authentication and approval checks | Independent release security review; same-user malware is not prevented by a loopback token. |
| NFR-11 Validate input | API/controller/schema/scope tests | Packaged malformed/oversized/unexpected-input cases and external review. |
| NFR-12 Limited AI payload | Structured payload tests and constrained explanation path | Release payload inspection with consent, redaction and deployed provider behavior. |
| NFR-13 HTTPS deployment | Configured remote-service URLs; local loopback is a distinct transport | Inspect actual staging/production configuration and certificate behavior. |
| NFR-14 Allowlisted actions | Registry, scope/current-evidence checks and single-use approvals | Packaged unsupported/stale/changed-action rejection tests. |
| NFR-15 No arbitrary shell | Fixed executors and policy tests | Independent review; no unrestricted CMD/PowerShell entry point may be added. |
| NFR-16 Explicit consent | Selection-change reset, disabled action without approval, controller checks | Native keyboard/cancel/reopen and adversarial negative acceptance. |
| NFR-17 Plain language | Shared result wording; historical process observation fix | Nontechnical users must understand partial evidence and historical versus live state. |
| NFR-18 Guided workflow | Full/parts/problem selection | First-time target-user walkthrough without coaching. |
| NFR-19 Transparent basis | Separate readings, availability, timestamps, advice and details | Expert verification that each conclusion follows its own evidence and scope. |
| NFR-20 Readable desktop UI | Prior native zoom acceptance; responsive browser screenshots | Keyboard/focus, supported resolutions and Windows scaling on target devices. Browser emulation is not native input proof. |
| NFR-21 Modular design | Application/domain/infrastructure separation and architecture tests | Maintain boundaries during later release fixes. |
| NFR-22 Configurable deployment | Deployment configuration and environment separation | Confirm actual backend/auth/download settings. Public release link is currently a hardcoded repository URL, not a private secret. |
| NFR-23 Maintainable installer | Inno Setup/build script exists; isolated desktop Release build passes | Build, version, inspect and test the actual installer; no installer was produced or installed in this pass. |
| NFR-24 Timely telemetry display | Fresh sample/update paths and browser rendering | Measure real post-scan display delay, not mocked fixture response time. |
| NFR-25 Uncertainty | Independent availability tests; unavailable is not zero/normal | Native absent-device/unsupported-sensor matrix and expert review. |
| NFR-26 Clear action outcomes | Request acceptance, per-app proof, save result and condition follow-up are separate | Clear historical wording fixed here; complete supervised partial/failed/recovery cases. A normal close is not a force quit. |
| NFR-27 Installer trust | Official repository link and installation guidance | Exact release checksum, publisher/signature verification where available, AV/SmartScreen observations and clean-machine tests. Never advise disabling protection. |
| NFR-28 Public secret review | No private environment files loaded by this pass's isolated builds | Review final distributed assets/package and current dependencies. Cached restore with audit disabled is not a vulnerability audit. |
| NFR-29 Clear storage | Used/free/mounted capacity versus physical capacity; storage tests | Confirm understandable multi-drive and unavailable-volume presentation on hardware. |
| NFR-30 First-time guidance | Help view and guided choices; account form first on mobile | First-use comprehension review. Existing help claims should be checked against current supported evidence, not assumed fulfilled because a help page exists. |
| NFR-31 No-warning feedback | `getDiagnosticOverview`: "No warning in the checked readings"; coverage notice remains independent | Accept scoped normal confirmation, not an unsupported "everything healthy" certificate. Missing/unchecked items must remain explicit. |

## Validation Requirements And Document Conflicts

The following targets and score discrepancies come from unupdated reference documents. Treat them as provisional validation/documentation follow-ups, not proof that the current application failed an approved acceptance test. Confirm the current evaluation plan and dataset before scoring or declaring a release blocker based on these rows.

| ID | Required evidence | Current audit status |
| --- | --- | --- |
| VR-1 | At least two knowledgeable evaluators, at least 20 controlled scenarios | Completed expert sheets and expected/actual decisions not supplied for this revision. Open. |
| VR-2 | At least 10 target users completing SUS | Raw item-level responses and inclusion/exclusion rules not supplied. Open. |
| VR-3 | Paired pre/post decision-confidence responses | Reconcile original data and the SRS target of >=25% improvement; do not substitute a post-only feature rating. Open. |
| VR-4 | Qualitative feedback on explanations, trust, workflow and history | Documents contain summaries, not a verified current anonymized feedback dataset. Open. |
| VR-5 | Safety, consent, allowlist, reporting and verification | Automated tests and scoped native examples support this; not complete all-action acceptance. Partial. |
| VR-6 | Expert assessment of evidence-supported cause/action | Code tests do not replace evaluator judgments. Open. |
| VR-7 | Current participant-based MVP validation | User context identifies 30 current October 14-22 responses; the raw current data is not available in these references. Open. |
| VR-8 | SUS benchmark/target comparison | SRS/inventory cite 73.1; STD cites 73.88 and 29 complete profiles. Neither score reaches 75. Do not call the >=75 target met or silently replace it with 68. Open. |
| VR-9 | CPU/RAM/OS/storage/driver validation | The documents' reported percentages need their underlying checks and build/device identifiers. Not a universal accuracy claim. Open. |
| VR-10 | Setup-friction observations | Retain observations; reconcile counts and test the actual new installer. Open. |
| VR-11 | Supported remediation completion | Documents' 21/21 execution claim cannot prove full app exit, repaired hardware or permanent improvement. Raw per-action outcomes and release-candidate checks needed. Open. |

Additional conflicts requiring document-owner review:

- The SDD glossary/deployment table describes an installed auto-start Windows service and a September survey. Current telemetry/action services run within the local API process; the installer inspected here packages Desktop/API and does not install that service. Align the design with the intended deployment, rather than implementing a new service solely to match an old diagram.
- The inventory's percentages mix implementation estimates, participant ratings and test claims. They are not an independently calculated acceptance rate and were not changed by this review.
- Preserve the user's current dataset selection. Do not reuse superseded participant results, recalculate SUS from a different rating scale, or publish the reported percentages as independently verified facts.
- Reconcile STD case-to-VR mappings with the inventory definitions before recording sign-off; a test name alone does not establish correct traceability.

## Website Checklist Decision

This is a supplementary public-download-site checklist, not a replacement for the desktop MVP requirements.

| Item | Decision for RigMD |
| --- | --- |
| 1. Custom 404 | Client missing-page view added. A true HTTP 404 still needs hosting/routing design and deployed verification. |
| 2. CTA above fold | Download command exists; account form moved first on mobile. Automated check at 390x844 confirms the registration command is in the first viewport. Other sizes/translated text still need normal review. |
| 3. Page title | Added per public route; base shell no longer says "frontend". Desktop shell keeps a RigMD title. |
| 4. Page description | Added generic per-route descriptions, with no email/token/query values. Client metadata is not server-rendered SEO. |
| 5. Open Graph image | Deferred. Use an approved product image and confirmed public URL if a shareable public landing page is introduced; do not expose private account details. |
| 6. Favicon | Existing RigMD ICO is referenced by HTML and matches the desktop/installer asset. The unused SVG is a template asset, not approved RigMD branding. The new page uses the existing Activity brand icon. |
| 7. robots.txt | Indexability decision open. Current entry pages are account/verification/download flows; do not treat them as marketing pages or claim robots rules enforce privacy. |
| 8. sitemap.xml | Defer until a canonical public domain and genuinely indexable pages are agreed. Do not list authenticated downloads or verification/token URLs. |
| 9. Image alt text | No new content image added. Existing icon-only controls/images need the ordinary accessibility audit; decorative marks should not duplicate adjacent text. |
| 10. Mobile breakpoints | Narrow browser workflows checked, including account errors, confirmation and desktop history. Not proof of every device/viewport. |
| 11. Sticky mobile CTA | Not necessary for the form-first flow; avoid adding an overlay that hides controls. Reassess only from real usability evidence. |
| 12. Loading states | Existing submission spinners, disabled buttons, access checking and desktop busy states retained. Registration busy state tested; release/staging outage tests remain. |
| 13. Form errors | Existing validation retained; rejected network promises now show an announced error and restore submit controls. Real service cases still need staging acceptance. |
| 14. Thank-you page | Existing check-email/verified states serve confirmation; another generic page is unnecessary. Mocked registration confirmation tested. |
| 15. Privacy policy | Owner-reviewed data-handling notice needed before broader public collection: actual account data, local history, optional AI/sync, retention/deletion, providers and contact. No invented policy or legal compliance claim added. |
| 16. Terms | Owner/adviser review of actual release/license/support terms remains. Do not fabricate a legal entity, guarantees or jurisdiction. |
| 17. Cookie banner | Do not add by default. First establish actual storage/tracking and applicable consent requirements with the owner; a cosmetic banner is not a compliance assessment. |
| 18. Analytics | Not an MVP requirement. No analytics/tracking added. Add only with an agreed purpose, data-minimization and appropriate notice/consent. |
| 19. Real contact | Owner must approve a real monitored support contact and any required identifying details. Do not publish an invented address or a student's private home address. |

The download page's account guard protects navigation to that page, not the public GitHub release URL itself. Do not present it as protection of the executable or as proof that the installer is signed.

## Verification Of This Change

- 124 frontend tests passed, including five new presentation/metadata tests. TypeScript check passed.
- 774 backend tests passed in an isolated HEAD source export; no backend execution/persistence code changed in this pass.
- Isolated desktop Release build succeeded with zero warnings/errors. Actual installer acceptance is still open.
- Production desktop/public frontend builds succeeded without loading private environment files. The desktop large-chunk warning remains; these builds are not startup benchmarks or dependency audits.
- Mocked browser checks: three desktop layouts (1440x1000, 1024x768 at effective 200% zoom, 390x844), saved historical closed/still-observed/unverified app proof, action-preview cancellation, report/original/latest agreement, and 14 public-route/layout cases at desktop/mobile widths. No browser exceptions or horizontal overflow in the tested cases. Relevant screenshots were visually reviewed.
- Browser authentication was mocked: no account was created, email sent, installer downloaded, real scan executed or app closed. Browser artifacts are not production auth or native-input acceptance.
- No commit, push, merge, deployment, real history edits, Windows settings changes or installer execution. Existing generated API web assets were left alone.

## Next Acceptance Order

1. Rebuild and perform the read-only saved-history check in the accompanying local test steps. Confirm that the old numbers are unchanged and the text says "at the action check".
2. Confirm the current agreed MVP scope, then update/reconcile SRS/SDD/STD/inventory accordingly. Confirm which evaluation targets still apply before comparing the current anonymized responses. Record dataset dates, exclusions, formulas and limitations. Do not edit reported scores based on guesses or reimpose obsolete targets.
3. Record expert controlled-scenario and first-time-user evidence. Keep unsupported sensors and expected no-battery desktop cases in the test matrix.
4. Time installed startup, >=50-record history load and report preparation on target hardware. Record build, OS, hardware, start/end definition, repeated timings and expected/actual results.
5. Build and inspect a versioned installer, verify its exact hash/publisher, test clean install/upgrade/uninstall/offline startup and WebView2 on the supported Windows matrix. Do not use a production history database for recovery/failure tests.
6. Before broader public distribution, approve the real contact/data-handling/support details and verify staging authentication, authorization, optional-AI fallback and quotas. Keep optional shared-credential database sync disabled until isolation/deletion/recovery are reviewed.
7. Review the scoped changes and current CI on PR #88. No automatic merge or release follows from this checklist.
