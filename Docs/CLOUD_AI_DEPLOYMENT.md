# Phase 2: Cloud AI Sign-In and Proxy Protection

## Current Status

Phase 2's six agreed acceptance groups are complete (100%) as of 2026-10-16,
based on isolated tests and user-operated deployment, installed-app, and staging
checks. The user installed the SQL migration, confirmed permissions, deployed
the proxy and follow-ups, and verified unsigned POST 401, public config 200,
Gemini 200/STOP with a visible explanation, signed-out local scans without proxy
requests, and sign-out/history retention after restart. Early 504/502 incidents
and their source follow-ups are recorded in SECURITY_HARDENING_PROGRESS.md.

The rollback-based sequential staging assertions passed. The pinned staging RPC
tool then reported 13 overlapping requests: 12 allowed and 1 denied, with an
exhausted-account follow-up denied and another dummy account allowed. This is not
a live HTTP-429 test of the Gemini proxy; that mapping/no-Google behavior is
covered by isolated tests. Old-window fixtures are not a literal midnight wait.
Revoke the dedicated staging test key after testing, not any production key.
The agent has not used real passwords/keys, executed live SQL/AI requests, or
performed a deployment/install. Completion is scoped checklist evidence, not
production-readiness certification; broader release and security work remains.

Offline diagnostics remain available without sign-in. Cloud AI requires a verified
RigMD email account. The desktop's local API credential is not a cloud account token.
Cloud sign-in authorizes AI access only: existing local history remains scoped to
the Windows-user installation, not isolated separately for each cloud account.
Legacy direct Gemini keys are no longer used by the two desktop Gemini clients;
Google credentials stay on Vercel. Old desktop builds without cloud sign-in will
fall back locally after the protected proxy is deployed.

## 1. Apply the Quota Migration

In your existing Supabase project's SQL Editor, review and run:

`backend-dotnet/database/20261015_cloud_ai_quota.sql`

This adds a dedicated private quota table and a service-role-only function. It
does not change policies on your existing profiles, sessions, recommendations,
or agent tables. Never grant this function to `anon` or `authenticated`, or add
`rigmd_private` to the Data API's exposed schemas.

Default fixed-window caps, shared by all Vercel instances:

| Limit | Requests |
| --- | --- |
| Per verified account, per minute | 12 |
| Per verified account, per UTC day | 120 |
| All accounts combined, per UTC day | 1000 |

One check can make multiple AI requests. Limits count attempts reserved before
calling Google, not only successful responses; there are no failure refunds.
Fixed windows can allow bursts around boundaries. These are request quotas, not
a currency-denominated spending guarantee. Also configure provider billing/quota
controls appropriate to your Google project.

The short transaction takes an advisory lock before checking and incrementing
all three counters. A one-second lock timeout makes contention fail rather than
waiting indefinitely. Expired quota buckets older than a day are pruned. Only
user UUIDs, time windows, and counts are stored, not prompts, passwords, or tokens.

Review permissions without consuming AI quota:

```sql
select
  has_function_privilege('anon', 'public.consume_rigmd_ai_quota(uuid)', 'execute') as anon_can_execute,
  has_function_privilege('authenticated', 'public.consume_rigmd_ai_quota(uuid)', 'execute') as authenticated_can_execute,
  has_function_privilege('service_role', 'public.consume_rigmd_ai_quota(uuid)', 'execute') as service_can_execute;
```

Expected: `false`, `false`, `true`. Validate concurrent calls, minute/day rollover,
and the per-account/global caps in a staging project before public rollout.
No local PostgreSQL runtime was available to the agent. The user subsequently
confirmed migration/permission checks, sequential staging assertions, and the
controlled overlapping-request staging RPC test passed; see Current Status.

## 2. Configure Vercel Server Variables

Set these in the Vercel project that hosts `frontend/api/gemini.js`, for Production:

| Name | Type | Value source |
| --- | --- | --- |
| `SUPABASE_URL` | Config | Your existing public HTTPS Supabase project URL |
| `SUPABASE_PUBLISHABLE_KEY` | Config | The same project's public publishable key |
| `SUPABASE_SECRET_KEY` | Secret | A server-only secret key for the same project |
| `GEMINI_API_KEY` | Secret | Your existing server-managed Gemini key |

A legacy `SUPABASE_SERVICE_ROLE_KEY` is accepted instead of `SUPABASE_SECRET_KEY`.
A legacy `SUPABASE_ANON_KEY` is accepted instead of the publishable key. Existing
public `VITE_SUPABASE_URL` and `VITE_SUPABASE_PUBLISHABLE_KEY` can also supply the
public authentication configuration. Prefer the explicit server names above.

Never prefix either secret with `VITE_`. Do not put a Supabase secret/service-role
key or Google key into the installer, desktop settings, frontend source, screenshots,
or chat. No key values need to be shared with the agent. Use a separate server
secret for this component where supported so its rotation is isolated.

The public `/api/cloud-ai-config` endpoint returns only the project URL and public
key. It deliberately rejects a secret/service-role key in the public-key slot.
Missing/invalid configuration or an unavailable quota function fails closed with
503 and local fallback rather than allowing unmetered requests.

## 3. Review and Deploy the Source

Review the intended source changes, including the earlier local API hardening,
then commit/push through your normal GitHub workflow. Do not automatically stage
environment files, local settings/databases, test screenshots, or installer output.
Generated `wwwroot` changes from your local installer build are separate from the
agent's source edits; review them according to the repository's existing release process.

Deploy the updated function from the latest intended commit, not by redeploying
an obsolete Vercel deployment. Server utility modules live outside `api` so they
are not additional function entrypoints. The Gemini function has a 30-second
platform duration limit; Auth and quota calls each have a two-second timeout,
and the Google call has a 20-second timeout. Desktop cloud requests wait up to
35 seconds per candidate so they do not abort before the server's response.
Provider slowness still falls back locally; these limits do not guarantee that
every cloud request completes. Interrupted requests log only the stage
(`identity`, `quota`, or `provider`) and timeout/upstream-failure category, never
the token, key, prompt, or raw exception. Do not disable timeouts or quota checks
to troubleshoot provider latency.

Provider response logs include only the allowed model, numeric upstream status,
and a bounded finish category (STOP, MAX_TOKENS, SAFETY, RECITATION, or OTHER).
They do not include prompts, response text, error bodies, credentials, or raw
finish strings. A 200 at the proxy alone does not prove the desktop accepted the
answer: diagnostic explanations require nonempty non-thought text and STOP.
Completed multi-part or Markdown answers do not require final punctuation.
If a 502 remains, inspect the safe provider-status entry before changing keys,
models, or timeouts. Do not treat a runtime deprecation warning as a proven cause.

After deployment, this unauthenticated test must return 401 and must not call Google:

```bat
curl.exe -i --request POST --header "Content-Type: application/json" --data "{}" "https://www.rig-md.com/api/gemini"
```

A normal GET to the proxy returns 405, not a sign-in page. Public configuration
should return 200 with only the URL/public key. Do not test by pasting user tokens
into URLs or chat. Configure Vercel firewall limits for unauthenticated flood/DoS
protection as an additional layer; per-user application quotas begin after identity
verification and are not a complete anonymous-traffic firewall.

## 4. Rebuild and Install the Desktop

From the repository root in Command Prompt:

```bat
dotnet test backend-dotnet\RigMD.Tests\RigMD.Tests.csproj --no-restore -m:1 /nr:false /p:UseSharedCompilation=false
powershell -NoProfile -ExecutionPolicy Bypass -File ".\installer\build-installer.ps1"
```

Close RigMD, then install the newly built executable:

```bat
".\installer\output\RigMD-Setup.exe"
```

The agent has not run this packaging step because it uses local build configuration.
The native and frontend pieces must be rebuilt together. A desktop build alone
does not package the new Settings form into the installed app.

### Local Scan Rate-Limit Follow-Up

User evidence now confirms a 200/STOP provider response and a visible cloud
explanation. A later signed-out scan hit the local API's old shared rate limit.
This is not evidence of a cloud quota rejection. Local rate limiting now separates
metadata reads, settings writes, sign-in, sign-out, scans, and autonomy work;
limits remain enabled, and the Supabase/Vercel quota implementation is unchanged.
Local 429 replies include code local_rate_limit, retryAfterSeconds, and an actual
Retry-After estimate. Do not rapidly retry a rejected scan.

This follow-up changes only the local API: rebuild/reinstall using step 4, without
changing keys or redeploying Vercel. Stay signed out with auto/Gemini mode selected,
run one scan, and verify it completes without a new /api/gemini request. Then restart
and verify sign-out/history retention. The user confirmed these installed checks
passed after the local fix, followed by the staged quota assertions and concurrency
test described in Current Status. Repeat these checks for relevant future changes.

## 5. Accept the Deployed Workflow

1. Signed out: run a read-only check and confirm local results still work without
   making a Gemini request.
2. In Settings, sign in with your existing verified RigMD email account. Invalid
   credentials or unverified email must not enable cloud AI. Passwords should clear
   after submission. The app does not expose Google keys or cloud tokens in its UI.
3. With Gemini mode enabled and saved, use a diagnostic explanation or read-only
   investigation preview and confirm the returned engine reports Gemini. Merely
   seeing an account-connected label does not prove an upstream AI call succeeded.
4. In staging, hit the configured request caps and verify 429 plus `Retry-After`,
   with local fallback and no additional Google calls. Verify two accounts have
   separate per-account counters and share the global cap; changing client IDs/IPs
   must not reset an account's allowance.
5. Sign out and run another check: cloud access should stop while local checks work.
   Sign-out discards local credentials even if best-effort remote logout is offline.
   Provider access-token revocation follows Supabase's token/session semantics;
   do not claim instant invalidation of every previously issued JWT.
6. Close/reopen RigMD: cloud sign-in is not remembered. Local history should remain.
   Refresh tokens are kept only in API-process memory. WebView2 password saving and
   general autofill are disabled. Restart requires signing in for cloud AI again.
7. Simulate Auth, quota-store, and Google outages in staging. Verify bounded waits,
   generic errors without key/prompt leakage, and a working offline path. No real
   Windows repair is required to perform these checks.

## Security Boundaries

The proxy verifies the user with Supabase Auth on each cloud request, then consumes
quota using the returned user ID. It does not trust `X-Client-ID`, body user IDs,
unverified local JWT decoding, or a shared key embedded in the installer.

Models are restricted to the existing three configured candidate names. Input is limited
to 128 KiB of actual UTF-8 JSON, at most 40 conversation entries, constrained
text/function-call parts, and function declarations. Remote files, media, search,
and code-execution tools are not forwarded. Output is single-candidate and capped
at 2048 output tokens and 256 KiB of response data. Internal reasoning levels are
server-controlled; these caps do not promise a precise monetary charge.

Prompt-injection risk is not eliminated by signing in or validating JSON. The
existing local read-only investigation, preview, explicit consent, single-use
approval, and registered-tool execution boundaries still apply to AI proposals.

## Official References

- [Supabase getUser: server-confirmed identity](https://supabase.com/docs/reference/javascript/auth-getuser)
- [Supabase API keys: public vs. server-only keys](https://supabase.com/docs/guides/api/api-keys)
- [Supabase database functions and execution permissions](https://supabase.com/docs/guides/database/functions)
- [Vercel Node.js function entrypoints](https://vercel.com/docs/functions/runtimes/node-js)
- [Gemini GenerateContent thinking controls](https://ai.google.dev/gemini-api/docs/generate-content/thinking)
