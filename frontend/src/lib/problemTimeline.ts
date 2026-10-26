import { buildTrendGroups, buildTrendObservations, buildTrendPoints, getSavedCheckScope, isTrendMetric,
  parseSavedTimestamp, type TrendGroup } from './diagnosticTrends.ts';
import { friendlyToolName } from './diagnosticLanguage.ts';
import type { SessionSummary } from '../types/rigmd.ts';

export interface ProblemReference { pattern_key: string; occurrences: Array<{ id: string }> }
export interface ProblemTimelineGroup extends TrendGroup { computerLabel: string }
export interface ProblemRun {
  run_id: string; status: string; created_at: string; completed_at?: string | null;
  attempts: Array<{ action_code: string }>;
}
export interface ProblemHistory { sessionId: string; runs: ProblemRun[] }
export function getRelatedWarningTiming(occurrences: Array<{ id: string }>, timestamps: Record<string, string>) {
  const times = occurrences.map(occurrence => parseSavedTimestamp(timestamps[occurrence.id]));
  const dated = times.filter((at): at is number => at !== null);
  return { at: dated.length ? Math.max(...dated) : null, complete: times.length > 0 && dated.length === times.length };
}
export interface ProblemEvent {
  id: string; sessionId: string; sessionLabel: string; at: number;
  timeVerified: boolean;
  kind: 'Original check' | 'Latest follow-up' | 'Saved action';
  value: string; status: string; tone: 'attention' | 'clear' | 'unknown' | 'neutral';
  window?: { start: number; end: number; sinceOriginal: boolean } | null;
}

export function buildProblemTimelineGroups(sessions: SessionSummary[], problem: ProblemReference, asOf: number) {
  const model = buildTrendGroups(sessions);
  const ids = new Set(problem.occurrences.map(item => item.id));
  const devices = [...new Set(model.groups.map(group => group.deviceId))].sort();
  const sessionCounts = new Map<string, number>();
  sessions.forEach(session => sessionCounts.set(session.session_id, (sessionCounts.get(session.session_id) ?? 0) + 1));
  const placed = new Set<string>();
  const groups: ProblemTimelineGroup[] = [];
  for (const sourceGroup of model.groups) {
    const group = { ...sourceGroup, snapshots: [...sourceGroup.snapshots] };
    // A known recheck with missing proof must not silently resurrect an older reading.
    for (const session of sessions) {
      const created = parseSavedTimestamp(session.created_at);
      if (sessionCounts.get(session.session_id) !== 1 || created === null || getSavedCheckScope(session) !== group.scope) continue;
      const append = (device: string | undefined, at: number, kind: 'Original check' | 'Latest follow-up', proof: SessionSummary['proof']) => {
        if (device?.trim() !== group.deviceId || group.snapshots.some(snapshot => snapshot.sessionId === session.session_id && snapshot.kind === kind)) return;
        group.snapshots.push({ id: `${session.session_id}:${kind}`, sessionId: session.session_id, sessionLabel: session.session_code || 'Saved check',
          at, kind, proof: proof ?? [], originalAt: kind === 'Latest follow-up' ? created : undefined });
      };
      append(session.device_id, created, 'Original check', session.proof);
      const checked = parseSavedTimestamp(session.resolution_checked_at);
      if (checked !== null && checked > created) append(session.resolution_device_id, checked, 'Latest follow-up', session.resolution_proof);
    }
    const related = group.snapshots.filter(snapshot => ids.has(snapshot.sessionId) && snapshot.at <= asOf + 300000 &&
      snapshot.proof.some(reading => reading.metric_id === problem.pattern_key));
    if (!related.length) continue;
    const firstAt = Math.min(...related.map(snapshot => snapshot.at));
    related.forEach(snapshot => placed.add(snapshot.sessionId));
    groups.push({ ...group, computerLabel: `Saved computer ${devices.indexOf(group.deviceId) + 1}`,
      snapshots: group.snapshots.filter(snapshot => snapshot.at >= firstAt && snapshot.at <= asOf + 300000) });
  }
  return { groups, unplacedCount: [...ids].filter(id => !placed.has(id)).length };
}

export function parseProblemSessions(payload: unknown): SessionSummary[] {
  const values = Array.isArray(payload) ? payload : payload && typeof payload === 'object' && 'sessions' in payload ? payload.sessions : null;
  if (!Array.isArray(values) || values.some(value => !value || typeof value !== 'object' || typeof value.session_id !== 'string' ||
    !value.session_id || ['session_code', 'created_at', 'device_id', 'resolution_device_id', 'resolution_checked_at', 'diagnosis_mode', 'component_ids', 'scenario_id']
      .some(key => value[key] != null && typeof value[key] !== 'string') ||
    ['proof', 'resolution_proof'].some(key => value[key] != null && (!Array.isArray(value[key]) ||
      value[key].some((row: unknown) => !row || typeof row !== 'object' || !('label' in row) || typeof row.label !== 'string' ||
        !('value' in row) || typeof row.value !== 'string' || ['metric_id', 'captured_at', 'status', 'meaning', 'window_start', 'window_end', 'window_kind']
          .some(field => field in row && (row as Record<string, unknown>)[field] != null && typeof (row as Record<string, unknown>)[field] !== 'string')))))) throw new Error('Saved checks unavailable');
  return values;
}

export function readProblemHistory(payload: unknown, expected: SessionSummary, scope: string): ProblemHistory {
  if (!payload || typeof payload !== 'object') throw new Error('Action history unavailable');
  const detail = payload as Record<string, unknown>;
  if (detail.session_id !== expected.session_id || !expected.device_id?.trim() || detail.device_id !== expected.device_id ||
    parseSavedTimestamp(expected.created_at) === null || typeof detail.created_at !== 'string' ||
    parseSavedTimestamp(detail.created_at as string) !== parseSavedTimestamp(expected.created_at) ||
    getSavedCheckScope(detail as unknown as SessionSummary) !== scope || !Array.isArray(detail.remediation_history)) {
    throw new Error('Action history identity or scope could not be verified');
  }
  const ids = new Set<string>();
  for (const run of detail.remediation_history) {
    if (!run || typeof run !== 'object' || typeof run.run_id !== 'string' || !run.run_id || ids.has(run.run_id) ||
      typeof run.status !== 'string' || typeof run.created_at !== 'string' ||
      (run.completed_at != null && typeof run.completed_at !== 'string') || !Array.isArray(run.attempts) ||
      run.attempts.some((attempt: { action_code?: unknown } | null) => !attempt || typeof attempt.action_code !== 'string')) {
      throw new Error('Action history unavailable');
    }
    ids.add(run.run_id);
  }
  return { sessionId: expected.session_id, runs: detail.remediation_history as ProblemRun[] };
}

export async function loadProblemHistories(sessions: SessionSummary[], scope: string,
  fetchDetail: (id: string, signal: AbortSignal) => Promise<unknown>, signal: AbortSignal) {
  const histories: ProblemHistory[] = [];
  const failed: string[] = [];
  let index = 0;
  // Bound parallel reads; this is never a scan or an action request.
  await Promise.all(Array.from({ length: Math.min(4, sessions.length) }, async () => {
    while (index < sessions.length) {
      signal.throwIfAborted();
      const session = sessions[index++];
      try {
        const payload = await fetchDetail(session.session_id, signal);
        signal.throwIfAborted();
        histories.push(readProblemHistory(payload, session, scope));
      } catch {
        signal.throwIfAborted();
        failed.push(session.session_id);
      }
    }
  }));
  signal.throwIfAborted();
  return { histories, failed };
}

export function buildProblemTimeline(group: TrendGroup, metric: string, histories: ProblemHistory[], asOf: number,
  originalSnapshots: TrendGroup['snapshots'] = group.snapshots) {
  const points = isTrendMetric(metric) ? new Map(buildTrendPoints(group, metric).map(point => [point.id, point])) : null;
  const events: ProblemEvent[] = buildTrendObservations(group, metric).map(observation => {
    const point = points?.get(observation.id);
    const status = points ? point?.status ?? 'Not verified' : observation.status;
    const tone = status === 'Needs attention' ? 'attention' : status === 'No warning in this reading' ? 'clear'
      : /not verified|unavailable|not assessed|not recorded|conflicting/i.test(status) ? 'unknown' : 'neutral';
    return { id: observation.id, sessionId: observation.sessionId, sessionLabel: observation.sessionLabel, at: observation.at,
      kind: observation.kind, timeVerified: observation.timeVerified, value: points ? point?.value != null ? `${point.value}%` : 'Not verified' : observation.value,
      status, tone, window: observation.window };
  });
  const readings = [...events];
  let unplacedActions = 0;
  const seen = new Set<string>();
  for (const history of histories) {
    // A follow-up on a legacy check does not establish the original action's computer.
    const original = originalSnapshots.find(snapshot => snapshot.sessionId === history.sessionId && snapshot.kind === 'Original check');
    if (!original) { unplacedActions += history.runs.length; continue; }
    for (const run of history.runs) {
      const id = `${history.sessionId}:action:${run.run_id}`;
      const started = parseSavedTimestamp(run.created_at), completed = parseSavedTimestamp(run.completed_at);
      if (seen.has(id)) continue;
      seen.add(id);
      if (started === null || started < original.at || started > asOf + 300000 ||
        (run.completed_at && (completed === null || completed < started || completed > asOf + 300000))) {
        unplacedActions++;
        continue;
      }
      const state = run.status.toLowerCase();
      events.push({ id, sessionId: history.sessionId, sessionLabel: original.sessionLabel, at: completed ?? started,
        kind: 'Saved action', timeVerified: true, value: [...new Set(run.attempts.map(attempt => friendlyToolName(attempt.action_code)))].join(', ') || 'Action run',
        status: state === 'completed' ? 'Action reported complete; effect not verified'
          : state === 'failed' ? 'Action reported failed' : 'Action run recorded; outcome not verified', tone: 'neutral' });
    }
  }
  events.sort((a, b) => a.at - b.at || a.id.localeCompare(b.id));
  const latest = readings[readings.length - 1];
  const savedActions = events.filter(event => event.kind === 'Saved action');
  const lastAction = savedActions[savedActions.length - 1];
  const awaitingReading = Boolean(lastAction && (!latest || lastAction.at >= latest.at));
  return { events, latest, awaitingReading, unplacedActions };
}
