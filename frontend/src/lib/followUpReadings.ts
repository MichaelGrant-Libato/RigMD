import { expandDiagnosticReadings, type DiagnosticReading } from './diagnosticLanguage.ts';

export const MISSING_FOLLOW_UP = 'missing_follow_up';

export function getFollowUpReadings(original: DiagnosticReading[] = [], latest: DiagnosticReading[] = []) {
  const readings = expandDiagnosticReadings(latest);
  const seen = new Set(readings.map(row => row.metric_id).filter(Boolean));
  const missing: DiagnosticReading[] = [];
  // These rows describe absent evidence; they are never persisted or used as action proof.
  for (const row of expandDiagnosticReadings(original)) {
    if (!row.metric_id?.trim() || seen.has(row.metric_id)) continue;
    seen.add(row.metric_id);
    missing.push({ metric_id: row.metric_id, label: row.label || row.metric_id,
      value: 'Not recorded in follow-up', status: 'unknown', evidence_kind: MISSING_FOLLOW_UP,
      meaning: 'No reading was saved for this metric in this follow-up. The original value has not been reused.' });
  }
  return [...readings, ...missing];
}
