import { expandDiagnosticReadings, getReadingPresentation, plainDiagnosticText } from './diagnosticLanguage.ts';
import { readingTone } from './diagnosticReadingVisuals.ts';
import { canCheckCurrentStatus, crashMetric, getCheckOverview, getCheckStatus, hasDetectedWarning, type CheckPresentationInput } from './checkPresentation.ts';
import { getSessionCheckLabel } from './pastChecksFormatting.ts';
import { getFindingChanges } from './findingChanges.ts';
import { getFollowUpReadings } from './followUpReadings.ts';
import { getFindingVisual } from './findingVisuals.ts';
import { memoryActionDisposition } from './repairSupport.ts';

export function getCheckNextSteps(check: CheckPresentationInput) {
  const hasFollowUp = Boolean(check.resolution_proof?.length);
  const readings = hasFollowUp ? getFollowUpReadings(check.proof, check.resolution_proof) : expandDiagnosticReadings(check.proof ?? []);
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

export function getRecommendedFindingSteps(check: CheckPresentationInput) {
  const view = getCheckNextSteps(check);
  const crash = crashMetric(check);
  const separateMemory = memoryActionDisposition(check, view.readings) === 'handoff';
  const steps = view.attention.filter(reading => reading.metric_id !== crash).map(reading => {
    const finding = getFindingVisual(reading);
    return { label: finding.label, instruction: separateMemory && /^memory_(usage|app_warning)$/.test(reading.metric_id ?? '')
      ? 'Run a separate memory check before considering an app-closing action. This memory warning does not establish the cause of the selected problem.'
      : finding.nextStep };
  });
  if (crash) steps.unshift({ label: 'Review crash details',
    instruction: 'If crashes continue, open Windows Reliability Monitor and review the affected app, event time and error details. Past crash records do not establish the cause. Checking for new records does not confirm a repair.' });
  return steps;
}

export function getActionEligibilityMessage(check: CheckPresentationInput, eligibility: { state: string; message: string }) {
  if (crashMetric(check) && eligibility.state === 'recheck_required' &&
    eligibility.message === 'This result has no verified warning in a known check scope. Check the current status before choosing an action.') {
    return 'This crash check does not authorize a software repair. Review the crash details and check any separate memory warning with a memory check.';
  }
  return eligibility.message;
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
