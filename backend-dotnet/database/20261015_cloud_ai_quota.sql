begin;
create schema if not exists rigmd_private;
revoke all on schema rigmd_private from public, anon, authenticated;
create table if not exists rigmd_private.ai_quota_buckets (
  bucket_key text primary key,
  window_end timestamptz not null,
  request_count integer not null default 0 check (request_count >= 0)
);
alter table rigmd_private.ai_quota_buckets enable row level security;
revoke all on table rigmd_private.ai_quota_buckets from public, anon, authenticated;
create index if not exists ai_quota_expiry on rigmd_private.ai_quota_buckets (window_end);

create or replace function public.consume_rigmd_ai_quota(p_user_id uuid)
returns table (allowed boolean, retry_after_seconds integer)
language plpgsql security definer set search_path = '' set timezone = 'UTC' set lock_timeout = '1s' as $$
declare
  now_at timestamptz := pg_catalog.clock_timestamp();
  minute_start timestamptz := pg_catalog.date_trunc('minute', now_at);
  day_start timestamptz := pg_catalog.date_trunc('day', now_at at time zone 'UTC') at time zone 'UTC';
  keys text[];
  ends timestamptz[];
  limits integer[] := array[12, 120, 1000];
  counter integer;
  retry integer := 0;
  i integer;
begin
  if p_user_id is null then raise exception 'User identity required'; end if;
  perform pg_catalog.pg_advisory_xact_lock(20261015, 1);
  delete from rigmd_private.ai_quota_buckets where window_end < now_at - interval '1 day';
  keys := array[
    'user-minute:' || p_user_id::text || ':' || minute_start::text,
    'user-day:' || p_user_id::text || ':' || day_start::text,
    'global-day:' || day_start::text
  ];
  ends := array[minute_start + interval '1 minute', day_start + interval '1 day', day_start + interval '1 day'];
  for i in 1..3 loop
    select b.request_count into counter from rigmd_private.ai_quota_buckets b where b.bucket_key = keys[i];
    if coalesce(counter, 0) >= limits[i] then
      retry := greatest(retry, ceil(extract(epoch from (ends[i] - now_at)))::integer);
    end if;
  end loop;
  if retry > 0 then return query select false, retry; return; end if;
  for i in 1..3 loop
    insert into rigmd_private.ai_quota_buckets as b (bucket_key, window_end, request_count)
      values (keys[i], ends[i], 1)
      on conflict (bucket_key) do update set request_count = b.request_count + 1;
  end loop;
  return query select true, 0;
end;
$$;
revoke all on function public.consume_rigmd_ai_quota(uuid) from public, anon, authenticated;
grant execute on function public.consume_rigmd_ai_quota(uuid) to service_role;
notify pgrst, 'reload schema';
commit;
