# RigMD Hardening Progress

Updated: 2026-10-16

## How Progress Is Counted

Progress measures the explicit checklist below, not the percentage of all possible
vulnerabilities eliminated. A passing build or scan does not establish complete security.
Implementation, automated verification, and installed-release acceptance are distinct.

The earlier public-production readiness estimate remains approximately 60% until
the remaining release blockers and installed-app checks are verified. This is an
engineering estimate, not a certification or probability of safety.

## Phase 1: Secure Local Remediation

**Checklist progress: 7 of 7 agreed acceptance groups complete, 100%.**
Installed-app acceptance below is user-reported, not an independent penetration test.

| Acceptance group | Status | Evidence |
| --- | --- | --- |
| Authenticate local API and stop trusting caller-supplied client IDs | Complete in source and automated tests | Per-launch 256-bit credential; fixed-time comparison; trusted local installation principal |
| Authenticate SignalR connections as well as HTTP requests | Complete in source and automated tests | Real HTTP negotiation and WebSocket handshake tests; missing credentials rejected |
| Close the alternate direct write-tool execution route | Complete in source and automated tests | Direct endpoint permits Tier 0 only; both write tiers rejected |
| Require explicit consent and recheck current tool safety | Complete in source and automated tests | Missing consent rejected; current arguments previewed again before execution |
| Prevent overlapping fixes | Complete in source and automated tests | Shared execution gate covers remediation and selected-app closing; controller returns conflict |
| Bind execution to a single-use, expiring, server-issued preview approval | Complete in source and automated tests | Five-minute, one-use approvals bind operation, diagnostic session, tool, and arguments; selected-app approvals bind the exact normalized selection |
| Rebuild, install, and exercise the agreed desktop workflow | Complete for the agreed primary-device checks, user-reported | User confirmed saved checks, preview cancellation, consent reset after changes, and reconnect/history after restart; prior screenshots showed the desktop loading and normal-browser denial |

**Six-phase roadmap progress: approximately 33%.** This is an equal-phase checklist
convention: Phases 1 and 2 are complete for their agreed scope, and Phases 3-6 have no completed acceptance groups.
It is not a time estimate or a production-readiness score. Revise the checklist
explicitly if scope changes instead of inventing percentage gains per code edit.

## What Changed

- Desktop creates a fresh random credential for each launch and passes it only to
  its API child process environment. It is not written to a configuration file.
- WebView2 injects that credential only into the trusted local page before the app
  starts. Navigation away from the local origin and new browser windows are blocked.
- Frontend HTTP wrappers and all three SignalR connection sites attach the credential.
  Desktop API requests use their own page origin rather than a configurable destination.
- API and hub requests require a loopback peer and a valid credential. Supplied origins
  must exactly match the approved local origins; cross-site fetches are rejected.
- WebSocket/SSE query credentials are supported only for GET requests under `/hubs`.
  They are not accepted for ordinary API requests or negotiation POST requests.
- Protected responses use `Cache-Control: no-store`. Hosting request diagnostics
  are filtered below Warning to avoid routine full-URL logs. Do not enable logging
  that records hub query credentials or authorization headers.
- Local history is scoped by the authenticated installation identity `local`, not
  by a caller-editable `X-Client-ID`. This is installation authentication, not cloud
  account isolation or a replacement for Supabase authorization policies.
- The obsolete client-ID middleware was removed. Client-ID headers may still be sent
  for compatibility but no longer authorize access or partition rate-limit identities.
- Direct tool execution rejects Tier 1 and Tier 2 tools. The reviewed execution path
  requires consent, reruns the tool's safety preview, and rejects unsafe conditions.
- A process-wide execution gate prevents concurrent remediation and app-closing work.
- Five-minute, in-memory approvals bind the exact operation, diagnostic session,
  tool, and argument JSON. Consumption is atomic and happens before execution;
  changed, expired, unknown, or already-used approvals are rejected. A failed attempt
  requires a fresh review instead of reusing the approval. Restarting discards them.
- Selected-app closing has a non-destructive preview endpoint that validates the
  selected apps and binds their exact selection before confirmation. The UI clears
  consent when selection changes and waits for the new preview before enabling execution.

The desktop credential does not protect against arbitrary malware already executing
with sufficient access to the same Windows user/process memory. Hiding DevTools is
not the access-control mechanism. No `.env` or `.env.*` contents were inspected or edited.

## Automated Verification

- .NET tests: **116 passed, 0 failed** (69 existing plus 47 added cases).
- Frontend tests: **9 passed, 0 failed** (7 existing plus 2 added cases).
- Desktop Release build: passed, with two existing nullable warnings in
  `GeminiReActLlmClient.cs`.
- Frontend production bundle: passed using an isolated environment directory and
  output directory, without loading repository environment files. Existing bundle
  size and SignalR dependency annotation warnings remain.
- Frontend lint: 0 errors, 14 existing warnings.
- TypeScript: still fails with the same four existing history/detail contract errors;
  no new type errors were reported. These are not considered fixed by this phase.
- Real transport test uses a temporary loopback server without production hardware,
  database, AI, or remediation services. No real processes were closed, files cleaned,
  services restarted, or operating-system repairs executed.

## Rebuild and Installed Acceptance

Run from the repository root in Command Prompt:

```bat
dotnet test backend-dotnet\RigMD.Tests\RigMD.Tests.csproj --no-restore -m:1 /nr:false /p:UseSharedCompilation=false
powershell -NoProfile -ExecutionPolicy Bypass -File ".\installer\build-installer.ps1"
```

The installer build is the normal local release process and may use your existing
local build configuration. The agent did not run this packaging step or inspect
its environment files. It requires Inno Setup and may restore packages.

1. Close the existing RigMD app, then install the newly built
   `installer\output\RigMD-Setup.exe`. Keep your previous release available for recovery.
2. Open RigMD Desktop. Check hardware readings, live graphs, a read-only diagnostic,
   saved history, and a remediation preview. The UI should not ask the user for the
   temporary local credential.
3. With the desktop running, open `http://localhost:5273/api/autonomy/settings`
   in ordinary Chrome. It should return HTTP 401, not local settings. The desktop
   should continue working. Do not copy its credential into the browser.
4. Cancel a remediation preview and confirm no action occurs. Do not run destructive
   remediation solely to test authentication; use a disposable Windows test machine
   for actual system-changing acceptance tests.
5. Change the tool or selected apps and verify consent is cleared. Verify an approval
   older than five minutes is rejected and a new review is required. Confirm this in
   a disposable test environment without running an actual repair on your main PC.
6. Close and reopen the desktop. Verify it still connects and retains local history.
7. Check installer upgrade behavior and repeat on another supported Windows desktop.

For development, launch through RigMD.Desktop so it creates the API credential.
An API launched alone without `RIGMD_LOCAL_API_TOKEN` deliberately denies protected
requests. Ordinary Vite/browser development is not automatically authenticated;
do not introduce a production authentication bypass to restore that convenience.

These source changes are uncommitted and have not been pushed, deployed, or included
in a published installer by the agent.

## Remaining Roadmap

### Dashboard and Check Results Follow-Up

**Progress: 4 of 4 acceptance groups complete, 100%, including user-reported installed acceptance.** This follow-up is separate
from the six-phase security roadmap and does not change its denominator or score.

| Acceptance group | Status |
| --- | --- |
| Restore dashboard used/total GB capacity and preserve unknown readings | Source complete; formatting tests passed |
| Open the saved result screen automatically after full, component, and problem checks | Source complete |
| Verify browser behavior and desktop/mobile layout | Passed in an isolated browser at 1440x1000 and 390x844 |
| Rebuild/reinstall and verify these changes against real device readings | Complete, user-reported after the storage/results-screen changes |

The storage card previously displayed only model names, percentage labels, and bars
when physical drives were present. It now displays combined used/total capacity and
per-drive capacity. Missing, invalid, or partially known usage is not replaced by
a false zero. Existing unit coverage was extended for single/multiple drives,
missing values, known zero, and volume-only fallback data.

The scan completion callback now opens the existing saved diagnosis-detail screen
for all three scan modes. It no longer appends the fresh report below the setup
form or waits for an unrelated hardware snapshot request before showing a saved
result. Failed scans and missing saved-result IDs stay on the form with an error.

Verification: 11 frontend unit tests passed; the environment-isolated production
bundle passed; changed-file lint had no errors (three existing warnings). The same
four pre-existing TypeScript history/detail contract errors remain. Browser tests
used fake local credentials and mocked responses only, exercised all three modes
on both viewport sizes, checked scan failure/missing-result handling, and reported
zero page errors. No real diagnostics or Windows remediation ran during these tests.

The user's screenshots showed the installed dashboard loading and the ordinary
browser receiving the expected local API denial message. The user subsequently
confirmed the UI request was done, then confirmed all four requested installed
checks: saved history, preview cancellation, consent reset on selection changes,
and reconnect/history after restart. Phase 1 is now complete for this agreed scope.
Approval expiry/replay remains covered by the automated tests; destructive repairs,
clean-machine installation, a second hardware configuration, and live-cloud checks
are not newly certified by this confirmation and remain release-validation work.

### Phase 2: Protect the Gemini Proxy

**Progress: 6 of 6 agreed acceptance groups complete, 100%.**
The user approved existing-account sign-in for cloud AI only. Offline diagnostics
remain usable without signing in.

| Acceptance group | Status | Evidence |
| --- | --- | --- |
| Verify cloud identity and email confirmation | Complete in source and isolated tests | Supabase Auth getUser on each proxy request; reject malformed/unverified/anonymous identities; no trust in client IDs |
| Integrate desktop cloud credentials without shipping server keys | Complete in source and isolated tests | In-memory access/refresh session, verified email sign-in, token refresh and sign-out, trusted HTTPS routing; Settings UI verified at desktop/mobile widths |
| Enforce shared durable rate/usage limits | Complete for agreed staged checks, user-reported | SQL permissions and sequential minute/day/global assertions passed; pinned staging RPC tool returned PASS for 13 overlapping requests (12 allowed, 1 denied), exhausted-account follow-up denied, and another dummy account allowed; quota-to-HTTP-429/no-Google mapping covered by isolated proxy tests |
| Bound request content, models, and output | Complete in source and isolated tests | 128 KiB JSON limit, constrained text/function protocol, fixed model allowlist, one candidate, output token/response-size caps |
| Bound failures and preserve offline fallback | Complete in source and isolated tests | Auth/quota/Google timeouts, generic errors, no retry cascade on auth/quota failure; both desktop clients reject legacy direct-key bypass |
| Accept the deployed proxy and newly installed desktop together | Complete for agreed primary-device flow, user-reported | Unsigned POST 401, public config 200, cloud 200/STOP and visible explanation; after local-limiter fix, user confirmed signed-out scan completed without a proxy request, restart remained signed out, and history was retained |

Current verification: 179 backend tests and 26 frontend tests pass. Desktop Release
and environment-isolated frontend production builds pass. Lint has no errors and
14 pre-existing warnings. The same four pre-existing TypeScript contract errors
remain; this phase does not mark them fixed. Browser tests cover failed login,
password clearing, successful login, and sign-out at 1440x1000 and 390x844 with
mocked credentials/responses and zero page errors. The extra backend cloud tests
never use a real provider key or account password.

The desktop browser's password saving/autofill is disabled. Cloud tokens stay in
API-process memory and are not returned to JavaScript or written to configuration.
Sign-in is required again after closing the app. Local history remains scoped to
the Windows-user installation, not separately to each cloud account.

The proxy's Google key and Supabase server-only quota credential stay on Vercel.
The ordinary public URL/publishable key are the only values returned by the public
cloud configuration endpoint. The old local API credential is never reused as a
cloud login token. Existing desktop builds without this sign-in integration will
fall back locally once the protected proxy is deployed.

Deployment, SQL acceptance, server-variable setup, rebuilt-installer commands, and
safe smoke tests are documented in `Docs/CLOUD_AI_DEPLOYMENT.md`. Apply the quota
migration and Vercel configuration before expecting cloud sign-in/AI to work.
No `.env` files were inspected or changed; no commit, push, or deployment was made.

#### Live Timeout Follow-Up

The user confirmed the mode was manually changed to local-only; automatic mode
reversion was not established. A later scan with cloud mode enabled produced
POST /api/gemini 504 and showed outgoing Supabase and Google requests. This proves
the desktop reached the proxy, not that a usable Gemini response was returned.
The old Google timeout was eight seconds, with a 15-second Vercel duration and
15-second desktop candidate timeout. Source now gives Google 20 seconds, Vercel
30 seconds, and both desktop callers 35 seconds; Auth/quota remain bounded at two
seconds each. Request caps, output limits, and authentication are unchanged.
Safe warning logs identify interrupted identity/quota/provider stages without
raw exceptions or secrets. Automated tests cover stage failures and 504 fallback
without retrying other models. The timeout follow-up is not yet deployed or
packaged by the agent and has not passed a live successful-cloud acceptance check.
Phase 2 remains 4/6 (67%); the equal-phase roadmap remains approximately 28%.

#### Completed-Response Parsing Follow-Up

Later user logs show the updated 30-second deployment returning 200 for
gemini-3.5-flash, then 502 for gemini-3.8-flash. This confirms one authenticated
provider round trip succeeded at the proxy, but does not prove the desktop used
that response or that both entries belong to the same diagnostic explanation.
The code had rejected answers based on final punctuation and read only the first
text part. The explainer now accepts nonempty combined non-thought text only when
the provider reports STOP, without requiring a period or stripping Markdown.
Safety-blocked answers stop the model cascade; missing/incomplete/blocked answers
are not accepted as complete. Sixteen new isolated cases cover complete Markdown,
multiple parts, thought exclusion, malformed data, and rejected finish reasons.
Safe proxy logs now record only allowed model, numeric provider status, and a
bounded completion category. Empty candidate lists return 502. The underlying
live 502 status/cause remains unconfirmed; the url.parse deprecation warning is
not established as its cause. These new parsing/logging changes still need
deployment/repackaging and live acceptance. Phase 2 remains 67%.

#### Local Rate-Limit Isolation Follow-Up

The next user screenshots show a 200/STOP cloud response and its visible saved
explanation, followed by successful sign-out while auto/Gemini mode stays selected.
The signed-out scan failed with the local API's rate-limit message, not evidence
of the Supabase cloud quota. Installed offline-scan/restart acceptance is pending.

The local limiter previously shared eight requests per 30 seconds across all
autonomy endpoints (including settings and tool-list reads), cloud sign-in/out,
and scans. A new isolated localhost regression test reproduced 429 during ordinary
settings/tool-list navigation under that implementation. The rate configuration
now lives in LocalApiRateLimiting, used by Program and the transport tests.
Metadata reads use the existing standard 45/15-second pool. Scan/recheck work,
autonomy work, settings writes, sign-in, and sign-out each retain bounded 8/30-second
pools without consuming each other's allowance. Expensive process reads retain
the autonomy limit, and remediation aliases cannot bypass it. Partitions continue
to use the authenticated local identity, never the caller's X-Client-ID header.
429 responses report the limiter's Retry-After estimate and a local_rate_limit
code rather than a fixed misleading ten-second delay.

Eight new HTTP tests verify navigation/sign-out/scan isolation, read and mutation
caps, spoofed client IDs, authentication ordering, route/query aliases, and retry
metadata without any real Windows providers, repairs, account credentials, or
cloud calls. All 165 backend and 26 frontend tests pass. This source change needs
only a desktop rebuild/reinstall, not Vercel changes. No installer was built or
installed by the agent; live offline/restart/quota checks remain pending. Phase 2
remains 67%, and the equal-phase roadmap remains approximately 28%.

#### Installed Acceptance and Staging Quota Checks

The user subsequently confirmed a signed-out scan succeeded with auto/Gemini mode
selected, without a new /api/gemini call, and restarting retained sign-out/history.
This completes the primary-device integration group and advances Phase 2 to 5/6
(83%), not a production-readiness score. The remaining group is shared-quota
behavior. The user created a separate RigMD-Staging project, installed the migration,
confirmed function permissions, and reported success for the rollback-based
sequential staging assertion script. The cropped SQL result does not independently
prove the project identity; the user provided staging reference
qwytztbzvjgmqybdmrch from its dashboard link.

A standalone tools/RigMD.QuotaChecks utility is now pinned to that exact staging
RPC endpoint. The operator enters a dedicated staging sb_secret_ key privately at
a hidden console prompt; no keys are accepted in arguments or configuration files,
printed, or persisted. No production URL override, redirects, Gemini calls, real
accounts, schema changes, or counter resets are allowed. It starts 13 overlapping
calls for a dummy UUID, expects exactly 12 allowed/1 denied in a single minute,
then verifies the exhausted account remains denied and a different dummy account
is allowed. Server dates, finite timing budgets, response shapes, and retry values
are checked. Inconclusive/network/malformed results cannot count as passes.
Separate RPC transactions leave only dummy counters in staging; they do not roll
back as the earlier SQL script does. The dedicated test key should be revoked
afterward. No live credentials or external quota calls were used by the agent.

Fourteen new mocked tests verify 13 in-flight calls, destination pinning, outcome
checks, redirection/error rejection, timing ambiguity, and non-disclosure. All 179
backend tests pass; existing frontend tests remain 26 passed. Package restore
reported NU1900 because vulnerability metadata could not be retrieved; that is not
a clean vulnerability audit and does not resolve the existing dependency alerts.
The remaining live concurrency run is operator-driven using the tool README.
Phase 2 remains 83%; the equal-phase roadmap is approximately 31%.

#### Phase 2 Acceptance Complete

On 2026-10-16, the user returned the staging tool's final output:

```text
PASS: 13 overlapping requests: 12 allowed, 1 denied.
PASS: exhausted account's follow-up denied; another dummy account allowed.
```

This completes the remaining agreed shared-quota acceptance group. Phase 2 is
6/6 (100%), and the equal-phase roadmap is approximately 33%. This is based on
source/isolated tests plus user-operated staging and installed-app evidence, not
an independent penetration test or proof of all possible concurrency behavior.
The test exercised the staging RPC, not live Gemini-proxy quota exhaustion.
HTTP-429/no-Google behavior is isolated-test evidence; old-window behavior was
checked with seeded fixtures rather than waiting across actual midnight. Those
distinctions remain part of the evidence record, not claims of production-wide
or enterprise-level certification. Existing dependency alerts, type-contract
errors, telemetry/history work, and broader release validation remain outstanding.

Next cleanup: revoke only the dedicated quota-concurrency-test key in RigMD-Staging,
not the live project's key. Dummy staging counters may remain; do not reset live
quota tables. No secret values were received by the agent. Next planned phase:
telemetry provenance and consistent diagnostic/recheck rules. Historical entries
above retain the progress recorded at their respective milestones.

### Six-Phase Plan

1. Phase 1 complete for the agreed local API/remediation safety scope; maintain its
   regression tests. Broader clean-machine/release validation remains in Phase 6.
2. Protect the Gemini proxy: verified authentication, quotas/rate limits, allowed
   models, request/output limits, upstream timeout, and deployed-function tests.
3. Correct telemetry provenance and diagnostic/recheck rules, including unknown
   sensors, consistent thresholds, and GPU-specific device-error attribution.
4. Correct recurrence and history evidence, including healthy-session exclusion,
   failure status, and persisted comparable before/after evidence.
5. Resolve TypeScript contract errors and assess dependency advisories with tests.
6. Build a versioned installer and validate clean-machine install/upgrade, offline
   operation, denied permissions, occupied ports, and WebView2 prerequisites.
