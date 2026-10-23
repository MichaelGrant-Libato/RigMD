import { getDiagnosticOverview, getReadingPresentation, plainDiagnosticText, friendlyConfidence, getDiagnosticCoverageNotice, type DiagnosticReading } from './diagnosticLanguage.ts';
import { getResolutionCategory, getSessionCheckLabel } from './pastChecksFormatting.ts';
import { memoryWarningLabel, type ScanScope } from './repairSupport.ts';
import { REVIEW_THRESHOLDS } from './reviewThresholds.ts';
import { getFindingChanges } from './findingChanges.ts';

export interface CheckPresentationInput extends ScanScope {
  diagnosis_mode?: string;
  scenario_id?: string;
  diagnosed_category?: string;
  action_category?: string;
  primary_result?: string;
  recommended_next_step?: string;
  proof?: DiagnosticReading[];
  resolution_proof?: DiagnosticReading[];
  resolution_status?: string;
  resolution_summary?: string;
}

export function canCheckCurrentStatus(check: { session_id?: string | null } | null | undefined) {
  return Boolean(check?.session_id?.trim());
}

const warning = (row: DiagnosticReading) => /^(high|elevated|warning|critical|still needs attention)$/i.test(row.status ?? '');
const normal = (row: DiagnosticReading) => /^(normal|normal now)$/i.test(row.status ?? '');
const count = (row: DiagnosticReading) => /^(\d+)\s+found$/i.exec(row.value?.trim() ?? '');
const verifiedCount = (rows: DiagnosticReading[]) => rows.length > 0 && rows.every(row => {
  const match = count(row);
  const value = match ? Number(match[1]) : Number.NaN;
  return Number.isSafeInteger(value) && (value === 0 ? normal(row) : warning(row));
}) && rows.every(row => count(row)![1] === count(rows[0])![1]);

export function crashMetric(check: CheckPresentationInput) {
  const scenario = check.scenario_id?.trim().toLowerCase().replace(/_/g, '-');
  if (['app-crashes', 'application-crashes'].includes(scenario ?? '')) return 'application_crash_history';
  if (['blue-screen-crash', 'blue-screen', 'bsod', 'system-crashes'].includes(scenario ?? '')) return 'system_crash_history';
  return null;
}

export function getCheckOverview(check: CheckPresentationInput) {
  const fallback = getDiagnosticOverview(check.diagnosed_category, check.proof);
  const metric = crashMetric(check);
  const scenario = check.diagnosis_mode === 'scenario' && check.scenario_id;
  const title = scenario ? getSessionCheckLabel(check) : fallback.title;
  if (!metric) return { title, summary: plainDiagnosticText(check.primary_result), meaning: fallback.meaning };
  const rows = (check.proof ?? []).filter(row => row.metric_id === metric);
  const verified = verifiedCount(rows);
  const found = verified ? Math.max(...rows.map(row => Number(count(row)![1]))) : null;
  return {
    title,
    summary: found === null ? 'Crash records could not be verified for this check.' : found === 0
      ? 'No matching crash records were found in the period checked.' : `${found} matching crash record${found === 1 ? '' : 's'} found in the period checked.`,
    meaning: 'This checks Windows-recorded crashes, not every possible app problem. The cause of a reported crash has not been established. Other readings are shown separately.',
  };
}

export function getCheckStatus(check: CheckPresentationInput) {
  const base = getResolutionCategory(check.resolution_status, check.action_category, check.diagnosed_category, false, false, check.resolution_proof);
  const metric = crashMetric(check);
  if (!check.resolution_proof?.length && (!check.resolution_status || /^open$/i.test(check.resolution_status)))
    return { ...base, label: 'Awaiting follow-up', fallbackText: 'No follow-up readings have been saved yet. Check again when you want an updated result.' };
  if (!metric) {
    const changes = getFindingChanges(check.proof, check.resolution_proof);
    if (changes?.label) return { ...base, label: changes.label, fallbackText: changes.summary };
    const latestWarnings = (check.resolution_proof ?? []).filter(warning);
    if (latestWarnings.length && !(check.proof ?? []).some(warning)) {
      const newlyDetected = latestWarnings.every(row => {
        const before = (check.proof ?? []).filter(previous => row.metric_id && previous.metric_id === row.metric_id);
        return before.length > 0 && before.every(previous => normal(previous) && Boolean(previous.value?.trim()) &&
          !/unavailable|not verified|not available|could not/i.test(previous.value ?? ''));
      });
      const names = (rows: DiagnosticReading[]) => [...new Set(rows.map(row => plainDiagnosticText(row.label || row.metric_id || 'Unnamed reading')))].join(', ');
      const unavailable = (check.resolution_proof ?? []).filter(row => /unknown|unavailable/i.test(getReadingPresentation(row).status ?? ''));
      return { ...base, category: 'still_active' as const, label: newlyDetected ? 'New warnings detected' : 'Warnings detected',
        className: 'border-amber-500/40 bg-amber-500/10 text-amber-200',
        fallbackText: `${newlyDetected ? 'New warnings detected compared with the original check' : 'Warnings detected in the latest saved readings'}: ${names(latestWarnings)}.` +
          (unavailable.length ? ` Could not verify: ${names(unavailable)}.` : '') +
          ' This is a current-readings check, not confirmation of a repair or overall device health.' };
    }
    if (base.category === 'resolved_after_action') {
      const latestWarnings = check.resolution_proof?.some(warning);
      const missingLatest = check.resolution_proof?.some(row => /unknown|unavailable/i.test(getReadingPresentation(row).status ?? ''));
      if (!check.resolution_proof?.length || missingLatest || latestWarnings || changes?.unknown.length || changes?.active.length) return {
        ...base, category: latestWarnings || changes?.active.length ? 'still_active' as const : 'needs_recheck' as const,
        label: changes?.active.length ? 'Original warning still detected' : latestWarnings ? 'Other warnings detected' : 'Latest result not verified',
        className: 'border-slate-500/40 bg-slate-500/10 text-slate-300',
        fallbackText: changes?.summary || (missingLatest ? 'Some follow-up readings could not be verified. Missing measurements do not confirm a normal result.'
          : latestWarnings ? 'The latest readings contain a warning. Review the affected measurements; no repair is confirmed.'
          : 'No follow-up measurements were saved. A stored status alone cannot confirm that the original warning cleared.')
      };
      return { ...base, label: 'Warning not detected',
        fallbackText: 'The checked warning was not detected in the latest saved readings. This does not prove a repair or establish overall device health.' };
    }
    return base;
  }
  const rows = (check.resolution_proof ?? []).filter(row => row.metric_id === metric);
  const verified = verifiedCount(rows) && rows.every(row =>
    (row.window_kind === 'since_diagnosis' || /since diagnosis/i.test(row.label ?? '')));
  if (!verified) return { ...base, category: 'needs_recheck' as const, label: 'Crash records not verified',
    className: 'border-slate-500/40 bg-slate-500/10 text-slate-300',
    fallbackText: 'The follow-up crash records or their checked period could not be verified. Other readings do not establish whether another crash was recorded.' };
  const found = Math.max(...rows.map(row => Number(count(row)![1])));
  return { ...base, category: found === 0 ? 'no_new_events' as const : 'still_active' as const,
    label: found === 0 ? 'No new matching records' : 'New crash records found',
    className: found === 0 ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300' : 'border-amber-500/40 bg-amber-500/10 text-amber-200',
    fallbackText: found === 0 ? 'No new matching crash records were found since the original check. This does not confirm that an intermittent crash is fixed.'
      : `${found} matching crash record${found === 1 ? '' : 's'} found since the original check. Review the affected app or stop code; the cause is not established.` };
}

export function getAdditionalFindingStatus(readings: DiagnosticReading[] = []) {
  const memory = readings.filter(row => row.metric_id === 'memory_usage');
  const active = memoryWarningLabel(readings.filter(row => row.metric_id === 'memory_usage' || row.metric_id === 'memory_app_warning'));
  if (active) return active;
  if (!memory.length) return null;
  if (memory.every(row => {
    const match = /^(\d+(?:\.\d+)?)%/.exec(row.value ?? '');
    return normal(row) && match && Number(match[1]) < REVIEW_THRESHOLDS.memory;
  })) return 'Memory warning not detected';
  return 'Memory could not be verified';
}

export function getLatestCheckSummary(check: CheckPresentationInput) {
  if (!crashMetric(check)) {
    const latest = getCheckStatus(check);
    return ['Latest result not verified', 'Original warning still detected', 'Other warnings detected', 'New warnings detected', 'Warnings detected'].includes(latest.label)
      ? latest.fallbackText : check.resolution_summary || latest.fallbackText;
  }
  const parts = [getCheckStatus(check).fallbackText];
  const memory = getAdditionalFindingStatus(check.resolution_proof);
  if (memory) parts.push(`${memory}.`);
  if (check.resolution_proof?.some(row => row.metric_id === 'cpu_temperature' && /unknown|unavailable/i.test(row.status ?? '')))
    parts.push('Processor temperature could not be checked.');
  return parts.join(' ');
}

export function getCheckAdvice(check: CheckPresentationInput) {
  if (crashMetric(check)) return 'Review the affected app, event time and available error details in Windows Reliability Monitor. Historical records do not establish a cause or an ongoing fault. Other findings can be investigated in a separate check.';
  return plainDiagnosticText(check.recommended_next_step) || 'Review the recorded findings before making changes.';
}

export function getCheckEvidenceLabel(check: CheckPresentationInput & { confidence_label?: string }) {
  const metric = crashMetric(check);
  if (!metric) return friendlyConfidence(check.confidence_label);
  return verifiedCount((check.proof ?? []).filter(row => row.metric_id === metric))
    ? 'Crash records checked' : 'Crash records not verified';
}

export function getCheckCoverageNotice(check: CheckPresentationInput & { confidence_label?: string }) {
  const metric = crashMetric(check);
  if (!metric) return getDiagnosticCoverageNotice(check);
  if (!verifiedCount((check.proof ?? []).filter(row => row.metric_id === metric)))
    return 'Crash records could not be verified. Supporting readings do not establish whether a crash was recorded.';
  return check.proof?.some(row => row.metric_id !== metric && /unknown|unavailable/i.test(row.status ?? ''))
    ? 'The crash-record query completed. Some supporting readings were unavailable and remain unverified.' : null;
}

export function getCheckPanelClassName(check: CheckPresentationInput) {
  const status = getCheckStatus(check);
  if (crashMetric(check) || status.label === 'Awaiting follow-up') return 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)]';
  const category = status.category;
  if (category === 'resolved_after_action') return 'border-emerald-500/35 bg-emerald-500/[0.06]';
  if (category === 'still_active') return 'border-red-500/35 bg-red-500/[0.06]';
  if (category === 'needs_recheck') return 'border-orange-500/35 bg-orange-500/[0.06]';
  return 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)]';
}

export function repeatedFindingLabel(check: CheckPresentationInput) {
  const warnings = (check.proof ?? []).filter(warning);
  return warnings.length && warnings.every(row => row.metric_id === 'memory_usage' || row.metric_id === 'memory_app_warning')
    ? 'Repeated memory warnings' : 'Repeated findings';
}
