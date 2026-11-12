import { useCallback, useEffect, useState } from 'react';
import { Activity, AlertTriangle, CheckCircle2, Clock3, FileText, Files, HelpCircle, Monitor, RefreshCw } from 'lucide-react';
import { apiFetch } from '../lib/api';
import { getPerformanceDeliveryFailures } from '../lib/performanceRecording';
import { measurementContext, operationLabels, summarizePerformance, targetFor, type PerformanceRecord } from '../lib/performanceMetrics';
import { appSpeedSummary, outcomeLabel, secondsLabel } from '../lib/performancePresentation';

interface RecordsResponse { records: PerformanceRecord[]; retentionLimit: number;
  health: { queued: number; saved: number; dropped: number; storageFailures: number }; }
const time = (ms: number | null) => ms === null ? '—' : `${(ms / 1000).toFixed(2)} s`;
const taskIcons = { 'startup.desktop': Monitor, 'history.ui': Files, 'report.ui': FileText };

export default function PerformanceRecords({ standalone = false }: { standalone?: boolean }) {
  const [data, setData] = useState<RecordsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const response = await apiFetch('/api/performance');
      if (!response.ok) throw new Error();
      const result: RecordsResponse = await response.json();
      if (!Array.isArray(result.records)) throw new Error();
      setData(result);
    } catch { setError('We couldn’t load the app’s timing results. Please try again.'); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { if (standalone) void load(); }, [standalone, load]);
  // The API returns newest records first; keep build/device timings separate.
  const context = data?.records[0] ? measurementContext(data.records[0]) : '';
  const records = data?.records.filter(row => measurementContext(row) === context) ?? [];
  const groups = summarizePerformance(records);
  const tasks = appSpeedSummary(data?.records ?? [], context);
  const deliveryFailures = getPerformanceDeliveryFailures();
  const missingResults = data && (data.health.dropped > 0 || data.health.storageFailures > 0 || deliveryFailures > 0);
  const Container = standalone ? 'section' : 'details';
  return <Container aria-label="App speed results" className="relative min-w-0 rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5">
    {!standalone && <summary className="cursor-pointer rounded font-bold text-white focus-visible:outline-2 focus-visible:outline-cyan-300">
      <Activity size={19} aria-hidden="true" className="mr-2 inline text-cyan-300" />App speed
    </summary>}
    <div className="mt-4 flex flex-wrap items-start justify-between gap-4">
      <div className="max-w-2xl"><h3 className="text-lg font-bold text-white">How quickly does RigMD respond?</h3>
        <p className="mt-2 text-sm leading-relaxed text-slate-300">See how long common tasks take on this PC. These results describe the app’s speed, not your computer’s health or whether a repair worked.</p>
      </div>
      <button type="button" onClick={() => void load()} disabled={loading} className="inline-flex min-h-11 shrink-0 items-center gap-2 rounded-lg border border-cyan-400/40 px-4 text-sm font-semibold text-cyan-200 disabled:opacity-50">
        <RefreshCw size={16} aria-hidden="true" className={loading ? 'animate-spin motion-reduce:animate-none' : ''} />{loading ? 'Loading results…' : data ? 'Refresh results' : 'Show results'}
      </button>
    </div>
    {error && <p role="alert" className="mt-4 rounded-lg border border-amber-400/30 bg-amber-400/5 p-3 text-sm text-amber-200">{error}</p>}
    {data && !error && <>
      <p role="status" className="mt-4 text-sm text-slate-400">{data.records.length === 0 ? 'No results saved yet. Use RigMD, then refresh here.' : `${data.records.length} timing results saved on this PC.`}</p>
      {missingResults && <p role="alert" className="mt-3 rounded-lg border border-amber-400/30 bg-amber-400/5 p-3 text-sm text-amber-200">Some timing results could not be saved. The summary may be incomplete. Details are available below.</p>}
      {records[0]?.buildConfiguration === 'Debug' && <p className="mt-4 text-xs leading-relaxed text-slate-400">You’re testing a development build. Collect separate results from the release build for your final performance report.</p>}
      <div className="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-3">
        {tasks.map(task => {
          const Icon = taskIcons[task.operation];
          const StatusIcon = task.status === 'within' ? CheckCircle2 : task.status === 'over' ? AlertTriangle : HelpCircle;
          const status = task.status === 'within' ? 'Within target' : task.status === 'over' ? 'Slower than target' : 'Not measured yet';
          const color = task.status === 'within' ? 'text-emerald-300' : task.status === 'over' ? 'text-amber-200' : 'text-slate-300';
          return <section key={task.operation} aria-label={task.title} className="flex min-w-0 flex-col rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5">
            <div className="flex items-center gap-3"><Icon size={20} aria-hidden="true" className="shrink-0 text-cyan-300" /><h4 className="font-bold text-white">{task.title}</h4></div>
            <p className="mt-2 min-h-10 text-sm leading-relaxed text-slate-400">{task.description}</p>
            <p className="mt-5 text-xs text-slate-400">{task.latest ? 'Latest completed timing' : 'Completed timing needed'}</p>
            <p className="mt-1 text-2xl font-bold tabular-nums text-white">{task.latest ? secondsLabel(task.latest.durationMs) : 'No timing yet'}</p>
            <p className="mt-2 text-sm text-slate-300">Target: {task.targetMs / 1000} seconds or less</p>
            <p className={`mt-4 flex items-center gap-2 text-sm font-semibold ${color}`}><StatusIcon size={17} aria-hidden="true" />{status}</p>
            {task.latest ? <>
              <p className="mt-2 text-xs leading-relaxed text-slate-400">{task.withinTarget} of {task.measured} completed timings met the target.{task.overTarget > 0 ? ` ${task.overTarget} took longer.` : ''}</p>
              <p className="mt-2 text-xs text-slate-400">Recorded {new Date(task.latest.createdAt).toLocaleString()}</p>
            </> : <p className="mt-2 text-sm leading-relaxed text-slate-300">{task.empty}</p>}
            {(task.incomplete > 0 || task.failed > 0 || task.canceled > 0) && <p className="mt-3 border-t border-[var(--rigmd-border)] pt-3 text-xs leading-relaxed text-amber-200">Other attempts: {task.incomplete} not fully measured · {task.failed} failed · {task.canceled} canceled. These do not count as meeting the target.</p>}
            {task.latestAttempt && task.latestAttempt.outcome !== 'completed' && <p className="mt-2 text-xs text-amber-200">Most recent attempt: {outcomeLabel(task.latestAttempt.outcome).toLowerCase()}.</p>}
          </section>;
        })}
      </div>
      <p className="mt-4 flex items-start gap-2 text-xs leading-relaxed text-slate-400"><Clock3 size={16} aria-hidden="true" className="mt-0.5 shrink-0" />Each timing is a saved observation. Results may vary with other apps running. A few fast runs alone do not establish overall readiness.</p>
      <details className="relative mt-5 border-t border-[var(--rigmd-border)] pt-4">
        <summary className="cursor-pointer rounded text-sm font-semibold text-cyan-200">Technical details for testing</summary>
        <p className="mt-3 text-xs leading-relaxed text-slate-400">Up to {data.retentionLimit} timing results are kept locally on this PC. Times include logging overhead where it affects the user’s wait. No survey answers are collected here.</p>
        {records[0] && <dl className="mt-4 grid gap-x-4 gap-y-2 text-xs sm:grid-cols-[auto_minmax(0,1fr)]">
          <dt className="text-slate-400">App version and build</dt><dd className="break-all">{records[0].appVersion} · {records[0].buildConfiguration}</dd>
          <dt className="text-slate-400">Test computer identifier</dt><dd className="break-all">{records[0].deviceKey}</dd>
          <dt className="text-slate-400">System</dt><dd className="break-words">{records[0].osVersion} · {records[0].architecture} · {records[0].logicalProcessors} logical processors</dd>
        </dl>}
        {missingResults && <p className="mt-3 text-xs text-amber-200">Queue drops: {data.health.dropped}; storage failures: {data.health.storageFailures}; browser delivery failures this page lifetime: {deliveryFailures}.</p>}
        <p className="mt-4 text-xs leading-relaxed text-slate-400">Typical time is the median (the middle observed time). The 95th percentile is a duration at or below which 95% of observed timings fall. Both include completed and incomplete operations; failed and canceled attempts remain in the counts. Target counts use only completed, eligible timings. Development and release builds, PCs, and operation boundaries are kept separate. Startup phases can overlap, so their durations should not be added together.</p>
        {groups.length > 0 && <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[760px] text-left text-sm"><caption className="sr-only">Detailed timing results for the selected setup</caption>
            <thead className="text-xs text-slate-400"><tr>{['Task', 'Attempts', 'Typical time (median)', '95th percentile', 'Longest time', 'What happened', 'Timing target results'].map(label => <th key={label} scope="col" className="p-3">{label}</th>)}</tr></thead>
            <tbody>{groups.map(group => <tr key={group.key} className="border-t border-slate-700">
              <th scope="row" className="p-3 font-semibold text-white">{operationLabels[group.operation] ?? group.operation}</th>
              <td className="p-3">{group.runs}</td><td className="p-3">{time(group.medianMs)}</td><td className="p-3">{time(group.p95Ms)}</td><td className="p-3">{time(group.maxMs)}</td>
              <td className="p-3 text-xs leading-relaxed">{group.completed} finished · {group.incomplete} not fully measured · {group.failed} failed · {group.canceled} canceled</td>
              <td className="p-3 text-xs">{group.eligible ? `${group.withinTarget}/${group.eligible} within target; ${group.overTarget} over target` : group.operation === 'history.ui' ? 'Needs a completed run showing at least 50 checks' : 'No completed timing target applies'}</td>
            </tr>)}</tbody>
          </table>
        </div>}
        {records.length > 0 && <details className="relative mt-4 border-t border-slate-700 pt-4"><summary className="cursor-pointer text-sm font-semibold text-cyan-200">Recent timings ({Math.min(records.length, 20)} shown)</summary>
          <ol className="mt-3 divide-y divide-slate-700">{records.slice(0, 20).map(row => <li key={row.id} className="py-3 text-sm">
            <p className="font-semibold">{operationLabels[row.operation] ?? row.operation} · {time(row.durationMs)} · {outcomeLabel(row.outcome)}</p>
            <p className="mt-1 text-xs text-slate-400">{new Date(row.createdAt).toLocaleString()} · {row.mode} · {row.engine}{row.recordCount !== null ? ` · ${row.recordCount} records/readings` : ''}{targetFor(row) !== null ? ` · target ${time(targetFor(row))}` : ''}</p>
            {row.apiWorkingSetMb !== null && <p className="mt-1 text-xs text-slate-400">API process memory: {row.apiWorkingSetMb.toFixed(1)} MB · API CPU time during request: {row.apiCpuTimeMs?.toFixed(1) ?? 'unavailable'} ms. Includes concurrent API work; excludes Desktop and WebView2.</p>}
          </li>)}</ol>
        </details>}
        <p className="mt-4 text-xs leading-relaxed text-slate-400">SRS targets: startup ≤5 s; at least 50 saved checks displayed ≤3 s; selected report preview ready ≤5 s. Startup scope is recorded per launch: essential_home includes Home essential readings; saved-check summary readiness is timed separately and full hardware inventory loads on demand. Earlier normal launches waited for the full profile. A hidden page is marked incomplete. Report-preview time excludes PDF saving. PDF request time includes the Save dialog; PDF generation time excludes it.</p>
      </details>
    </>}
    {!data && !error && <p className="mt-4 text-xs text-slate-400">Choose Show results to see timings already saved on this PC. This does not start a device check.</p>}
  </Container>;
}
