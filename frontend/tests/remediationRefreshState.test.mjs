import test from 'node:test';
import assert from 'node:assert/strict';
import { getRemediationRefreshView } from '../src/lib/remediationRefreshState.ts';
import { getResultActionAccess } from '../src/lib/remediationEligibility.ts';

const check = { session_id: 'saved-check' };
const allowed = { state: 'allowed', message: 'Latest saved evidence permits review.' };
const state = { identity: 'saved-check:revision1', key: 'request1', eligibility: allowed, retainedEligibility: allowed, pending: false, error: false };

test('focus refresh preserves the review while immediately withholding execution permission', () => {
  const before = JSON.stringify(state);
  const view = getRemediationRefreshView(state, state.identity, 'request2');
  assert.equal(getResultActionAccess(check, view.reviewEligibility).remediation, true);
  assert.equal(getResultActionAccess(check, view.eligibility).remediation, false);
  assert.equal(view.pending, true);
  assert.equal(JSON.stringify(state), before);
});

test('an in-progress refresh and a failed refresh retain the review but cannot permit changes', () => {
  for (const error of [false, true]) {
    const refreshing = { ...state, key: 'request2', eligibility: null, pending: !error, error };
    const view = getRemediationRefreshView(refreshing, state.identity, 'request2');
    assert.equal(getResultActionAccess(check, view.reviewEligibility).remediation, true);
    assert.equal(getResultActionAccess(check, view.eligibility).remediation, false);
    assert.equal(view.error, error);
  }
});

test('successful verification restores access, but a superseded result removes it', () => {
  const verified = getRemediationRefreshView(state, state.identity, state.key);
  assert.equal(getResultActionAccess(check, verified.eligibility).remediation, true);
  const superseded = { state: 'superseded', message: 'Use the newer check.' };
  const view = getRemediationRefreshView({ ...state, eligibility: superseded }, state.identity, state.key);
  assert.equal(getResultActionAccess(check, view.reviewEligibility).remediation, false);
  assert.equal(getResultActionAccess(check, view.eligibility).remediation, false);
});

test('a different check, changed proof revision or initial load cannot inherit a previous review', () => {
  for (const identity of ['another-check:revision1', 'saved-check:revision2']) {
    const view = getRemediationRefreshView(state, identity, state.key);
    assert.equal(view.reviewEligibility, null);
    assert.equal(view.eligibility, null);
  }
  const initial = getRemediationRefreshView({ ...state, eligibility: null, retainedEligibility: null, pending: true }, state.identity, state.key);
  assert.equal(initial.reviewEligibility, null);
  assert.equal(initial.eligibility, null);
});
