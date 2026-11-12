import { useCallback, useEffect, useState } from 'react';
import { apiGet } from '../lib/api';

interface CheckRow { session_id: string; created_at: string; action_category: string; resolution_status: string }
interface CheckPage { reference_id: string; total_checks: number; offset: number; limit: number; checks: CheckRow[] }

export default function WarningCheckLinks({ referenceId, onOpenSession }: { referenceId: string; onOpenSession: (id: string) => void }) {
  const [page, setPage] = useState<CheckPage | null>(null);
  const [offset, setOffset] = useState(0);
  const [retry, setRetry] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const load = useCallback(async (signal: AbortSignal) => {
    setLoading(true); setError(false);
    try {
      const result = (await apiGet<CheckPage>(`/api/warning-signs/${encodeURIComponent(referenceId)}/checks`, { params: { offset, limit: 20 }, signal, timeout: 10000 })).data;
      if (signal.aborted) return;
      if (!result || result.reference_id !== referenceId || result.offset !== offset || result.limit !== 20 ||
          !Number.isSafeInteger(result.total_checks) || result.total_checks < 0 || !Array.isArray(result.checks) || result.checks.length > 20 ||
          !result.checks.every(row => row && typeof row.session_id === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(row.session_id) &&
            typeof row.created_at === 'string' && Number.isFinite(Date.parse(row.created_at)) && typeof row.resolution_status === 'string' && typeof row.action_category === 'string')) throw new Error('Invalid saved-check page');
      setPage(result);
    } catch { if (!signal.aborted) { setError(true); setPage(null); } }
    finally { if (!signal.aborted) setLoading(false); }
  }, [referenceId, offset]);
  useEffect(() => { const controller = new AbortController(); void load(controller.signal); return () => controller.abort(); }, [load, retry]);
  return <section aria-label="Saved checks behind this alert" className="border-t border-[var(--rigmd-border)] px-5 py-4">
    <h4 className="text-sm font-semibold text-white">Saved checks behind this alert</h4>
    <p className="mt-1 text-xs text-slate-400">Open the original result and its follow-up evidence. Historical observations can include resolved checks.</p>
    {loading ? <p role="status" className="mt-3 text-sm text-slate-300">Loading matching checks…</p>
      : error ? <div role="alert" className="mt-3 text-sm text-amber-300">Matching checks could not be loaded. <button type="button" className="underline" onClick={() => setRetry(value => value + 1)}>Retry matching checks</button></div>
        : page && <>
          <p className="mt-3 text-xs text-slate-400">{page.total_checks} matching saved checks. {page.checks.length ? `Showing ${offset + 1}–${offset + page.checks.length}.` : 'No matching checks remain on this page.'}</p>
          <ul className="mt-2 space-y-2">{page.checks.map(row => <li key={row.session_id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-[var(--rigmd-border)] p-3">
            <div className="min-w-0"><p className="text-sm text-slate-200">{new Date(row.created_at).toLocaleString()}</p><p className="text-xs text-slate-400">{row.resolution_status ? `Saved follow-up status: ${row.resolution_status}` : 'No follow-up status recorded'}</p></div>
            <button type="button" className="rounded-lg border border-cyan-400/30 px-3 py-2 text-xs font-semibold text-cyan-300" onClick={() => onOpenSession(row.session_id)}>Open saved check</button>
          </li>)}</ul>
          <div className="mt-3 flex gap-3">
            {offset > 0 && <button type="button" className="text-sm text-cyan-300 underline" onClick={() => setOffset(value => Math.max(0, value - 20))}>Previous checks</button>}
            {offset + page.checks.length < page.total_checks && <button type="button" className="text-sm text-cyan-300 underline" onClick={() => setOffset(value => value + 20)}>Next checks</button>}
          </div>
        </>}
  </section>;
}
