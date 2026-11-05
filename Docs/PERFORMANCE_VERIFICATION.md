# Local performance evidence

This recorder supports repeatable performance verification. It does not establish diagnostic accuracy, confirm a repair, replace stakeholder validation, or declare that the SRS objectives have been met.

## What is recorded

| Operation | Start and end | Interpretation |
| --- | --- | --- |
| Desktop startup | Process start (initial elapsed time), then monotonic clock until the dashboard has loaded hardware/summary data, the splash has exited, and two animation frames have elapsed | Includes API bootstrap and WebView initialization. An initial data error or backgrounded page is incomplete. A launch that never reaches readiness can leave no startup record. |
| Scan screen | Start-check handler through saved session loading and two frames of the result screen | Missing/not-assessed readings mark the run incomplete. A failed scan records failure. Navigating away can leave a pending run, recorded incomplete after ten minutes if the page remains open. |
| Backend requests | Authenticated request entering timing middleware through response completion | Separate entries for scans, saved-history reads, rechecks and action requests. HTTP completion is not proof that a repair completed or worked. |
| Saved-check screen | Refresh starts through loaded records, React commit and two frames | Counts rows actually rendered, not the database total. Trends loading is incomplete for this operation. |
| Report preview | Refresh, saved-check selection or history-toggle handler through document preparation, React commit and two frames | A selected saved check is required for a completed target observation. Includes retrieval when refresh is used. |
| PDF | Separate browser request duration and native generation/write duration | Browser request includes the Save dialog; native generation excludes the dialog and ends after the complete file replaces its destination. Canceling the dialog produces a canceled browser request. |
| Text download | Download handler through issuing the browser download | Does not establish that the user saved a file. |

Screen-ready is a rendering proxy, not a human usability measurement. Frontend timers use `performance.now`; native/backend timers use `Stopwatch`. A hidden page at any point makes an otherwise completed frontend run incomplete. Timings over 24 hours and malformed metadata are rejected. PDF and text contents are unchanged.

## Startup stage measurements

New desktop builds also record four stages under **Technical details for testing**:

| Stage | Measurement boundary |
| --- | --- |
| Starting the local service | The call that cleans up stale API processes and starts the API process. |
| Waiting for the local service | The readiness checks after process launch, until the local API and frontend assets are available. |
| Preparing the app window | The window Loaded handler through WebView initialization and setup, before navigation. |
| Loading the Home screen | Navigation through dashboard readiness, including initial data loading, splash completion and rendering frames. |

The existing process-start-to-dashboard-ready measurement and its five-second SRS target are unchanged. These stages identify where time is spent; they have no individual SRS targets and must not be added together as a replacement for the total because other startup work occurs between their boundaries. Durations are captured before recording so record writes do not inflate another stage.

Older startup records have no stage breakdown. Test a newly built desktop host and inspect its build context separately from the installed release. A launch that never reaches dashboard readiness can still leave no stage records. Adding these measurements does not itself make startup faster or prove the cause of a delay.

## Using Settings

1. Run a build containing both the updated API/frontend and updated desktop host. An older installed host cannot provide startup or native PDF timings.
2. Use the app normally: launch, run a check, open Past Checks, refresh a report, and save a PDF. Only approve software changes if independently intended; an action is not required for performance testing.
3. Open **Settings > App speed > Show results** (or **Refresh results** after loading). Recording is asynchronous; refresh again if the latest operation is still being written.
4. Review the three plain-language cards. They show the latest eligible completed timing, its target, all eligible pass/over-target counts, and incomplete/failed/canceled attempts. If multiple setups exist, select **Results to show**. **Technical details for testing** retains the full build/device context, median, 95th percentile, slowest durations and recent entries.
5. The user interface has no timing-data download. The team can review retained samples and logging-health counters through the existing authenticated local `GET /api/performance` endpoint during developer verification. Its existing loopback authentication and origin checks still apply; do not expose the endpoint or copy credentials into evidence. Save testing evidence separately from the everyday user workflow. No survey is involved.

## SRS timing observations

The current thresholds are startup â‰¤5 seconds, at least 50 saved checks displayed â‰¤3 seconds, and a selected report prepared â‰¤5 seconds. Other operations have durations but no invented acceptance thresholds.

For history, clear filters in **Past Checks â†’ Saved checks**, then use **Show 50 checks** (requires at least 50 existing records). This refreshes real history and displays up to 50 actual rows. It does not create diagnostic records. Runs displaying fewer than 50 cannot count toward the history target.

For acceptance, use the agreed minimum-spec Windows test PC, a Release build and the exact released source revision. Record hardware/specification and test conditions separately, including internet/cloud mode, first launch after restart versus subsequent launches, cache state, background applications, power state and dataset size. Avoid mixing test conditions. Record the exact commit and installer SHA256 alongside the retained timing evidence; assembly version alone cannot distinguish uncommitted builds carrying the same version.

Decide the sample size before testing (for example, ten cold launches and twenty warm repetitions per scenario). Keep failed/slow runs, and publish actual counts, median, p95, maximum and threshold observations. Repeating one successful run is not sufficient evidence for every supported device. Surveys, diagnostic accuracy against a reference, repair verification and accessibility remain separate evaluations.

Median is the central value (average of the two central values for even sample counts); p95 uses the nearest-rank method. Completed and incomplete runs contribute durations; failed/canceled runs retain outcome counts but do not enter those duration summaries. Only completed eligible runs enter target counts. Inspect individual samples when separating partial scans from full scans or different engine modes. The UI groups by operation, API assembly version/build configuration, computer pseudonym, OS, architecture and logical processor count; it does not automatically separate cold/warm or workload conditions.

## Storage and safeguards

- Records stay in the local SQLite database, with an idempotent schema upgrade for existing installs. Up to 2,000 newest entries are retained per local client identity. They are not uploaded by the cloud-sync service.
- A bounded 256-entry queue keeps database writes off the request path. The queue drains during normal host shutdown within the host's shutdown budget; force-closing or losing power can lose pending records. This is operational evidence, not a tamper-proof audit trail.
- Queue acceptance is not persistence. Health counters show accepted, saved, dropped and storage-failed entries for the current API process lifetime. Browser delivery failures cover the current page lifetime. Native delivery is best effort; missing native records require investigation and must not be treated as successful operations.
- Stored/exported context includes OS version, architecture, logical processor count and a pseudonym derived from the computer name. It is not guaranteed anonymous. No raw computer name, account email, credentials, diagnostic readings, report contents, request bodies, URLs, session IDs or file paths are stored in performance entries.
- Optional resource samples are the API process working set at request completion and API CPU time elapsed during that request. These include concurrent API work, exclude Desktop/WebView2, are not peak memory, and are not total PC resource use.
- Existing loopback authentication/origin checks protect `/api/performance`. Payloads use a fixed operation/outcome vocabulary, a 2 KB request limit and a separate 120-writes/minute allowance. Client messages cannot claim backend-only operations or resource values. The performance endpoints are excluded from automatic timing to avoid recursion.
- Failure to save a performance record does not fail a diagnostic operation or change its findings, action permissions, feedback or verification status. No extra Gemini request is made.

## Verification for this change

Automated coverage includes schema upgrades/retention/isolation, queue overflow and storage failure, authenticated rate limits independent of scans/actions, exact timing thresholds, actual displayed-record eligibility, nonfinite/invalid input rejection, trusted desktop message origins, cancellation, hidden-page handling, single completion and the two-frame boundary. A native Release installation still needs real-device startup/PDF measurements before making performance acceptance claims.
