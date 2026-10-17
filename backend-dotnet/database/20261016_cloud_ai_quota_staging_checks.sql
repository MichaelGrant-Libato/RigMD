-- STAGING ONLY. Confirm the dashboard project is RigMD-Staging, NOT RigMD.
-- Run this WHOLE file in one SQL Editor execution, with no text selected.
-- Requires 20261015_cloud_ai_quota.sql in a fresh, unused staging project.
-- Uses dummy UUIDs; never creates accounts, calls Gemini, or changes permissions.
-- Test counter changes are rolled back. No existing counters are cleared/reset.
-- Refusing a nonempty table is an extra guard, NOT proof of project identity.
-- Expected: "Success. No rows returned" with no error; PASS notices may also show.
-- This checks sequential DB behavior, NOT concurrent requests or HTTP 429 wiring.

begin;
set local timezone = 'UTC';
set local datestyle = 'ISO, MDY';
set local lock_timeout = '1s';
set local statement_timeout = '10s';
set local idle_in_transaction_session_timeout = '15s';

do $$
declare
  now_at timestamptz;
  minute_start timestamptz;
  day_start timestamptz;
  user_a uuid := gen_random_uuid();
  user_b uuid := gen_random_uuid();
  user_c uuid := gen_random_uuid();
  user_d uuid := gen_random_uuid();
  user_e uuid := gen_random_uuid();
  user_f uuid := gen_random_uuid();
  minute_a text;
  day_a text;
  global_day text;
  old_minute text;
  old_day text;
  stale_key text := 'staging-stale:' || gen_random_uuid()::text;
  outcome record;
  request_number integer;
  before_rows bigint;
  before_total bigint;
begin
  perform pg_catalog.pg_advisory_xact_lock(20261015, 1);

  if exists (select 1 from rigmd_private.ai_quota_buckets) then
    raise exception 'STOP: expected an empty staging quota table. Do not clear production data. Confirm RigMD-Staging.';
  end if;

  if has_function_privilege('anon', 'public.consume_rigmd_ai_quota(uuid)', 'execute')
     or has_function_privilege('authenticated', 'public.consume_rigmd_ai_quota(uuid)', 'execute')
     or not has_function_privilege('service_role', 'public.consume_rigmd_ai_quota(uuid)', 'execute') then
    raise exception 'FAIL: expected anon=false, authenticated=false, service_role=true.';
  end if;

  if not exists (
    select 1 from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    where n.nspname = 'rigmd_private' and c.relname = 'ai_quota_buckets' and c.relrowsecurity
  ) then
    raise exception 'FAIL: RLS is not enabled on the private quota table.';
  end if;
  raise notice 'PASS: function permissions and table RLS';

  now_at := clock_timestamp();
  minute_start := date_trunc('minute', now_at);
  day_start := date_trunc('day', now_at);
  if extract(second from now_at) >= 55 then
    raise exception 'RETRY: too close to a minute boundary. Wait 10 seconds, then run the whole file again.';
  end if;
  minute_a := 'user-minute:' || user_a::text || ':' || minute_start::text;
  day_a := 'user-day:' || user_a::text || ':' || day_start::text;
  global_day := 'global-day:' || day_start::text;

  -- A loop forces 13 actual calls; an uncorrelated SQL cross join would not.
  for request_number in 1..13 loop
    select * into strict outcome from public.consume_rigmd_ai_quota(user_a);
    if outcome.allowed is distinct from (request_number <= 12) then
      raise exception 'FAIL: unexpected per-minute result on request %.', request_number;
    end if;
    if request_number <= 12 and outcome.retry_after_seconds is distinct from 0 then
      raise exception 'FAIL: an allowed request returned a retry delay.';
    end if;
    if request_number = 13 and
       (outcome.retry_after_seconds is null or outcome.retry_after_seconds not between 1 and 60) then
      raise exception 'FAIL: minute-limit denial needs a bounded retry delay.';
    end if;
  end loop;
  if (select request_count from rigmd_private.ai_quota_buckets where bucket_key = minute_a) is distinct from 12
     or (select request_count from rigmd_private.ai_quota_buckets where bucket_key = day_a) is distinct from 12
     or (select request_count from rigmd_private.ai_quota_buckets where bucket_key = global_day) is distinct from 12 then
    raise exception 'FAIL: denied minute request changed the counters.';
  end if;
  raise notice 'PASS: first 12 requests allowed, 13th denied, denial did not consume quota';

  select * into strict outcome from public.consume_rigmd_ai_quota(user_b);
  if outcome.allowed is distinct from true or outcome.retry_after_seconds is distinct from 0
     or (select request_count from rigmd_private.ai_quota_buckets where bucket_key = global_day) is distinct from 13 then
    raise exception 'FAIL: another account should have its own allowance and share the global counter.';
  end if;
  raise notice 'PASS: separate account allowance and shared global counter';

  -- Seed only a fresh dummy account's current-day counter to avoid 120 AI calls.
  insert into rigmd_private.ai_quota_buckets (bucket_key, window_end, request_count)
    values ('user-day:' || user_c::text || ':' || day_start::text, day_start + interval '1 day', 120);
  select count(*), sum(request_count) into before_rows, before_total from rigmd_private.ai_quota_buckets;
  select * into strict outcome from public.consume_rigmd_ai_quota(user_c);
  if outcome.allowed is distinct from false or outcome.retry_after_seconds is null
     or outcome.retry_after_seconds not between 1 and 86400
     or (select count(*) from rigmd_private.ai_quota_buckets) is distinct from before_rows
     or (select sum(request_count) from rigmd_private.ai_quota_buckets) is distinct from before_total then
    raise exception 'FAIL: daily account cap should deny without creating/incrementing counters.';
  end if;
  raise notice 'PASS: 120-request account/day cap and non-consuming denial';

  -- Old-window fixtures verify key isolation, not a real wait across midnight.
  old_minute := 'user-minute:' || user_d::text || ':' || (minute_start - interval '1 minute')::text;
  old_day := 'user-day:' || user_d::text || ':' || (day_start - interval '1 day')::text;
  insert into rigmd_private.ai_quota_buckets (bucket_key, window_end, request_count) values
    (old_minute, minute_start, 12),
    (old_day, day_start, 120),
    (stale_key, now_at - interval '2 days', 1);
  select * into strict outcome from public.consume_rigmd_ai_quota(user_d);
  if outcome.allowed is distinct from true or outcome.retry_after_seconds is distinct from 0
     or (select request_count from rigmd_private.ai_quota_buckets
         where bucket_key = 'user-minute:' || user_d::text || ':' || minute_start::text) is distinct from 1
     or (select request_count from rigmd_private.ai_quota_buckets
         where bucket_key = 'user-day:' || user_d::text || ':' || day_start::text) is distinct from 1
     or exists (select 1 from rigmd_private.ai_quota_buckets where bucket_key = stale_key) then
    raise exception 'FAIL: old-window isolation or expired-bucket cleanup failed.';
  end if;
  raise notice 'PASS: prior minute/day counts do not carry forward; expired fixture removed';

  -- Only the test-created global row is adjusted; the whole transaction rolls back.
  update rigmd_private.ai_quota_buckets set request_count = 999 where bucket_key = global_day;
  select * into strict outcome from public.consume_rigmd_ai_quota(user_e);
  if outcome.allowed is distinct from true or outcome.retry_after_seconds is distinct from 0
     or (select request_count from rigmd_private.ai_quota_buckets where bucket_key = global_day) is distinct from 1000 then
    raise exception 'FAIL: the 1000th global request should be allowed.';
  end if;
  select count(*), sum(request_count) into before_rows, before_total from rigmd_private.ai_quota_buckets;
  select * into strict outcome from public.consume_rigmd_ai_quota(user_f);
  if outcome.allowed is distinct from false or outcome.retry_after_seconds is null
     or outcome.retry_after_seconds not between 1 and 86400
     or (select count(*) from rigmd_private.ai_quota_buckets) is distinct from before_rows
     or (select sum(request_count) from rigmd_private.ai_quota_buckets) is distinct from before_total then
    raise exception 'FAIL: global cap should deny another account without consuming quota.';
  end if;
  raise notice 'PASS: shared 1000-request daily cap blocks another account without incrementing counters';

  if date_trunc('minute', clock_timestamp()) is distinct from minute_start then
    raise exception 'RETRY: the minute changed during testing. Run the whole file again.';
  end if;
  raise notice 'PASS: all sequential quota checks; test counters will now roll back';
end;
$$;

rollback;
