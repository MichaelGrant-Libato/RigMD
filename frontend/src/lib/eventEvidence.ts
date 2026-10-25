import type { DiagnosticReading } from './diagnosticLanguage';

export function isEventReading(row: { metric_id?: string; label?: string }) {
  return ['application_crash_history', 'system_crash_history'].includes(row.metric_id ?? '') || /crash.*(?:records|events)|(?:records|events).*crash/i.test(row.label ?? '');
}

export function eventFollowUpSummary(readings: DiagnosticReading[]) {
  const since = readings.filter(row => row.window_kind === 'since_diagnosis' || /since diagnosis|since.*check|after.*diagnosis/i.test(row.label ?? ''));
  if (since.length && since.some(row => /^(high|elevated|still needs attention)$/i.test(row.status ?? '')))
    return 'Matching crash records were found during the follow-up period. Review the affected app or stop code; a cause is not established.';
  if (since.length && since.every(row => /^(normal|normal now)$/i.test(row.status ?? '') && /^0(?:\s+(?:found|records?|events?))?$/i.test(row.value ?? '')))
    return 'No new matching crash records were found during the follow-up period. Earlier saved records remain in this report; this does not prove a repair.';
  return 'The crash-record checks cover different or unverified periods. Their counts do not establish improvement.';
}
