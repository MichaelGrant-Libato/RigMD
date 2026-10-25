import test from 'node:test';
import assert from 'node:assert/strict';
import { canRecheckDetectedFinding, getResultHeading } from '../src/lib/checkNextSteps.ts';
import { getOriginalResultStatus } from '../src/lib/checkPresentation.ts';
import { getCoverageMessage, getFindingDetails } from '../src/lib/findingDetails.ts';
import { getHomeDashboardMessage } from '../src/lib/homeDashboardMessage.ts';

const memory = (value = '91%', status = 'high') => ({ metric_id: 'memory_usage', label: 'Physical Memory', value, status });
const missing = { metric_id: 'cpu_temperature', label: 'Processor temperature', value: 'Unavailable', status: 'unknown', evidence_kind: 'unavailable' };
const baseline = { session_id: 'saved', diagnosis_mode: 'full', diagnosed_category: 'High Memory Pressure', proof: [memory(), missing] };
test('recheck is shown only for detected warnings, not a category name or missing measurements', () => {
  assert.equal(canRecheckDetectedFinding(baseline), true);
  for (const resolution_proof of [[memory('76.6%', 'normal'), missing], [missing], []]) {
    const check = resolution_proof.length ? { ...baseline, resolution_proof } : { ...baseline, proof: [] };
    assert.equal(canRecheckDetectedFinding(check), false);
  }
  assert.equal(canRecheckDetectedFinding({ ...baseline, session_id: '' }), false);
  assert.equal(canRecheckDetectedFinding({ ...baseline, proof: [memory('Unavailable', 'high')] }), false);
});
test('crash recheck follows the selected event query, not incidental memory use', () => {
  const event = { metric_id: 'application_crash_history', value: '0 found', status: 'normal', window_kind: 'rolling_history' };
  const check = { ...baseline, diagnosis_mode: 'scenario', scenario_id: 'app-crashes', proof: [event, memory()] };
  assert.equal(canRecheckDetectedFinding(check), false);
  assert.equal(canRecheckDetectedFinding({ ...check, proof: [{ ...event, value: '2 found', status: 'high' }, memory()] }), true);
  assert.equal(canRecheckDetectedFinding({ ...check, resolution_proof: [{ ...event, window_kind: 'since_diagnosis' }, memory()] }), false);
  assert.equal(canRecheckDetectedFinding({ ...check, resolution_proof: [{ ...event, value: '2 found', status: 'high', window_kind: 'rolling_history' }] }), false);
  assert.equal(canRecheckDetectedFinding({ ...check, proof: [{ ...event, value: 'bad count', status: 'high' }] }), false);
});
test('headings follow current evidence and initial results do not demand unnecessary follow-up', () => {
  assert.equal(getResultHeading(baseline), 'Memory needs attention');
  assert.equal(getResultHeading({ ...baseline, resolution_proof: [memory('76.6%', 'normal'), missing] }), 'Full device check');
  assert.equal(getResultHeading({ ...baseline, proof: [memory('76.6%', 'normal')], resolution_proof: [memory()] }), 'Readings need attention');
  assert.equal(getOriginalResultStatus({ ...baseline, proof: [missing] }).label, 'Partially checked');
  assert.equal(getOriginalResultStatus({ ...baseline, proof: [memory()] }).label, 'Needs attention');
  assert.equal(getOriginalResultStatus({ ...baseline, diagnosed_category: 'Component Not Present', proof: [] }).label, 'Not applicable');
});
test('capability limits are separate from unavailable readings and query failures', () => {
  const battery = { metric_id: 'battery_drain', label: 'Battery wear and drain rate', value: 'Not verified', status: 'unknown',
    meaning: 'Charge is not capacity or drain rate. This check has no timed discharge measurement or full-charge/design-capacity comparison. Review a Windows battery report.' };
  assert.equal(getFindingDetails(battery).statusLabel, 'Not assessed');
  assert.match(getFindingDetails(battery).explanation, /does not measure battery wear/);
  assert.equal(getCoverageMessage([battery]), '1 check not assessed by this scan.');
  assert.equal(getCoverageMessage([battery, missing]), '1 check not assessed by this scan. 1 reading unavailable.');
  assert.equal(getFindingDetails({ ...battery, meaning: 'The Windows query failed.' }).statusLabel, 'Reading unavailable');
  assert.equal(getFindingDetails({ ...battery, value: 'Verified', status: 'normal' }).notAssessed, false);
  assert.equal(getCoverageMessage([missing, { ...missing, metric_id: 'gpu_temperature' }]), '2 readings unavailable.');
});
test('Home distinguishes current warnings, limited saved checks and saved alerts', () => {
  const dashboard = { last_saved_session: { ...baseline, action_category: 'Monitor', proof: [missing] }, warning_signs_active_count: 0 };
  const live = getHomeDashboardMessage(dashboard, ['Memory']);
  assert.equal(live.primary, 'current');
  assert.equal(live.title, 'Current readings need review');
  assert.doesNotMatch(live.subtitle, /Nothing needs to change|fixed|healthy/);
  assert.equal(getHomeDashboardMessage(dashboard).title, 'Your latest check was limited');
  assert.equal(getHomeDashboardMessage({ ...dashboard, warning_signs_active_count: 1 }, ['Memory']).primary, 'alerts');
  const escalation = { ...dashboard, last_saved_session: { ...baseline, action_category: 'Escalate' } };
  assert.match(getHomeDashboardMessage(escalation).title, /professional inspection/);
  assert.doesNotMatch(getHomeDashboardMessage({ ...escalation, last_saved_session: { ...escalation.last_saved_session, resolution_proof: [memory('76%', 'normal')] } }).title, /inspection/);
});
