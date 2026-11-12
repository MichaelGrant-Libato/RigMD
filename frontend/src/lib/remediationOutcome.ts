import type { AutonomyExecutionProof, AutonomyResult } from '../services/autonomyService.ts';

const attemptLabels = ['Planned', 'Safety rejected', 'Needs approval', 'Running', 'Failed', 'Checking result',
  'Fixed', 'Not fixed', 'Needs help', 'Could not confirm', 'Undo pending', 'Undone', 'Undo failed', 'Trying another option', 'Changed approach', 'Completed'];
const verificationLabels = ['Fixed', 'Not fixed', 'Needs help', 'Could not confirm'];
const label = (value: string | number | undefined, labels: string[]) => typeof value === 'number' ? labels[value] ?? '' : value ?? '';
const latestAttempt = (result?: AutonomyResult | null) => result?.attempts?.[result.attempts.length - 1];

export const getActionExecution = (result?: AutonomyResult | null) => result?.execution ?? latestAttempt(result)?.execution;

export function getCleanupProofPresentation(proof: AutonomyExecutionProof) {
  const count = (value?: string) => value && /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) ? Number(value) : null;
  const after = count(proof.after);
  if (proof.label === 'Approved temporary files') {
    const before = count(proof.before);
    if (before !== null && after !== null && after <= before) return {
      label: 'Temporary files deleted', value: `${after} of ${before} previewed files`,
    };
  }
  if (proof.label === 'Logical size of deleted files' && after !== null) return { label: 'Size of deleted files', value: `${after.toLocaleString()} bytes` };
  return null;
}

export function hasFailedExecution(result?: AutonomyResult | null) {
  return getActionExecution(result)?.success === false || /^(?:ExecutionFailed|Failed)$/i.test(label(latestAttempt(result)?.state, attemptLabels));
}

export function getRemediationState(result?: AutonomyResult | null) {
  if (!result) return 'info';
  // An unsuccessful action cannot become a completed action because a follow-up still has a warning.
  if (hasFailedExecution(result)) return 'failed';
  const attempt = latestAttempt(result);
  const verification = label(result.verification ?? attempt?.verification, verificationLabels).toLowerCase();
  const state = (label(attempt?.state, attemptLabels) || verification).toLowerCase();
  if (verification.includes('not fixed') || verification.includes('unresolved')) return 'unresolved';
  if (verification.includes('needs help') || verification.includes('worse')) return 'failed';
  if (verification.includes('could not confirm') || verification.includes('unknown')) return 'info';
  if (verification.includes('fixed') || verification.includes('resolved')) return 'success';
  if (state.includes('approval') || state.includes('consent') ||
    result.safety?.requiresUserConfirmation === true && result.safety.isApproved === false) return 'consent';
  if (state.includes('reject') || result.safety?.isApproved === false) return 'rejected';
  if (state.includes('not fixed') || state.includes('unresolved')) return 'unresolved';
  if (state.includes('failed') || state.includes('needs help')) return 'failed';
  if (state.includes('fixed') || state.includes('completed') || getActionExecution(result)?.success === true) return 'success';
  return 'info';
}
