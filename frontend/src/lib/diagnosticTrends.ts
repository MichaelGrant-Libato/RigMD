import type { DiagnosticReading } from './diagnosticLanguage.ts';
import { getFindingVisual } from './findingVisuals.ts';
import { getFindingDetails } from './findingDetails.ts';
import { isEventReading } from './eventEvidence.ts';
import { COMPONENT_LABELS, SCENARIO_LABELS, getSessionCheckLabel, parseComponentIds } from './pastChecksFormatting.ts';
import type { SessionSummary } from '../types/rigmd.ts';

export const TREND_METRICS = [
  { id: 'memory_usage', label: 'Memory use', description: 'Memory used by running apps' },
  { id: 'cpu_usage', label: 'Processor activity', description: 'Processor activity during the check' },
  { id: 'storage_usage', label: 'Storage space used', description: 'Highest reported storage use' },
] as const;
export type TrendMetric = typeof TREND_METRICS[number]['id'];
export function getTrendStatusColor(status: string) {
  if (status === 'Needs attention') return '#fcd34d';
  if (status === 'No warning in this reading') return '#34d399';
  return '#94a3b8';
}
export type TrendCheckType = 'full' | 'component' | 'scenario';
export const TREND_CHECK_TYPES = [
  { id: 'full', label: 'Full computer checks' },
  { id: 'component', label: 'Part checks' },
  { id: 'scenario', label: 'Problem checks' },
] as const;
// Match the current visible scan choices; also retain saved combined/legacy selections.
export const TREND_COMPONENT_IDS = ['cpu', 'memory', 'gpu', 'storage', 'os', 'drivers', 'battery', 'network', 'display'];
export const TREND_SCENARIO_IDS = ['slow-system', 'slow-boot', 'blue-screen-crash', 'driver-error', 'no-display', 'overheating-loud-fan', 'network-problem', 'app-crashes', 'stuttering-freezing', 'storage-problem'];
export interface TrendSelection {
  device: string;
  checkType: TrendCheckType;
  scope: string;
  metric: string;
  days: string;
  limit: number;
}
export const DEFAULT_TREND_SELECTION: TrendSelection = { device: '', checkType: 'full', scope: 'full', metric: 'memory_usage', days: 'all', limit: 20 };
export interface TrendSnapshot {
  id: string;
  sessionId: string;
  sessionLabel: string;
  kind: 'Original check' | 'Latest follow-up';
  at: number;
  proof: DiagnosticReading[];
  originalAt?: number;
}
export interface TrendGroup {
  key: string;
  deviceId: string;
  scope: string;
  label: string;
  snapshots: TrendSnapshot[];
}
export interface TrendPoint {
  id: string;
  sessionId: string;
  sessionLabel: string;
  kind: TrendSnapshot['kind'];
  at: number;
  value: number | null;
  status: string;
  version: string;
  basis: string;
  segment: number | null;
}

function timestamp(value?: string | null) {
  // Undated and timezone-ambiguous legacy readings cannot establish a time series.
  if (!value) return null;
  const parts = /^(\d{4})-(\d{2})-(\d{2})T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/i.exec(value);
  if (!parts) return null;
  const year = Number(parts[1]), month = Number(parts[2]), day = Number(parts[3]);
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const daysInMonth = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (month < 1 || month > 12 || day < 1 || day > daysInMonth[month - 1]) return null;
  const at = Date.parse(value);
  return Number.isFinite(at) ? at : null;
}

function scopeKey(session: SessionSummary) {
  const mode = session.diagnosis_mode;
  if (mode === 'full') return 'full';
  if (mode === 'scenario' && /^[a-z][a-z0-9-]*$/.test(session.scenario_id ?? '')) return `scenario:${session.scenario_id}`;
  if (mode === 'component') {
    const parts = [...new Set(parseComponentIds(session.component_ids))].sort();
    if (parts.length && parts.every(part => /^[a-z][a-z0-9-]*$/.test(part))) return `component:${parts.join(',')}`;
  }
  return null;
}

export function buildTrendGroups(sessions: SessionSummary[]) {
  const groups = new Map<string, TrendGroup>();
  const counts = new Map<string, number>();
  for (const session of sessions) counts.set(session.session_id, (counts.get(session.session_id) ?? 0) + 1);
  let excludedSessions = 0;
  for (const session of sessions) {
    const scope = scopeKey(session);
    const created = timestamp(session.created_at);
    if (!session.session_id || counts.get(session.session_id) !== 1 || !scope || created === null) {
      excludedSessions++;
      continue;
    }
    let added = false;
    const add = (deviceId: string | undefined, proof: DiagnosticReading[] | undefined, at: number, kind: TrendSnapshot['kind']) => {
      if (!deviceId?.trim() || !Array.isArray(proof) || !proof.some(reading => typeof reading.metric_id === 'string' && reading.metric_id.trim())) return;
      const key = JSON.stringify([deviceId.trim(), scope]);
      let group = groups.get(key);
      if (!group) {
        group = { key, deviceId: deviceId.trim(), scope, label: getSessionCheckLabel(session), snapshots: [] };
        groups.set(key, group);
      }
      group.snapshots.push({ id: `${session.session_id}:${kind}`, sessionId: session.session_id,
        sessionLabel: session.session_code || 'Saved check', kind, at, proof,
        originalAt: kind === 'Latest follow-up' ? created : undefined });
      added = true;
    };
    add(session.device_id, session.proof, created, 'Original check');
    const checked = timestamp(session.resolution_checked_at);
    if (checked !== null && checked > created) add(session.resolution_device_id, session.resolution_proof, checked, 'Latest follow-up');
    if (!added) excludedSessions++;
  }
  return { groups: [...groups.values()].sort((a, b) => Math.max(...b.snapshots.map(s => s.at)) - Math.max(...a.snapshots.map(s => s.at))), excludedSessions };
}

export function buildTrendPoints(group: TrendGroup, metric: TrendMetric): TrendPoint[] {
  const points = group.snapshots.map<TrendPoint>(snapshot => {
    const matches = snapshot.proof.filter(reading => reading.metric_id === metric);
    const reading = matches.length === 1 ? matches[0] : null;
    const captured = timestamp(reading?.captured_at);
    const view = reading ? getFindingVisual(reading) : null;
    const format = metric === 'cpu_usage' ? /^\d+(?:\.\d+)?%$/ : metric === 'storage_usage' ? /^\d+(?:\.\d+)?% used$/
      : /^\d+(?:\.\d+)?%(?: \(\d+(?:\.\d+)? \/ \d+(?:\.\d+)? GB\))?$/;
    const usable = reading?.evidence_kind === 'measurement' && view?.kind === 'usage' && captured !== null &&
      format.test(reading.value ?? '') && !(metric === 'memory_usage' && reading.value?.includes('(') && !view.capacity) &&
      captured <= snapshot.at + 5000 && captured >= snapshot.at - 600000 &&
      (snapshot.originalAt === undefined || captured > snapshot.originalAt) && !reading.window_kind;
    return { id: snapshot.id, sessionId: snapshot.sessionId, sessionLabel: snapshot.sessionLabel, kind: snapshot.kind,
      at: captured !== null && usable ? captured : snapshot.at, value: usable ? view.percent : null,
      status: usable ? view.statusLabel : matches.length > 1 ? 'Conflicting saved readings' : reading ? 'Not verified' : 'Not recorded',
      version: usable && typeof reading.rule_version === 'string' ? reading.rule_version : '',
      basis: usable && metric === 'memory_usage' ? `memory:${view.capacity?.total ?? 'unrecorded'}` : metric, segment: null } satisfies TrendPoint;
  }).sort((a, b) => a.at - b.at || a.id.localeCompare(b.id));
  const times = new Map<number, number>();
  for (const point of points) times.set(point.at, (times.get(point.at) ?? 0) + 1);
  let segment = 0;
  let previous: TrendPoint | undefined;
  for (const point of points) {
    if (metric === 'storage_usage' || point.value === null || !point.version || times.get(point.at)! > 1) {
      previous = undefined;
      continue;
    }
    if (!previous || previous.version !== point.version || previous.basis !== point.basis) segment++;
    point.segment = segment;
    previous = point;
  }
  return points;
}

export function trendChange(points: TrendPoint[], metric: TrendMetric) {
  if (metric === 'storage_usage') return 'Drive identity was not saved; individual snapshots only.';
  const latest = points[points.length - 1];
  if (!latest || latest.value === null) return 'The latest saved reading could not be verified.';
  const comparable = points.filter(point => point.segment !== null && point.segment === latest.segment);
  const first = comparable[0];
  if (latest.segment === null || comparable.length < 2 || !first || first.value === null) return 'At least two comparable readings are needed.';
  const difference = Number((latest.value - first.value).toFixed(1));
  return difference === 0 ? `Same use: ${latest.value}% in the latest comparable readings.`
    : `${difference < 0 ? 'Lower' : 'Higher'} use: ${first.value}% to ${latest.value}% in the latest comparable readings.`;
}

export function isTrendMetric(metric: string): metric is TrendMetric {
  return TREND_METRICS.some(item => item.id === metric);
}

export function trendCheckCount(groups: TrendGroup[], checkType: TrendCheckType) {
  return new Set(groups.filter(group => group.scope === checkType || group.scope.startsWith(`${checkType}:`))
    .flatMap(group => group.snapshots.map(snapshot => snapshot.sessionId))).size;
}

export function getTrendScopeOptions(groups: TrendGroup[], checkType: TrendCheckType) {
  const choices = new Map<string, string>();
  if (checkType === 'full') choices.set('full', 'Full computer check');
  else if (checkType === 'component') for (const id of TREND_COMPONENT_IDS) choices.set(`component:${id}`, `${COMPONENT_LABELS[id]} check`);
  else for (const id of TREND_SCENARIO_IDS) choices.set(`scenario:${id}`, SCENARIO_LABELS[id]);
  for (const group of groups) {
    if (group.scope === checkType || group.scope.startsWith(`${checkType}:`)) choices.set(group.scope, choices.get(group.scope) ?? group.label);
  }
  return [...choices].map(([value, label]) => ({ value, label,
    count: new Set(groups.filter(group => group.scope === value).flatMap(group => group.snapshots.map(snapshot => snapshot.sessionId))).size }));
}

export function getTrendReadingOptions(group: TrendGroup) {
  const choices = new Map<string, string>();
  for (const snapshot of group.snapshots) for (const reading of snapshot.proof) {
    if (typeof reading.metric_id === 'string' && reading.metric_id.trim()) choices.set(reading.metric_id, getFindingDetails(reading).label);
  }
  const usageOrder = TREND_METRICS.map(item => item.id as string);
  return [...choices].map(([value, label]) => ({ value, label: TREND_METRICS.find(item => item.id === value)?.label ?? label }))
    .sort((a, b) => {
      const ai = usageOrder.indexOf(a.value), bi = usageOrder.indexOf(b.value);
      return (ai < 0 ? 100 : ai) - (bi < 0 ? 100 : bi) || a.label.localeCompare(b.label) || a.value.localeCompare(b.value);
    });
}

export interface TrendObservation {
  id: string;
  sessionId: string;
  sessionLabel: string;
  kind: TrendSnapshot['kind'];
  at: number;
  timeVerified: boolean;
  value: string;
  status: string;
  attention: boolean;
  event: boolean;
  window: { start: number; end: number; sinceOriginal: boolean } | null;
}

export function buildTrendObservations(group: TrendGroup, metric: string): TrendObservation[] {
  return group.snapshots.map(snapshot => {
    const matches = snapshot.proof.filter(reading => reading.metric_id === metric);
    const reading = matches.length === 1 ? matches[0] : null;
    const view = reading ? getFindingDetails(reading) : null;
    const unavailable = reading ? getFindingVisual(reading).kind === 'unavailable' : true;
    const captured = timestamp(reading?.captured_at);
    const timeVerified = captured !== null && captured <= snapshot.at + 5000 && captured >= snapshot.at - 600000 &&
      (snapshot.originalAt === undefined || captured > snapshot.originalAt);
    const event = Boolean(reading && isEventReading(reading));
    const start = timestamp(reading?.window_start), end = timestamp(reading?.window_end);
    const window = event && start !== null && end !== null && start <= end && timeVerified && end <= captured + 5000
      ? { start, end, sinceOriginal: reading?.window_kind === 'since_diagnosis' } : null;
    const status = matches.length > 1 ? 'Conflicting saved readings' : !reading ? 'Not recorded'
      : !timeVerified ? 'Reading time not verified' : event ? window && reading?.evidence_kind === 'event_log' && !unavailable
        ? 'Recorded Windows events' : 'Event check not verified' : unavailable ? view?.notAssessed ? 'Not assessed' : 'Reading unavailable' : view?.statusLabel ?? 'Not verified';
    return { id: snapshot.id, sessionId: snapshot.sessionId, sessionLabel: snapshot.sessionLabel, kind: snapshot.kind,
      at: timeVerified ? captured : snapshot.at, timeVerified,
      value: reading?.value || (matches.length > 1 ? 'Conflicting saved readings' : 'Not recorded'),
      status, attention: timeVerified && !unavailable && view?.tone === 'attention', event, window } satisfies TrendObservation;
  }).sort((a, b) => a.at - b.at || a.id.localeCompare(b.id));
}
