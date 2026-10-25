import test from 'node:test';
import assert from 'node:assert/strict';
import { getResultActionAccess, readRemediationEligibility } from '../src/lib/remediationEligibility.ts';

const id = '105c8fd2-d0ac-4048-8536-2ccfd7a949a9';
const newer = '205c8fd2-d0ac-4048-8536-2ccfd7a949a9';
const check = { session_id: id, diagnosis_mode: 'full', diagnosed_category: 'High Memory Pressure',
  proof: [{ metric_id: 'memory_usage', label: 'Physical Memory', value: '92%', status: 'high' }] };

test('historical warnings cannot offer changes while eligibility is pending or unavailable', () => {
  assert.deepEqual(getResultActionAccess(check, null), { remediation: false, recheck: null });
  assert.throws(() => readRemediationEligibility({}, id));
  assert.throws(() => readRemediationEligibility({ state: 'allowed' }, id));
  assert.throws(() => readRemediationEligibility({ state: 'unknown', message: 'unverified' }, id));
});

test('a superseded warning keeps its evidence but cannot offer remediation or a duplicate recheck', () => {
  const before = structuredClone(check);
  const eligibility = readRemediationEligibility({ state: 'superseded', message: 'This warning was not detected in a newer check.', newer_session_id: newer }, id);
  assert.deepEqual(getResultActionAccess(check, eligibility), { remediation: false, recheck: null });
  assert.equal(eligibility.newer_session_id, newer);
  assert.deepEqual(check, before);
});

test('unverified evidence offers a read-only current check, not remediation', () => {
  const eligibility = { state: 'recheck_required', message: 'Check current status before considering changes.' };
  assert.deepEqual(getResultActionAccess(check, eligibility), { remediation: false, recheck: 'current' });
  assert.deepEqual(getResultActionAccess({ ...check, proof: [] }, eligibility), { remediation: false, recheck: 'current' });
  assert.deepEqual(getResultActionAccess({ ...check, session_id: '' }, eligibility), { remediation: false, recheck: null });
});

test('normal or limited checks without an actionable warning do not require another check', () => {
  const eligibility = readRemediationEligibility({ state: 'not_indicated', message: 'No action is indicated by the saved readings.' }, id);
  assert.deepEqual(getResultActionAccess(check, eligibility), { remediation: false, recheck: null });
});

test('verified eligibility does not resurrect recheck for a cleared result, or invent a newer record link', () => {
  const eligibility = readRemediationEligibility({ state: 'allowed', message: '', newer_session_id: id }, id);
  assert.deepEqual(getResultActionAccess(check, eligibility), { remediation: true, recheck: 'again' });
  const cleared = { ...check, resolution_proof: [{ ...check.proof[0], status: 'normal', value: '65%' }] };
  assert.equal(getResultActionAccess(cleared, eligibility).recheck, null);
  assert.equal(eligibility.newer_session_id, undefined);
  assert.equal(readRemediationEligibility({ state: 'superseded', message: 'Newer result', newer_session_id: '<bad-id>' }, id).newer_session_id, undefined);
});
