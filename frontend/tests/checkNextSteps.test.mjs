import test from 'node:test';
import assert from 'node:assert/strict';
import { getCheckNextSteps } from '../src/lib/checkNextSteps.ts';

const memory = (value = '87.5%', status = 'high') => ({ metric_id: 'memory_usage', label: 'Physical Memory', value, status, evidence_kind: 'measurement' });
const missing = { metric_id: 'cpu_temperature', label: 'Processor temperature', value: 'Unavailable', status: 'unknown', evidence_kind: 'unavailable' };
const baseline = { diagnosis_mode: 'full', diagnosed_category: 'High Memory Pressure', proof: [memory(), missing] };
test('top summary uses original readings until follow-up, without changing history', () => {
  const before = JSON.stringify(baseline);
  const view = getCheckNextSteps(baseline);
  assert.equal(view.hasFollowUp, false);
  assert.equal(view.attention.length, 1);
  assert.equal(view.unavailable.length, 1);
  assert.equal(JSON.stringify(baseline), before);
});
test('normal latest memory does not inherit an old warning or hide missing readings', () => {
  const check = { ...baseline, resolution_status: 'needs_recheck', resolution_proof: [memory('76.6%', 'normal'), missing] };
  const before = JSON.stringify(check);
  const view = getCheckNextSteps(check);
  assert.equal(view.hasFollowUp, true);
  assert.equal(view.attention.length, 0);
  assert.equal(view.unavailable.length, 1);
  assert.match(view.summary, /no longer detected/i);
  assert.doesNotMatch(view.summary, /fixed|repaired|everything is normal/i);
  assert.equal(JSON.stringify(check), before);
});
test('unavailable memory cannot count as normal or as a confirmed warning', () => {
  const view = getCheckNextSteps({ ...baseline, resolution_proof: [{ ...memory('Unavailable', 'normal'), evidence_kind: 'unavailable' }] });
  assert.equal(view.attention.length, 0);
  assert.equal(view.unavailable.length, 1);
  assert.doesNotMatch(view.summary, /no longer detected|fixed/i);
});
test('crash summary keeps event checks separate from additional memory warnings', () => {
  const event = { metric_id: 'application_crash_history', label: 'Application crash records', value: '0 found', status: 'normal', window_kind: 'since_diagnosis' };
  const view = getCheckNextSteps({ diagnosis_mode: 'scenario', scenario_id: 'app-crashes', diagnosed_category: 'Application crash history requires review', proof: [{ ...event, value: '2 found', status: 'high', window_kind: 'recent' }], resolution_status: 'resolved', resolution_proof: [event, memory()] });
  assert.equal(view.attention.length, 1);
  assert.match(view.summary, /crash|record/i);
  assert.match(view.summary, /does not confirm/);
});
