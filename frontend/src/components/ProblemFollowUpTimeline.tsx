import { useEffect, useMemo, useRef, useState } from 'react';
import { ArrowUpRight, CircleHelp, Clock3, Info, ListChecks, RefreshCw, TriangleAlert, Wrench } from 'lucide-react';
import { apiGet } from '../lib/api';
import { buildProblemTimeline, buildProblemTimelineGroups, loadProblemHistories,
  type ProblemHistory, type ProblemReference } from '../lib/problemTimeline';
import type { SessionSummary } from '../types/rigmd';

export interface ProblemTimelineSelection { key: string; limit: number }
const dateLabel = (at: number) => new Date(at).toLocaleString([], {
  month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit',
});
const toneClass = { attention: 'text-amber-300', clear: 'text-emerald-300', unknown: 'text-slate-400', neutral: 'text-cyan-300' };

export default function ProblemFollowUpTimeline({ problem, sessions, error, asOf, selection, onSelectionChange, onOpenSession }: {
  problem: ProblemReference; sessions: SessionSummary[] | null; error: string | null; asOf: number;
  selection: ProblemTimelineSelection; onSelectionChange: (selection: ProblemTimelineSelection) => void;
  onOpenSession: (id: string) => void;
}) {
  const model = useMemo(() => buildProblemTimelineGroups(sessions ?? [], problem, asOf), [sessions, problem, asOf]);
  const defaultKey = model.groups[0]?.key;
  const group = selection.key ? model.groups.find(item => item.key === selection.key) : model.groups[0];
  useEffect(() => {
    if (!selection.key && defaultKey) onSelectionChange({ key: defaultKey, limit: selection.limit });
  }, [selection.key, selection.limit, defaultKey, onSelectionChange]);
  const snapshots = group?.snapshots.slice().sort((a, b) => a.at - b.at || a.id.localeCompare(b.id)) ?? [];
  const visibleGroup = group ? { ...group, snapshots: snapshots.slice(-selection.limit) } : null;
  const visibleIds = new Set(visibleGroup?.snapshots.map(snapshot => snapshot.sessionId));
  const targets = (sessions ?? []).filter(session => visibleIds.has(session.session_id));
  const signature = JSON.stringify([group?.key, selection.limit, targets.map(session => [session.session_id, session.created_at,
    session.resolution_checked_at, session.last_action_status, session.last_action_summary])]);
  const [actions, setActions] = useState<{ signature: string; histories: ProblemHistory[]; failed: string[]; loading: boolean; loadedAt?: number } | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => {
    request.current?.abort();
    return () => request.current?.abort();
  }, [signature]);
  const currentActions = actions?.signature === signature ? actions : null;
  const timeline = visibleGroup ? buildProblemTimeline(visibleGroup, problem.pattern_key, currentActions?.histories ?? [], asOf, group?.snapshots) : null;
  const loadActions = async () => {
    if (!group || currentActions?.loading) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setActions({ signature, histories: [], failed: [], loading: true });
    try {
      const result = await loadProblemHistories(targets, group.scope, async (id, signal) => {
        const response = await apiGet<unknown>(`/api/diagnosis/sessions/${encodeURIComponent(id)}`, { signal });
        return response.data;
      }, controller.signal);
      if (!controller.signal.aborted) setActions({ signature, ...result, loading: false, loadedAt: Date.now() });
    } catch {
      if (!controller.signal.aborted) setActions({ signature, histories: [], failed: targets.map(target => target.session_id), loading: false });
    }
  };

  return <section aria-label="Problem follow-up timeline" data-testid="problem-timeline" className="min-w-0 border-b border-[var(--rigmd-border)] pb-5">
    <h4 className="flex items-center gap-2 font-bold text-white"><ListChecks size={18} className="text-cyan-300" />Problem follow-up</h4>
    {!error && sessions !== null && model.groups.length > 0 && <label className="mt-4 block min-w-0 text-sm text-slate-300">Computer and check scope
      <select aria-label="Problem timeline selection" value={group?.key ?? ''}
        onChange={event => onSelectionChange({ key: event.target.value, limit: 10 })}
        className="mt-2 block h-10 w-full min-w-0 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-3 text-sm text-white">
        {!group && <option value="" disabled>Selected history is no longer available</option>}
        {model.groups.map(item => <option key={item.key} value={item.key}>{item.computerLabel} / {item.label}</option>)}
      </select>
    </label>}
    {error || sessions === null ? <p role="status" className="mt-3 text-sm text-amber-200">Saved checks could not be loaded for this timeline. Refresh Repeated Problems to try again.</p>
      : !group ? <p role="status" className="mt-3 text-sm text-slate-300">A timeline for one identified computer and check scope is not available. The related saved checks remain below.</p> : <>
        <p className="mt-2 text-xs text-slate-400">Matching readings do not establish one shared cause or a verified repair.</p>
        <div className="my-4 flex min-w-0 flex-wrap items-start justify-between gap-3 border-y border-[var(--rigmd-border)] py-4">
          <div className="min-w-0 flex-1"><p data-testid="problem-latest-status" className="font-semibold text-white">
            {timeline?.awaitingReading ? 'Awaiting a later reading' : timeline?.latest?.tone === 'attention' ? 'Warning in latest saved reading'
              : timeline?.latest?.tone === 'clear' ? 'No warning in latest saved reading'
                : timeline?.latest?.tone === 'unknown' ? 'Latest reading not verified' : 'Latest saved observation'}</p>
            <p className="mt-1 text-xs text-slate-400">{timeline?.latest && `${timeline.latest.timeVerified ? '' : 'Check saved: '}${dateLabel(timeline.latest.at)}`}</p></div>
          {timeline?.latest && <div className="min-w-0 max-w-full sm:max-w-[45%]">
            <p className="mb-1 text-xs text-slate-400">Latest saved reading</p>
            <p className="break-words text-base font-bold text-white">{timeline.latest.value}</p>
            <p className={`mt-1 text-xs ${toneClass[timeline.latest.tone]}`}>{timeline.latest.status}</p>
          </div>}
        </div>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <p data-testid="problem-actions-state" role="status" className="min-w-0 flex-1 text-xs text-slate-400">
            {!currentActions ? 'Past actions not loaded.' : currentActions.loading ? 'Loading saved actions...'
              : currentActions.failed.length ? `Action history could not be verified for ${currentActions.failed.length} check(s). Loaded history may be incomplete.`
                : currentActions.histories.every(history => !history.runs.length) ? 'No past actions were saved under these checks.'
                  : 'Actions saved under these checks; their effect on this finding is not verified.'}
          </p>
          <button type="button" onClick={loadActions} disabled={currentActions?.loading} title="Read saved action history; no action will run"
            className="inline-flex min-h-9 shrink-0 items-center gap-2 rounded-lg border border-[var(--rigmd-border)] px-3 py-2 text-xs font-semibold text-cyan-300 disabled:opacity-50">
            <RefreshCw size={14} className={currentActions?.loading ? 'animate-spin' : ''} />{currentActions ? 'Refresh past actions' : 'Load past actions'}
          </button>
        </div>
        {currentActions?.loadedAt && <p className="mb-3 text-xs text-slate-400">Action history loaded: {dateLabel(currentActions.loadedAt)}</p>}
        <ol className="min-w-0">
          {timeline?.events.map(event => {
            const Icon = event.kind === 'Saved action' ? Wrench : event.tone === 'attention' ? TriangleAlert
              : event.tone === 'unknown' ? CircleHelp : event.kind === 'Latest follow-up' ? RefreshCw : Info;
            return <li key={event.id} data-testid="problem-event" className="min-w-0 border-t border-[var(--rigmd-border-soft)]">
              <button type="button" onClick={() => onOpenSession(event.sessionId)} aria-label={`Open ${event.sessionLabel}, ${event.kind}, ${dateLabel(event.at)}`}
                className="grid min-h-[88px] w-full grid-cols-[20px_minmax(0,1fr)_16px] items-start gap-3 py-4 text-left hover:bg-white/5">
                <Icon size={18} className={`mt-1 ${toneClass[event.tone]}`} />
                <span className="min-w-0"><span className="block text-sm font-semibold text-white">{event.kind}</span>
                  <span className="mt-1 block break-words text-sm text-white">{event.value}</span>
                  <span className={`mt-1 block text-xs ${toneClass[event.tone]}`}>{event.status}</span>
                  <span className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-slate-400"><Clock3 size={12} />{!event.timeVerified && 'Check saved: '}{dateLabel(event.at)} / {event.sessionLabel}</span>
                  {event.window && <span className="mt-2 block text-xs text-slate-400">{event.window.sinceOriginal ? 'Since original check' : 'Recorded period'}: {dateLabel(event.window.start)} to {dateLabel(event.window.end)}</span>}
                </span><ArrowUpRight size={16} className="mt-1 text-cyan-300" />
              </button>
            </li>;
          })}
        </ol>
        {timeline && timeline.unplacedActions > 0 && <p className="mt-2 text-xs text-slate-400">{timeline.unplacedActions} saved action record(s) could not be placed with verified identity and timing. Their original check details remain available.</p>}
        {snapshots.length > selection.limit && <button type="button" onClick={() => onSelectionChange({ key: group.key, limit: selection.limit + 10 })}
          className="mt-3 inline-flex min-h-9 items-center gap-2 rounded-lg border border-[var(--rigmd-border)] px-3 py-2 text-xs font-semibold text-cyan-300">Show earlier checks</button>}
      </>}
    {!error && sessions !== null && model.unplacedCount > 0 && <p className="mt-3 text-xs text-slate-400">{model.unplacedCount} related check(s) lack the identity, scope, timing, or matching evidence required here. Their saved results remain available below.</p>}
  </section>;
}
