import DiagnosticReadings from './DiagnosticReadings';
import { useState } from 'react';
import { followUpStatusLabel, followUpTimeLabel, type FollowUpObservation } from '../lib/followUpHistory';

export default function FollowUpHistory({ observations = [], status = 'not_recorded' }: {
  observations?: FollowUpObservation[];
  status?: string;
}) {
  const [limit, setLimit] = useState(20);
  return <section aria-labelledby="follow-up-history-title" className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5">
    <h2 id="follow-up-history-title" className="text-lg font-bold text-white">Recheck history</h2>
    <p className="mt-2 text-sm text-slate-400">Each entry is a separate saved observation of this check. A normal reading does not prove a repair or overall hardware health.</p>
    {status === 'legacy_latest_only' && <p className="mt-3 text-sm text-amber-200">An older version retained only its latest follow-up. That saved observation is preserved here; earlier overwritten rechecks cannot be recovered.</p>}
    {status === 'partially_unavailable' && <p role="alert" className="mt-3 text-sm text-amber-200">Some saved recheck entries could not be read. Available entries are shown; the history may be incomplete.</p>}
    {observations.length === 0
      ? <p className="mt-4 text-sm text-slate-300">No saved rechecks are available for this check.</p>
      : <ol className="mt-4 space-y-3">{observations.slice(0, limit).map(item => <li key={item.observation_id}>
        <details className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
          <summary className="cursor-pointer text-sm font-semibold text-cyan-200">
            {followUpTimeLabel(item.checked_at)} · {followUpStatusLabel(item.status)}
          </summary>
          <p className="mt-3 text-sm text-slate-300">{item.summary || 'No summary was saved.'}</p>
          {item.source?.startsWith('legacy_') && <p className="mt-2 text-xs text-slate-400">Preserved from the earlier version's saved follow-up.</p>}
          <div className="mt-3"><DiagnosticReadings readings={item.proof} title="Readings recorded in this recheck" /></div>
        </details>
      </li>)}</ol>}
    {observations.length > limit && <button type="button" className="mt-4 rounded-lg border border-cyan-400/40 px-4 py-2 text-sm text-cyan-200" onClick={() => setLimit(value => value + 20)}>Show more rechecks ({observations.length - limit} remaining)</button>}
  </section>;
}
