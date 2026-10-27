import test from 'node:test';
import assert from 'node:assert/strict';
import { getActionEligibilityMessage, getCheckNextSteps, getRecommendedFindingSteps } from '../src/lib/checkNextSteps.ts';

const memory = (value = '87.5%', status = 'high') => ({ metric_id: 'memory_usage', label: 'Physical Memory', value, status, evidence_kind: 'measurement' });
const missing = { metric_id: 'cpu_temperature', label: 'Processor temperature', value: 'Unavailable', status: 'unknown', evidence_kind: 'unavailable' };
const baseline = { diagnosis_mode: 'full', diagnosed_category: 'High Memory Pressure', proof: [memory(), missing] };

test('recommended guidance covers each warning and keeps storage distinct from app memory', () => {
  const check = { ...baseline, proof: [...baseline.proof,
    { metric_id: 'storage_usage', label: 'Storage space', value: '82.1% used', status: 'high' }] };
  const before = JSON.stringify(check);
  const steps = getRecommendedFindingSteps(check);
  assert.equal(steps.length, 2);
  assert.match(steps[0].instruction, /Save your work.*review apps/);
  assert.match(steps[1].instruction, /Review files.*back up important files/);
  assert.doesNotMatch(steps[1].instruction, /close apps|memory/);
  assert.equal(JSON.stringify(check), before);
});

test('a normal or missing latest reading cannot reuse an earlier action recommendation', () => {
  for (const latest of [memory('76%', 'normal'), { ...memory('Unavailable', 'unknown'), evidence_kind: 'unavailable' },
    { metric_id: 'cpu_usage', label: 'Processor activity', value: '20%', status: 'normal' }]) {
    assert.deepEqual(getRecommendedFindingSteps({ ...baseline, resolution_proof: [latest] }), []);
  }
});

test('unavailable and informational readings never become recommended repairs', () => {
  assert.deepEqual(getRecommendedFindingSteps({ ...baseline, proof: [missing,
    { metric_id: 'graphics_devices', label: 'Windows device status', value: '0 device errors', status: 'info' }] }), []);
});

test('crash guidance recommends investigation rather than cleanup or Windows repair', () => {
  const steps = getRecommendedFindingSteps({ diagnosis_mode: 'scenario', scenario_id: 'app-crashes',
    proof: [{ metric_id: 'application_crash_history', label: 'Application crash records', value: '2 found', status: 'high' }] });
  assert.equal(steps.length, 1);
  assert.match(steps[0].instruction, /Reliability Monitor/);
  assert.doesNotMatch(steps[0].instruction, /cleanup|system.file|close.*app/i);
});

test('drive-health guidance preserves the backup advice shown in finding details', () => {
  const steps = getRecommendedFindingSteps({ proof: [
    { metric_id: 'storage_health', label: 'Drive-health warnings', value: 'Warning reported', status: 'high', evidence_kind: 'reported_status' },
  ] });
  assert.match(steps[0].instruction, /Back up important files/);
  assert.doesNotMatch(steps[0].instruction, /cleanup|temporary files/);
});

test('crash follow-ups retain investigation advice while memory remains a separate finding', () => {
  const event = { metric_id: 'application_crash_history', label: 'Application crash records', value: '2 found', status: 'high', evidence_kind: 'event_log' };
  const original = { diagnosis_mode: 'scenario', scenario_id: 'app-crashes', proof: [event, memory('91%'), missing] };
  const followUp = { ...original, resolution_proof: [{ ...event, value: '0 found', status: 'normal', window_kind: 'since_diagnosis' }, memory('92%'), missing] };
  for (const check of [original, followUp]) {
    const steps = getRecommendedFindingSteps(check);
    assert.equal(steps.length, 2);
    assert.match(steps[0].instruction, /Reliability Monitor/);
    assert.match(steps[1].instruction, /separate memory check/);
    assert.doesNotMatch(steps[1].instruction, /Save your work.*review apps/);
  }
  assert.equal(getRecommendedFindingSteps({ ...followUp, resolution_proof: [followUp.resolution_proof[0]] }).length, 1);
});

test('crash scope notices explain the action restriction without concealing identity or ordering failures', () => {
  const check = { diagnosis_mode: 'scenario', scenario_id: 'app-crashes' };
  const message = 'This result has no verified warning in a known check scope. Check the current status before choosing an action.';
  assert.match(getActionEligibilityMessage(check, { state: 'recheck_required', message }), /does not authorize a software repair/);
  for (const state of ['allowed', 'superseded', 'not_indicated']) {
    assert.equal(getActionEligibilityMessage(check, { state, message }), message);
  }
  assert.equal(getActionEligibilityMessage(baseline, { state: 'recheck_required', message }), message);
  const identity = 'This saved check\'s device cannot be verified. Run a new check on this computer.';
  assert.equal(getActionEligibilityMessage(check, { state: 'recheck_required', message: identity }), identity);
});
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
  assert.equal(view.unavailable.length, 2);
  assert.deepEqual(view.unavailable.map(row => row.metric_id).sort(), ['cpu_temperature', 'memory_usage']);
  assert.doesNotMatch(view.summary, /no longer detected|fixed/i);
});
test('crash summary keeps event checks separate from additional memory warnings', () => {
  const event = { metric_id: 'application_crash_history', label: 'Application crash records', value: '0 found', status: 'normal', window_kind: 'since_diagnosis' };
  const view = getCheckNextSteps({ diagnosis_mode: 'scenario', scenario_id: 'app-crashes', diagnosed_category: 'Application crash history requires review', proof: [{ ...event, value: '2 found', status: 'high', window_kind: 'recent' }], resolution_status: 'resolved', resolution_proof: [event, memory()] });
  assert.equal(view.attention.length, 1);
  assert.match(view.summary, /crash|record/i);
  assert.match(view.summary, /does not confirm/);
});
