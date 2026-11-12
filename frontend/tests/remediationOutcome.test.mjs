import test from 'node:test';
import assert from 'node:assert/strict';
import { getActionExecution, getCleanupProofPresentation, getRemediationState, hasFailedExecution } from '../src/lib/remediationOutcome.ts';

test('a stopped cleanup stays failed even when follow-up reports the issue unresolved or cleared', () => {
  for (const verification of [0, 1, 2, 3, 'Unresolved', 'Resolved', 'Unknown']) {
    const result = { verification, execution: { success: false, summary: 'No files were removed. Review a fresh preview.' },
      attempts: [{ state: 4, verification }] };
    const before = JSON.stringify(result);
    assert.equal(getRemediationState(result), 'failed');
    assert.equal(hasFailedExecution(result), true);
    assert.match(getActionExecution(result).summary, /No files were removed/);
    assert.equal(JSON.stringify(result), before);
  }
});

test('saved attempt failures retain the backend result when a top-level execution is absent', () => {
  for (const state of [4, 'ExecutionFailed', 'Failed']) {
    const execution = { success: false, summary: 'The cleanup folder could not be verified.' };
    const result = { verification: 1, attempts: [{ state: 15, execution: { success: true } }, { state, execution }] };
    assert.equal(getRemediationState(result), 'failed');
    assert.equal(getActionExecution(result), execution);
  }
  assert.equal(getRemediationState({ verification: 1, attempts: [{ state: 'ExecutionFailed' }] }), 'failed');
});

test('completed cleanup and condition verification remain separate', () => {
  assert.equal(getRemediationState({ execution: { success: true }, verification: 1 }), 'unresolved');
  assert.equal(getRemediationState({ execution: { success: true }, verification: 3 }), 'info');
  assert.equal(getRemediationState({ execution: { success: true }, verification: 0 }), 'success');
  assert.equal(hasFailedExecution({ execution: { success: true }, verification: 2 }), false);
  assert.equal(getRemediationState({ safety: { isApproved: false }, attempts: [{ state: 1 }] }), 'rejected');
});

test('older cleanup receipts describe deleted counts rather than implying that the whole preview disappeared', () => {
  assert.deepEqual(getCleanupProofPresentation({ label: 'Approved temporary files', before: '500', after: '0' }),
    { label: 'Temporary files deleted', value: '0 of 500 previewed files' });
  assert.deepEqual(getCleanupProofPresentation({ label: 'Logical size of deleted files', before: '0', after: '0' }),
    { label: 'Size of deleted files', value: '0 bytes' });
  for (const item of [{ label: 'Other files', before: '500', after: '0' },
    { label: 'Approved temporary files', before: '500', after: '501' },
    { label: 'Approved temporary files', before: '500', after: 'unknown' }]) assert.equal(getCleanupProofPresentation(item), null);
});
