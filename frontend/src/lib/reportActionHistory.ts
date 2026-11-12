import type { SessionSummary } from '../types/rigmd.ts';

export type ReportActionProof = { label?: string; status?: string; meaning?: string; before?: string; after?: string };
export type ReportActionRun = {
  run_id: string; status: string; created_at: string; completed_at?: string | null;
  attempts: Array<{ action_code: string; created_at: string; verification_status?: string | null;
    execution_summary?: string | null; proof?: ReportActionProof[] }>;
};
export type ReportActionHistory = { runs: ReportActionRun[]; unavailable?: boolean };
const record = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);
const optionalText = (value: unknown) => value == null || typeof value === 'string';

// One read for the selected check only. Never combine another check's actions with this report.
export async function loadReportActionHistory(
  fetchDetail: (path: string, options: RequestInit) => Promise<Pick<Response, 'ok' | 'json'>>,
  expected: SessionSummary, signal: AbortSignal,
): Promise<ReportActionHistory> {
  signal.throwIfAborted();
  const response = await fetchDetail(`/api/diagnosis/sessions/${encodeURIComponent(expected.session_id)}`,
    { signal, cache: 'no-store' });
  if (!response.ok) throw new Error('Saved action details could not be loaded.');
  const detail: unknown = await response.json();
  signal.throwIfAborted();
  if (!record(detail) || detail.session_id !== expected.session_id ||
    (expected.device_id && detail.device_id !== expected.device_id) ||
    (expected.created_at && detail.created_at !== expected.created_at) || !Array.isArray(detail.remediation_history)) {
    throw new Error('Saved action details could not be verified.');
  }
  const ids = new Set<string>();
  for (const run of detail.remediation_history) {
    if (!record(run) || typeof run.run_id !== 'string' || !run.run_id.trim() || ids.has(run.run_id) ||
      typeof run.status !== 'string' || typeof run.created_at !== 'string' || !optionalText(run.completed_at) ||
      !Array.isArray(run.attempts) || run.attempts.some(attempt => !record(attempt) ||
        typeof attempt.action_code !== 'string' || typeof attempt.created_at !== 'string' ||
        !optionalText(attempt.verification_status) || !optionalText(attempt.execution_summary) ||
        (attempt.proof != null && (!Array.isArray(attempt.proof) || attempt.proof.some(row => !record(row) ||
          ['label', 'status', 'meaning', 'before', 'after'].some(key => !optionalText(row[key]))))))) {
      throw new Error('Saved action details could not be verified.');
    }
    ids.add(run.run_id);
  }
  return { runs: detail.remediation_history as ReportActionRun[] };
}
