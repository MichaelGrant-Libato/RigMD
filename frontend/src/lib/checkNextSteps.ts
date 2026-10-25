import { expandDiagnosticReadings, getReadingPresentation, plainDiagnosticText } from './diagnosticLanguage.ts';
import { readingTone } from './diagnosticReadingVisuals.ts';
import { canCheckCurrentStatus, crashMetric, getCheckOverview, getCheckStatus, hasDetectedWarning, type CheckPresentationInput } from './checkPresentation.ts';
import { getSessionCheckLabel } from './pastChecksFormatting.ts';
import { getFindingChanges } from './findingChanges.ts';

export function getCheckNextSteps(check: CheckPresentationInput) {
  const hasFollowUp = Boolean(check.resolution_proof?.length);
  const readings = expandDiagnosticReadings(hasFollowUp ? check.resolution_proof! : check.proof ?? []);
  const attention = readings.filter(row => readingTone(getReadingPresentation(row).status) === 'attention');
  const unavailable = readings.filter(row => readingTone(getReadingPresentation(row).status) === 'unknown');
  const status = getCheckStatus(check);
  const changes = hasFollowUp ? getFindingChanges(check.proof, check.resolution_proof) : null;
  const summary = !hasFollowUp ? getCheckOverview(check).meaning
    : crashMetric(check) ? status.fallbackText
      : changes?.summary || status.fallbackText;
  return { hasFollowUp, readings, attention, unavailable, summary: plainDiagnosticText(summary) };
}

export function getShortCheckSummary(check: CheckPresentationInput) {
  if (/component not present/i.test(check.diagnosed_category ?? '')) return 'This part was not detected in this check.';
  const view = getCheckNextSteps(check);
  if (crashMetric(check)) {
    const status = getCheckStatus(check);
    if (!view.hasFollowUp) return 'Crash records describe past events, not a confirmed cause.';
    if (status.label === 'No new matching records') return 'No new matching crash records were found. This does not confirm a repair.';
    if (status.label === 'New crash records found') return 'Windows recorded another matching crash. The cause is not confirmed.';
    return 'The follow-up crash records could not be verified.';
  }
  if (!view.readings.length) return 'Detailed readings were not saved for this check.';
  if (view.attention.length) return `${view.attention.length} reading${view.attention.length === 1 ? ' needs' : 's need'} attention.`;
  if (view.unavailable.length === view.readings.length) return 'These readings could not be checked.';
  if (view.unavailable.length) return 'No warning in the readings collected. Some checks remain unverified.';
  return 'No warning was found in the available readings.';
}

export function canRecheckDetectedFinding(check: CheckPresentationInput & { session_id?: string | null }) {
  return canCheckCurrentStatus(check) && hasDetectedWarning(check);
}

export function getResultHeading(check: CheckPresentationInput) {
  if (crashMetric(check) || !hasDetectedWarning(check)) return getSessionCheckLabel(check);
  const active = getCheckNextSteps(check).attention;
  const originalWarnings = expandDiagnosticReadings(check.proof ?? []).filter(row => readingTone(getReadingPresentation(row).status) === 'attention');
  const originalTitle = getCheckOverview(check).title;
  return originalWarnings.length && active.every(row => originalWarnings.some(before => row.metric_id ? before.metric_id === row.metric_id : before.label === row.label))
    ? originalTitle : 'Readings need attention';
}
