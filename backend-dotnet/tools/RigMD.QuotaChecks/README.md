# Staging Quota Concurrency Check

This console tool is pinned to Supabase project `qwytztbzvjgmqybdmrch`, identified
by the user as RigMD-Staging. It cannot accept a different project URL. It calls
only `consume_rigmd_ai_quota`, never the Gemini proxy or Google. It does not read
`.env` files, modify schema/permissions, create users, or change production settings.

## Before Running

1. Confirm this reference belongs to RigMD-Staging, not the live project.
2. Apply the quota migration and run the sequential staging checks first.
3. In **RigMD-Staging > Settings > API Keys > Secret keys**, create a dedicated
   key named `quota-concurrency-test`. Keep it private. It must begin `sb_secret_`.
   Do not use the live project's key, a publishable key, database password, or a
   Gemini key. No Vercel environment-variable changes are needed.

## Run in Command Prompt

From the RigMD repository root:

```bat
dotnet run --project backend-dotnet\tools\RigMD.QuotaChecks\RigMD.QuotaChecks.csproj
```

Type `RigMD-Staging` at the confirmation prompt. At the hidden-input prompt, paste
the staging secret key and press Enter. Nothing should be displayed as you paste.
Never append the key to a command, save it in a file/environment variable, or send
it in chat. The tool rejects command-line arguments and redirected input. Press
Escape at the key prompt to cancel without sending any requests.

The key is held in process memory while used; it is not printed or persisted.
Normal .NET build messages may appear before the prompts. No third-party packages
are added by this tool. Do not rebuild or reinstall RigMD Desktop for this test.

## Expected Results

```text
PASS: 13 overlapping requests: 12 allowed, 1 denied.
PASS: exhausted account's follow-up denied; another dummy account allowed.
```

The RPC itself returns HTTP 200 with `allowed=false` for quota denial. This is a
database-concurrency test, not a deployed `/api/gemini` HTTP 429 test. The proxy's
mapping of denied quota to 429/no Google request is covered by isolated tests.

The tool makes at most 18 staging RPC calls, including a warm-up, 13 overlapping
calls for one random dummy UUID, and follow-ups for that UUID and a different one.
It may wait for a new minute before starting. It has finite request/overall
timeouts, rejects redirects/errors/malformed responses, and records no pass if
the batch crosses a minute boundary or takes too long. Do not run multiple copies
at once, and do not repeatedly retry failures. Send only the final PASS/error lines.

Unlike the sequential SQL script, these separate RPC transactions commit a few
dummy quota counters in staging. They contain no real account information or AI
prompts. Existing expiry cleanup prunes old counters when the quota function runs.
Do not clear production counters to prepare this test.

After the test, revoke the dedicated `quota-concurrency-test` secret key in
RigMD-Staging. Do not revoke any production key. This single test is evidence
about the configured quota boundary, not a load test or a full security audit.
