import type { DashboardSummary, SessionSummary } from '../types/rigmd.ts';
import { sortSessionsNewestFirst } from './reportFormatting.ts';

type ReportFetch = (path: string, init: RequestInit) => Promise<Pick<Response, 'ok' | 'json'>>;
const record = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);
const count = (value: unknown) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;

export async function loadReportData(fetchReport: ReportFetch, signal: AbortSignal) {
  signal.throwIfAborted();
  const options = { signal, cache: 'no-store' as const };
  const sessionsResponse = await fetchReport('/api/diagnosis/sessions', options);
  if (!sessionsResponse.ok) throw new Error('Saved checks could not be loaded.');
  const data: unknown = await sessionsResponse.json();
  const rows = Array.isArray(data) ? data : record(data) ? data.sessions : null;
  if (!Array.isArray(rows) || !rows.every(row => record(row) && typeof row.session_id === 'string' && row.session_id.trim()
    && (row.proof == null || Array.isArray(row.proof)) && (row.resolution_proof == null || Array.isArray(row.resolution_proof)))) {
    throw new Error('Saved checks could not be verified.');
  }
  signal.throwIfAborted();

  // Read the summary after the checks, then publish neither result until both are verified.
  const summaryResponse = await fetchReport('/api/dashboard/summary', options);
  if (!summaryResponse.ok) throw new Error('The saved-history summary could not be loaded.');
  const summary: unknown = await summaryResponse.json();
  const totals = record(summary) ? summary.totals : null;
  if (!record(summary) || !record(totals)
    || !['total_sessions', 'this_month_count', 'escalated_count'].every(key => count(totals[key]))
    || !count(summary.recurring_issues_count) || !count(summary.warning_signs_active_count)
    || typeof summary.server_time !== 'string' || !Number.isFinite(Date.parse(summary.server_time))
    || Number(totals.total_sessions) < rows.length) {
    throw new Error('The saved-history summary could not be verified.');
  }
  signal.throwIfAborted();
  return { sessions: sortSessionsNewestFirst(rows as SessionSummary[]), dashboard: summary as unknown as DashboardSummary };
}
