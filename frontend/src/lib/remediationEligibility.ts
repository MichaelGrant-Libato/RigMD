import { canRecheckDetectedFinding } from './checkNextSteps.ts';
import type { CheckPresentationInput } from './checkPresentation.ts';

export interface RemediationEligibility {
  state: 'allowed' | 'superseded' | 'recheck_required' | 'not_indicated';
  message: string;
  newer_session_id?: string;
  checked_at?: string;
}

export function readRemediationEligibility(value: unknown, sessionId: string): RemediationEligibility {
  if (!value || typeof value !== 'object') throw new Error('Missing action eligibility');
  const data = value as Record<string, unknown>;
  if (!['allowed', 'superseded', 'recheck_required', 'not_indicated'].includes(String(data.state)) || typeof data.message !== 'string') {
    throw new Error('Invalid action eligibility');
  }
  const id = typeof data.newer_session_id === 'string' && data.newer_session_id !== sessionId &&
    /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i.test(data.newer_session_id)
    ? data.newer_session_id : undefined;
  return {
    state: data.state as RemediationEligibility['state'],
    message: data.message,
    newer_session_id: id,
    checked_at: typeof data.checked_at === 'string' ? data.checked_at : undefined,
  };
}

export function getResultActionAccess(check: CheckPresentationInput & { session_id?: string }, eligibility: RemediationEligibility | null) {
  return {
    remediation: Boolean(check.session_id) && eligibility?.state === 'allowed',
    recheck: eligibility?.state === 'recheck_required' && check.session_id ? 'current'
      : eligibility?.state === 'allowed' && canRecheckDetectedFinding(check) ? 'again' : null,
  };
}
