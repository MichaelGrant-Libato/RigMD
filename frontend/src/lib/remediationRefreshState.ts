import type { RemediationEligibility } from './remediationEligibility.ts';

export type RemediationRefreshState = {
  identity: string;
  key: string;
  eligibility: RemediationEligibility | null;
  retainedEligibility: RemediationEligibility | null;
  pending: boolean;
  error: boolean;
};

export function getRemediationRefreshView(state: RemediationRefreshState, identity: string, key: string) {
  const sameCheck = state.identity === identity;
  const current = sameCheck && state.key === key;
  return {
    eligibility: current && !state.pending && !state.error ? state.eligibility : null,
    reviewEligibility: sameCheck ? state.eligibility ?? state.retainedEligibility : null,
    pending: !current || state.pending,
    error: current && state.error,
  };
}
