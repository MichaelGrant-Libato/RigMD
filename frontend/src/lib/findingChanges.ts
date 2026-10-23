import type { DiagnosticReading } from './diagnosticLanguage';
import { isEventReading, eventFollowUpSummary } from './eventEvidence.ts';

const warning = (row: DiagnosticReading) => /^(high|elevated|still needs attention)$/i.test(row.status ?? '');
const normal = (row: DiagnosticReading) => /^(normal|normal now)$/i.test(row.status ?? '') &&
  Boolean(row.value?.trim()) && !/unavailable|not verified|not available|could not/i.test(row.value ?? '');

export function getFindingChanges(original: DiagnosticReading[] = [], latest: DiagnosticReading[] = []) {
  const targets = original.filter(row => row.metric_id && warning(row));
  if (!targets.length || !latest.length) return null;
  const initialIds = new Set(original.map(row => row.metric_id).filter(Boolean));
  const cleared: string[] = [], active: string[] = [], unknown: string[] = [];
  const eventUpdates: string[] = [];
  for (const target of targets) {
    const matches = latest.filter(row => row.metric_id === target.metric_id);
    const name = target.label || target.metric_id!;
    if (isEventReading(target)) {
      eventUpdates.push(eventFollowUpSummary(matches));
      continue;
    }
    // Conflicting or missing evidence must never clear a warning.
    if (matches.some(warning)) active.push(name);
    else if (matches.length && matches.every(normal)) cleared.push(name);
    else unknown.push(name);
  }
  const added = latest.filter(row => row.metric_id && initialIds.has(row.metric_id) && warning(row) &&
    !targets.some(target => target.metric_id === row.metric_id)).map(row => row.label || row.metric_id!);
  const parts = [
    ...eventUpdates,
    cleared.length ? `Original warning no longer detected in this reading: ${cleared.join(', ')}.` : '',
    active.length ? `Original warning still detected: ${active.join(', ')}.` : '',
    unknown.length ? `Original warning could not be verified: ${unknown.join(', ')}.` : '',
    added.length ? `Other warnings now detected: ${added.join(', ')}.` : '',
  ].filter(Boolean);
  const label = (cleared.length > 0 || eventUpdates.length > 0) && active.length === 0 && unknown.length === 0 && added.length > 0
    ? 'Other warnings detected' : undefined;
  return { summary: parts.join(' '), cleared, active, unknown, added, eventUpdates, label };
}
