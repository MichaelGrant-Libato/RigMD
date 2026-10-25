import test from 'node:test';
import assert from 'node:assert/strict';
import { getAppObservationPresentation } from '../src/lib/appObservationPresentation.ts';

test('legacy process observations are historical, not a claim about current app state', () => {
  const proof = Object.freeze({ status: 'App still running in background', before: '3883.67 MB across 23 processes', after: '251.44 MB; 2 matching processes' });
  const rendered = getAppObservationPresentation(proof.status);
  assert.equal(rendered.status, 'Processes were still running at the action check');
  assert.match(rendered.meaning, /may have exited since/);
  assert.match(rendered.meaning, /did not force/);
  assert.equal(proof.status, 'App still running in background');
  assert.equal(proof.after, '251.44 MB; 2 matching processes');
});

test('zero observed processes does not certify memory savings or a permanent exit', () => {
  const rendered = getAppObservationPresentation('App closed');
  assert.equal(rendered.status, 'No matching processes at the action check');
  assert.match(rendered.meaning, /does not establish how much system memory/);
  assert.match(rendered.meaning, /reopened later/);
});

test('unknown observation stays unverified and unrelated proof is unchanged', () => {
  assert.equal(getAppObservationPresentation('App state not verified').status, 'App exit was not verified at the action check');
  for (const status of [undefined, '', 'Completed', 'App closed unexpectedly', '128 files removed']) {
    assert.equal(getAppObservationPresentation(status), null);
  }
});
