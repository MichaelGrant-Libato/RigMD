import { useMemo, useState } from 'react';
import { ArrowUpRight, ChartNoAxesCombined, CircleHelp, Info, RefreshCw, SlidersHorizontal, TriangleAlert } from 'lucide-react';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import {
  buildTrendGroups, buildTrendPoints, buildTrendObservations, getTrendReadingOptions, getTrendScopeOptions,
  getTrendStatusColor, isTrendMetric, TREND_CHECK_TYPES, TREND_METRICS, trendChange, trendCheckCount,
  type TrendCheckType, type TrendPoint, type TrendSelection,
} from '../lib/diagnosticTrends';
import type { SessionSummary } from '../types/rigmd';

const formatDate = (at: number) => new Date(at).toLocaleString([], { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' });

function ChartTooltip({ active, payload }: { active?: boolean; payload?: ReadonlyArray<{ payload?: TrendPoint }> }) {
  const point = payload?.find(item => item.payload?.value !== null)?.payload;
  if (!active || !point) return null;
  return <div className="max-w-[240px] rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-3 text-sm text-white shadow-lg">
    <p className="font-bold">{point.value}%</p><p>{formatDate(point.at)}</p>
    <p className="text-slate-300">{point.sessionLabel} / {point.kind}</p><p className="text-slate-300">{point.status}</p>
  </div>;
}

function UsageChart({ points, onViewSession }: { points: TrendPoint[]; onViewSession?: (id: string) => void }) {
  const segments = [...new Set(points.flatMap(point => point.segment === null ? [] : [point.segment]))];
  const data = points.map(point => ({ ...point, ...(point.segment === null ? {} : { [`segment_${point.segment}`]: point.value }) }));
  const span = points[points.length - 1].at - points[0].at;
  return <>
    <div data-testid="trend-chart" aria-hidden="true" className="h-[260px] min-w-0 w-full">
      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
        <LineChart data={data} accessibilityLayer={false} margin={{ top: 16, right: 16, bottom: 8, left: 0 }}>
          <CartesianGrid vertical={false} stroke="#334155" strokeDasharray="3 4" />
          <XAxis dataKey="at" type="number" domain={[points[0].at - 60000, points[points.length - 1].at + 60000]} tickCount={3} minTickGap={40}
            tickFormatter={value => span < 86400000
              ? new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', ...(span < 600000 ? { second: '2-digit' as const } : {}) })
              : new Date(value).toLocaleDateString([], { month: 'short', day: 'numeric' })} tick={{ fill: '#94a3b8', fontSize: 11 }} />
          <YAxis domain={[0, 100]} ticks={[0, 25, 50, 75, 100]} width={44} tickFormatter={value => `${value}%`} tick={{ fill: '#94a3b8', fontSize: 11 }} />
          <Tooltip content={<ChartTooltip />} />
          {segments.map(segment => <Line key={segment} type="linear" dataKey={`segment_${segment}`} stroke="#94a3b8" strokeWidth={2} dot={false} activeDot={false} connectNulls={false} isAnimationActive={false} tooltipType="none" />)}
          <Line dataKey="value" stroke="transparent" activeDot={false} isAnimationActive={false} connectNulls={false}
            dot={props => props.payload.value === null ? <g key={props.payload.id} /> : <circle key={props.payload.id} cx={props.cx} cy={props.cy} r={5} stroke={getTrendStatusColor(props.payload.status)} strokeWidth={2}
              data-status={props.payload.status} data-kind={props.payload.kind}
              fill={props.payload.kind === 'Original check' ? getTrendStatusColor(props.payload.status) : '#101821'} style={{ cursor: onViewSession ? 'pointer' : undefined }}
              onClick={() => onViewSession?.(props.payload.sessionId)} />} />
        </LineChart>
      </ResponsiveContainer>
    </div>
    <div className="flex flex-wrap gap-x-5 gap-y-2 text-xs text-slate-300">
      <span className="inline-flex items-center gap-2"><span className="h-2.5 w-2.5 rounded-full bg-slate-400" />Original check</span>
      <span className="inline-flex items-center gap-2"><span className="h-2.5 w-2.5 rounded-full border-2 border-slate-400" />Latest follow-up</span>
      <span className="inline-flex items-center gap-2 text-emerald-300"><Info size={14} />No warning in this reading</span>
      <span className="inline-flex items-center gap-2 text-amber-300"><TriangleAlert size={14} />Needs attention</span>
      <span>Unverified readings leave a gap.</span>
    </div>
  </>;
}

export default function DiagnosticTrends({ sessions, loading, error, onRefresh, onViewSession, selection, onSelectionChange, asOf }: {
  sessions: SessionSummary[]; loading: boolean; error: string | null; onRefresh: () => void;
  onViewSession?: (sessionId: string) => void;
  selection: TrendSelection; onSelectionChange: (value: TrendSelection) => void; asOf: number;
}) {
  const { device, scope, checkType, metric, days, limit } = selection;
  const update = (change: Partial<TrendSelection>) => onSelectionChange({ ...selection, ...change });
  const [filtersOpen, setFiltersOpen] = useState(false);
  const model = useMemo(() => buildTrendGroups(sessions), [sessions]);
  const devices = [...new Set(model.groups.map(group => group.deviceId))].sort();
  const selectedDevice = devices.includes(device) ? device : model.groups[0]?.deviceId ?? '';
  const deviceGroups = model.groups.filter(group => group.deviceId === selectedDevice);
  const scopes = getTrendScopeOptions(deviceGroups, checkType);
  const selectedScope = scopes.find(item => item.value === scope) ?? scopes.find(item => item.count > 0) ?? scopes[0];
  const group = deviceGroups.find(item => item.scope === selectedScope?.value);
  const readings = group ? getTrendReadingOptions(group) : [];
  const selectedReading = readings.find(item => item.value === metric) ?? readings[0];
  const selectedMetric = selectedReading?.value ?? '';
  const usageMetric = isTrendMetric(selectedMetric) ? selectedMetric : null;
  const cutoff = days === 'all' ? -Infinity : asOf - Number(days) * 86400000;
  const inPeriod = (point: { at: number }) => point.at >= cutoff && point.at <= asOf + 300000;
  const allUsage = group && usageMetric ? buildTrendPoints(group, usageMetric).filter(inPeriod) : [];
  const points = allUsage.slice(-limit);
  const observations = group ? buildTrendObservations(group, selectedMetric).filter(inPeriod) : [];
  const visibleObservations = observations.slice(-limit);
  const latest = visibleObservations[visibleObservations.length - 1];
  const latestUsage = points[points.length - 1];
  const summary = usageMetric ? points.length ? trendChange(points, usageMetric) : 'No saved readings in this period.'
    : observations.some(point => point.event) ? 'Crash records cover their own observation periods. Their counts are not compared as improvement.'
      : 'Saved observations, not a confirmed fix.';
  const config = TREND_METRICS.find(item => item.id === selectedMetric);
  const selectClass = 'block h-10 w-full min-w-0 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-3 text-sm text-white disabled:opacity-60';

  return <section aria-label="Diagnostic trends" className="min-w-0 space-y-5">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="min-w-0 flex-1"><h2 className="text-xl font-bold text-white">Changes across checks</h2>
        <p className="mt-1 text-sm text-slate-400">Original checks and the latest saved follow-up per session.</p></div>
      <button type="button" title="Refresh saved readings" aria-label="Refresh" onClick={onRefresh} disabled={loading} className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-[var(--rigmd-border)] text-cyan-300 disabled:opacity-50">
        <RefreshCw size={16} className={loading ? 'animate-spin' : ''} /></button>
    </div>
    {loading ? <p role="status" className="py-12 text-center text-slate-300">Loading saved readings...</p>
      : error ? <p role="alert" className="py-12 text-center text-amber-300">Saved readings could not be loaded. Please try refreshing.</p> : <>
        <div className="flex min-w-0 items-center gap-3 sm:hidden">
          <button type="button" aria-expanded={filtersOpen} aria-controls="trend-filters" onClick={() => setFiltersOpen(value => !value)} className="inline-flex h-10 shrink-0 items-center gap-2 rounded-lg border border-[var(--rigmd-border)] px-3 text-sm font-semibold text-cyan-300">
            <SlidersHorizontal size={16} />Filters</button>
          <p className="min-w-0 break-words text-xs text-slate-400">{selectedScope?.label} / {days === 'all' ? 'All saved dates' : `Last ${days} days`}</p>
        </div>
        <div id="trend-filters" className={`${filtersOpen ? 'grid' : 'hidden'} min-w-0 gap-3 sm:grid sm:grid-cols-3`}>
          <label className="min-w-0 text-sm text-slate-300">Computer
            <select aria-label="Trend computer" value={selectedDevice} disabled={!devices.length} onChange={event => update({ device: event.target.value, scope: '', limit: 20 })} className={`${selectClass} mt-2`}>
              {!devices.length && <option value="">No identified computer in history</option>}
              {devices.map((id, index) => <option key={id} value={id}>Saved computer {index + 1}</option>)}
            </select></label>
          <label className="min-w-0 text-sm text-slate-300">Check type
            <select aria-label="Trend check type" value={checkType} onChange={event => update({ checkType: event.target.value as TrendCheckType, scope: '', limit: 20 })} className={`${selectClass} mt-2`}>
              {TREND_CHECK_TYPES.map(item => <option key={item.id} value={item.id}>{item.label} ({trendCheckCount(deviceGroups, item.id)} eligible saved)</option>)}
            </select></label>
          <label className="min-w-0 text-sm text-slate-300">Period
            <select aria-label="Trend period" value={days} onChange={event => update({ days: event.target.value, limit: 20 })} className={`${selectClass} mt-2`}>
              <option value="all">All saved dates</option><option value="7">Last 7 days</option><option value="30">Last 30 days</option><option value="90">Last 90 days</option>
            </select></label>
          {checkType !== 'full' && <label className="min-w-0 text-sm text-slate-300 sm:col-span-3">{checkType === 'component' ? 'Part or saved part selection' : 'Problem'}
            <select aria-label="Trend check scope" value={selectedScope?.value ?? ''} onChange={event => update({ scope: event.target.value, limit: 20 })} className={`${selectClass} mt-2`}>
              {scopes.map(item => <option key={item.value} value={item.value}>{item.label} ({item.count ? `${item.count} eligible saved` : 'No eligible saved checks'})</option>)}
            </select></label>}
        </div>
        {!group ? <div className="border-y border-[var(--rigmd-border)] py-12 text-center">
          <ChartNoAxesCombined size={32} className="mx-auto mb-3 text-slate-400" />
          <h3 className="font-bold text-white">{sessions.length ? 'No eligible saved checks for this selection' : 'No saved checks yet'}</h3>
          <p className="mx-auto mt-2 max-w-lg text-sm text-slate-300">No eligible {selectedScope?.label.toLowerCase()} results in {TREND_CHECK_TYPES.find(item => item.id === checkType)?.label.toLowerCase()} for this computer. Existing results remain in Saved checks.</p>
        </div> : <>
          <label className="block min-w-0 border-b border-[var(--rigmd-border)] pb-4 text-sm text-slate-300">Saved reading
            <select aria-label="Saved reading to review" value={selectedMetric} onChange={event => update({ metric: event.target.value, limit: 20 })} className={`${selectClass} mt-2 sm:max-w-[520px]`}>
              {readings.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
            </select></label>
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div className="min-w-0 flex-1"><h3 className="break-words font-bold text-white">{config?.description ?? selectedReading?.label}</h3>
              <p data-testid="trend-change" className="mt-1 text-sm text-slate-300">{summary}</p></div>
            <div className="min-w-0 max-w-full text-right sm:max-w-[320px]"><p className="text-xs text-slate-400">Latest saved reading</p>
              <p data-testid="latest-trend-value" className={`${usageMetric ? 'text-2xl' : 'text-base'} break-words font-bold text-white`}>
                {usageMetric ? latestUsage?.value != null ? `${latestUsage.value}%` : 'Not verified' : latest?.value ?? 'Not recorded'}</p>
              {usageMetric && latestUsage && <p data-testid="latest-trend-status" className="mt-1 inline-flex items-center gap-1 text-xs" style={{ color: getTrendStatusColor(latestUsage.status) }}>
                {latestUsage.status === 'Needs attention' ? <TriangleAlert size={14} /> : latestUsage.value === null ? <CircleHelp size={14} /> : <Info size={14} />}{latestUsage.status}</p>}
              {latest && <p className="text-xs text-slate-400">{formatDate(latest.at)}{!latest.timeVerified && ' / Session saved'}</p>}</div>
          </div>
          {usageMetric ? <>
            {points.some(point => point.value !== null) ? <UsageChart points={points} onViewSession={onViewSession} />
              : <p role="status" className="border-y border-[var(--rigmd-border)] py-10 text-center text-slate-400">No verified percentages in this selection.</p>}
            <p className="text-sm text-slate-400">{usageMetric === 'storage_usage'
              ? 'These are the highest storage percentages reported in each check, not a test of drive health. The checked drives may differ.'
              : 'Workload can change between checks. Lower use does not prove a repair. Lines stop at missing or non-comparable readings.'}</p>
          </> : <p className="text-sm text-slate-400">{observations.some(point => point.event)
            ? 'No new matching records does not confirm a repair. Each recorded period remains separate.'
            : 'These observations do not confirm a repair or overall hardware health.'}</p>}
          <div aria-label="Saved trend readings" role="region" className="border-y border-[var(--rigmd-border)]">
            <div className="flex flex-wrap items-center justify-between gap-2 py-3 text-sm"><h3 className="font-bold text-white">Saved readings</h3>
              <span className="text-slate-400">{visibleObservations.length} of {observations.length} readings</span></div>
            {[...visibleObservations].reverse().map(observation => {
              const point = points.find(item => item.id === observation.id);
              const value = usageMetric ? point?.value != null ? `${point.value}%` : 'Not verified' : observation.value;
              const status = usageMetric ? point?.status ?? 'Not verified' : observation.status;
              const attention = usageMetric ? status === 'Needs attention' : observation.attention;
              const Icon = attention ? TriangleAlert : /not verified|unavailable|not assessed|not recorded/i.test(status) ? CircleHelp : Info;
              return <button key={observation.id} type="button" disabled={!onViewSession} onClick={() => onViewSession?.(observation.sessionId)}
                aria-label={`Open ${observation.sessionLabel}, ${observation.kind}, ${formatDate(observation.at)}`}
                className="grid min-h-[76px] w-full grid-cols-[20px_minmax(0,1fr)_18px] items-center gap-3 border-t border-[var(--rigmd-border)] py-3 text-left hover:bg-white/5 disabled:cursor-default">
                <Icon size={18} className={attention ? 'text-amber-300' : 'text-slate-400'} />
                <span className="min-w-0"><span className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1">
                  <span className="text-sm font-semibold text-white">{formatDate(observation.at)}</span>
                  <span className="min-w-0 max-w-full break-words text-sm font-bold text-white sm:max-w-[50%]">{value}</span></span>
                  <span className="mt-1 flex flex-wrap justify-between gap-x-4 gap-y-1 text-xs text-slate-400">
                    <span>{observation.sessionLabel} / {observation.kind}{!observation.timeVerified && ' / Session saved'}</span>
                    <span className={attention ? 'text-amber-300' : ''}>{status}</span></span>
                  {observation.event && <span className="mt-2 block text-xs text-slate-400">{observation.window
                    ? `${observation.window.sinceOriginal ? 'Since original check' : 'Recorded period'}: ${formatDate(observation.window.start)} to ${formatDate(observation.window.end)}`
                    : 'Observation period not verified'}</span>}
                </span><ArrowUpRight size={16} className="text-cyan-300" />
              </button>;
            })}
            {observations.length > limit && <button type="button" onClick={() => update({ limit: limit + 20 })} className="my-3 rounded-lg border border-[var(--rigmd-border)] px-3 py-2 text-sm font-semibold text-cyan-300">Show earlier readings</button>}
          </div>
        </>}
      </>}
    {!loading && !error && model.excludedSessions > 0 && <p className="text-xs text-slate-400">{model.excludedSessions} saved check{model.excludedSessions === 1 ? '' : 's'} lack the identity, timing, scope, or readings required here. Their evidence remains in Saved checks.</p>}
  </section>;
}
