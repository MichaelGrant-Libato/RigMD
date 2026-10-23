import type { DiagnosticReading } from './diagnosticLanguage';
import { REVIEW_THRESHOLDS } from './reviewThresholds.ts';

export interface ScanScope {
  diagnosis_mode?: string;
  component_ids?: string | string[];
  scenario_id?: string;
  diagnosed_category?: string;
}

export function allowsMemoryAction(scope: ScanScope) {
  const mode = scope.diagnosis_mode?.trim().toLowerCase();
  if (mode === 'full') return true;
  if (mode === 'component') {
    let parts: unknown = scope.component_ids ?? [];
    if (typeof parts === 'string') {
      const raw = parts.trim();
      try { parts = JSON.parse(raw); }
      catch { parts = raw.startsWith('[') ? [] : raw.split(/[,;\s]+/); }
    }
    return Array.isArray(parts) && parts.some(part => /^(memory|ram|memory \(ram\))$/i.test(String(part).trim()));
  }
  if (mode === 'scenario') return ['slow-system', 'stuttering-freezing'].includes(
    scope.scenario_id?.trim().toLowerCase().replace(/_/g, '-') ?? '');
  return !mode && /memory|\bram\b/i.test(scope.diagnosed_category ?? '');
}

export function memoryActionDisposition(scope: ScanScope, readings: DiagnosticReading[] = []) {
  if (!hasMemoryAction(readings)) return 'none';
  return allowsMemoryAction(scope) ? 'direct' : 'handoff';
}

export function requiresHardwareInspection(category?: string) {
  return /storage health|drive health|s\.m\.a\.r\.t|smart failure/i.test(category ?? '');
}

export function reviewOnlyNotice(category?: string) {
  if (/crash|stop error|stability risk/i.test(category ?? ''))
    return 'Review the affected app, event time and error details in Reliability Monitor. Crash records alone do not identify a repairable cause. Windows repair, cleanup and app closing are not offered as a fix for these records. If the problem returns, collect the matching record and follow the app or device maker\'s guidance.';
  if (/unavailable|could not verify|no active issue|component not present/i.test(category ?? ''))
    return 'No automatic repair is supported by this result. Review unavailable measurements or select a relevant check if symptoms continue. Missing readings do not establish a need for Windows repair or cleanup.';
  return null;
}

export const hardwareInspectionNotice = 'A drive-health warning was reported. Back up important files and arrange a hardware inspection. RigMD cannot repair physical damage. Cleanup and Windows repair tools are not a repair for this warning; the warning alone does not prove the exact fault.';

export function hasMemoryAction(readings: DiagnosticReading[] = []) {
  return readings.some(row => {
    if (!/^(high|elevated|still needs attention)$/i.test(row.status ?? '')) return false;
    if (row.metric_id === 'memory_app_warning') return row.evidence_kind === 'reported_status' && Boolean(row.value?.trim()) &&
      !/^(unavailable|not available|none|no warning|false)$/i.test(row.value?.trim() ?? '');
    const percent = /^(\d+(?:\.\d+)?)%/.exec(row.value ?? '');
    return row.metric_id === 'memory_usage' && percent !== null && Number(percent[1]) >= REVIEW_THRESHOLDS.memory && Number(percent[1]) <= 100;
  });
}

export function crashCheckNotice(original: DiagnosticReading[] = [], latest?: DiagnosticReading[]) {
  const event = original.find(row => /^(application|system)_crash_history$/.test(row.metric_id ?? ''));
  const match = /^(\d+)\s+found$/i.exec(event?.value?.trim() ?? '');
  const found = match ? Number(match[1]) : Number.NaN;
  const events = original.filter(row => /^(application|system)_crash_history$/.test(row.metric_id ?? ''));
  const valid = Number.isSafeInteger(found) && found >= 0 && events.every(row => row.metric_id === event?.metric_id &&
    /^(\d+)\s+found$/i.exec(row.value?.trim() ?? '')?.[1] === match?.[1] && (found === 0
      ? /^(normal|normal now)$/i.test(row.status ?? '') : /^(high|elevated|warning|critical)$/i.test(row.status ?? '')));
  const parts = [valid ? found === 0 ? 'The original check found no matching crash records in its checked period.'
    : `The original check found ${found} matching crash record${found === 1 ? '' : 's'}.`
    : 'The original crash records could not be verified.'];
  if (latest?.length) {
    const rows = latest.filter(row => row.metric_id === event?.metric_id ||
      !event && /^(application|system)_crash_history$/.test(row.metric_id ?? ''));
    if (rows.length && rows.every(row => (row.window_kind === 'since_diagnosis' || /since diagnosis/i.test(row.label ?? '')) &&
      /^(normal|normal now)$/i.test(row.status ?? '') && /^0\s+found$/i.test(row.value?.trim() ?? '')))
      parts.push('No new matching records were found during follow-up; earlier records remain saved.');
    else parts.push('Review the follow-up crash evidence and its checked period separately.');
  }
  parts.push('This does not establish a crash cause or confirm a repair. Review the affected app, time and available error details in Windows Reliability Monitor.');
  return parts.join(' ');
}

export function memoryWarningLabel(readings: DiagnosticReading[] = []) {
  const warnings = readings.filter(row => /^(high|elevated|still needs attention)$/i.test(row.status ?? ''));
  return hasMemoryAction(readings) && warnings.every(row => row.metric_id === 'memory_usage' || row.metric_id === 'memory_app_warning')
    ? warnings.some(row => row.metric_id === 'memory_usage') ? 'Memory use is high' : 'An app memory warning needs review' : null;
}

export function separateMemorySummary(readings: DiagnosticReading[] = []) {
  const event = readings.find(row => /^(application|system)_crash_history$/.test(row.metric_id ?? ''));
  if (!memoryWarningLabel(readings) || event?.status !== 'normal' || !/^0(?:\s+found)?$/.test(event.value ?? '')) return null;
  const since = event.window_kind === 'since_diagnosis' || /since diagnosis/i.test(event.label ?? '');
  const parts = [`${memoryWarningLabel(readings)}.`, since ? 'No new matching crash records were found since the original check.' : 'No matching crash records were found in the period checked.'];
  if (readings.some(row => row.metric_id === 'cpu_temperature' && /unknown|unavailable/.test(row.status ?? '')))
    parts.push('Processor temperature could not be checked.');
  parts.push('These are separate findings. High memory use does not confirm the cause of a crash.');
  return parts.join(' ');
}
