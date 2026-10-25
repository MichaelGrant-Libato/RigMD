import test from 'node:test';
import assert from 'node:assert/strict';
import { getFindingChanges } from '../src/lib/findingChanges.ts';
const row = (metric_id, status) => ({ metric_id, label: metric_id, status, value: 'measured' });

test('original memory clears independently of a newly observed CPU warning', () => {
  const changes = getFindingChanges([row('memory_usage', 'high'), row('cpu_usage', 'normal')],
    [row('memory_usage', 'normal'), row('cpu_usage', 'high'), row('cpu_temperature', 'unknown')]);
  assert.deepEqual(changes.cleared, ['memory_usage']);
  assert.deepEqual(changes.added, ['cpu_usage']);
  assert.equal(changes.label, 'Other warnings detected');
  assert.match(changes.summary, /Original warning no longer detected/);
  assert.doesNotMatch(changes.summary, /repaired|fixed|healthy/i);
});

test('missing or conflicting evidence cannot clear an original warning', () => {
  assert.deepEqual(getFindingChanges([row('memory_usage', 'high')],
    [{ ...row('memory_usage', 'normal'), value: 'Unavailable' }]).unknown, ['memory_usage']);
  for (const latest of [[row('memory_usage', 'unknown')], [row('cpu_usage', 'normal')]])
    assert.deepEqual(getFindingChanges([row('memory_usage', 'high')], latest).unknown, ['memory_usage']);
  const conflict = getFindingChanges([row('memory_usage', 'high')], [row('memory_usage', 'normal'), row('memory_usage', 'high')]);
  assert.deepEqual(conflict.active, ['memory_usage']);
  assert.deepEqual(conflict.cleared, []);
});

test('no saved baseline or no recheck never invents an outcome', () => {
  assert.equal(getFindingChanges([], [row('memory_usage', 'normal')]), null);
  assert.equal(getFindingChanges([row('memory_usage', 'high')], []), null);
});
