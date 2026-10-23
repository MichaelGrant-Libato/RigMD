import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { getCheckOverview, getCheckStatus, getAdditionalFindingStatus, repeatedFindingLabel, getLatestCheckSummary,
  getCheckEvidenceLabel, getCheckCoverageNotice, getCheckPanelClassName, canCheckCurrentStatus } from '../src/lib/checkPresentation.ts';

const event = (value = '0 found', status = 'normal', window_kind = 'since_diagnosis') => ({ metric_id: 'application_crash_history', label: 'Application crash records', value, status, window_kind });
const memory = (value = '93.6% (14.7 / 15.7 GB)', status = 'high') => ({ metric_id: 'memory_usage', label: 'Working memory', value, status });
const check = { diagnosis_mode: 'scenario', scenario_id: 'app-crashes', diagnosed_category: 'High Memory Pressure', action_category: 'Troubleshoot', primary_result: 'Memory use is high', proof: [event(), memory()] };

test('read-only follow-up is available for saved checks regardless of repair category', () => {
  for (const action_category of ['Monitor', 'Maintain', 'Troubleshoot', 'Escalate', '', undefined]) {
    assert.equal(canCheckCurrentStatus({ session_id: 'saved-check', action_category }), true);
  }
  for (const value of [null, undefined, {}, { session_id: '' }, { session_id: '   ' }]) assert.equal(canCheckCurrentStatus(value), false);
});

test('a processor Monitor check stays partial when its temperature cannot be verified', () => {
  const cpu = { diagnosis_mode: 'component', component_ids: '["cpu"]', diagnosed_category: 'Scanner evidence unavailable', action_category: 'Monitor',
    proof: [{ metric_id: 'cpu_usage', value: '19.2%', status: 'normal' }, { metric_id: 'cpu_temperature', value: 'Unavailable', status: 'unknown' }] };
  const latest = { ...cpu, resolution_status: 'needs_recheck', resolution_proof: cpu.proof };
  assert.equal(getCheckStatus(latest).label, 'Partially checked');
  assert.equal(getCheckStatus({ ...latest, resolution_status: 'resolved' }).label, 'Latest result not verified');
  assert.equal(getCheckStatus({ ...latest, resolution_status: 'resolved', resolution_proof: [{ metric_id: 'cpu_usage', value: '92%', status: 'high' }] }).label, 'Other warnings detected');
  assert.doesNotMatch(getLatestCheckSummary({ ...latest, resolution_status: 'resolved', resolution_summary: 'Everything is normal.',
    resolution_proof: [{ metric_id: 'cpu_usage', value: '92%', status: 'high' }] }), /Everything is normal/);
});

test('selected crash check stays primary while memory remains a separate finding', () => {
  const overview = getCheckOverview(check);
  assert.equal(overview.title, 'Application crashes check');
  assert.match(overview.summary, /No matching crash records/);
  assert.equal(getAdditionalFindingStatus(check.proof), 'Memory use is high');
  assert.equal(repeatedFindingLabel(check), 'Repeated memory warnings');
});

test('primary crash-query coverage is independent of supporting unavailable temperature', () => {
  const partial = { ...check, confidence_label: 'Partial', proof: [...check.proof,
    { metric_id: 'cpu_temperature', label: 'Processor temperature', value: 'Unavailable', status: 'unknown' }] };
  assert.equal(getCheckEvidenceLabel(partial), 'Crash records checked');
  assert.match(getCheckCoverageNotice(partial), /query completed.*supporting readings were unavailable/);
  const failed = { ...partial, proof: [{ ...event(), status: 'unknown' }] };
  assert.equal(getCheckEvidenceLabel(failed), 'Crash records not verified');
  assert.match(getCheckCoverageNotice(failed), /could not be verified/);
  assert.equal(getCheckEvidenceLabel({ confidence_label: 'Partial', diagnosis_mode: 'full' }), 'Evidence partly available');
});

test('a no-new-records badge does not colour the whole crash panel as resolved', () => {
  const latest = { ...check, resolution_status: 'resolved', resolution_proof: [event(), memory()] };
  assert.equal(getCheckStatus(latest).label, 'No new matching records');
  assert.doesNotMatch(getCheckPanelClassName(latest), /emerald|red/);
  assert.equal(getAdditionalFindingStatus(latest.resolution_proof), 'Memory use is high');
});

test('all screens can use one evidence-based follow-up label, without claiming a crash repair', () => {
  for (const resolution_status of ['Resolved', 'resolved', 'NoNewEvents', 'PartiallyChecked', 'StillActive']) {
    const latest = { ...check, resolution_status, resolution_proof: [event(), memory('65.6%', 'normal')] };
    assert.equal(getCheckStatus(latest).label, 'No new matching records');
    assert.match(getLatestCheckSummary(latest), /Memory warning not detected/);
    assert.match(getLatestCheckSummary(latest), /does not confirm/);
  }
});

test('missing, contradictory, failed or differently scoped crash evidence cannot clear a crash check', () => {
  const invalid = [[], [event('0 found', 'unknown')], [event('0 found', 'high')], [event('2 found', 'normal')], [event('0 found', 'normal', 'last_7_days')], [event(), event('2 found', 'high')], [event('9007199254740992 found', 'high')]];
  for (const resolution_proof of invalid) {
    assert.equal(getCheckStatus({ ...check, resolution_status: 'resolved', resolution_proof }).label, 'Crash records not verified');
  }
  assert.equal(getCheckStatus({ ...check, resolution_status: 'open' }).label, 'Awaiting follow-up');
  assert.equal(getCheckStatus({ ...check, resolution_proof: [event('2 found', 'high')] }).label, 'New crash records found');
});

test('memory result uses the shared review threshold and rejects invalid or inconsistent normal evidence', () => {
  assert.equal(getAdditionalFindingStatus([memory('81.9%', 'normal')]), 'Memory warning not detected');
  for (const value of ['82%', '101%', 'Unavailable', '-1%']) {
    assert.equal(getAdditionalFindingStatus([memory(value, 'normal')]), 'Memory could not be verified');
  }
  assert.equal(getAdditionalFindingStatus([memory('82%', 'high'), event('2 found', 'high')]), 'Memory use is high');
  assert.equal(getAdditionalFindingStatus([{ metric_id: 'memory_app_warning', status: 'high', value: 'App memory warning reported', evidence_kind: 'reported_status' }]), 'An app memory warning needs review');
  assert.equal(getCheckStatus({ diagnosed_category: 'High Memory Pressure', resolution_status: 'resolved' }).label, 'Latest result not verified');
  const saved = { diagnosed_category: 'High Memory Pressure', resolution_status: 'resolved', proof: [memory()] };
  assert.equal(getCheckStatus({ ...saved, resolution_proof: [memory('62%', 'normal')] }).label, 'Warning not detected');
  assert.equal(getCheckStatus({ ...saved, resolution_proof: [memory()] }).label, 'Original warning still detected');
  assert.equal(getCheckStatus({ ...saved, resolution_proof: [memory('Unavailable', 'unknown')] }).label, 'Latest result not verified');
});

test('original memory clearing and a new processor warning have the same label across screens', () => {
  const latest = { diagnosed_category: 'High Memory Pressure', resolution_status: 'still_active',
    proof: [memory(), { metric_id: 'cpu_usage', label: 'Processor activity', value: '25%', status: 'normal' }],
    resolution_proof: [memory('62%', 'normal'), { metric_id: 'cpu_usage', label: 'Processor activity', value: '96%', status: 'high' }] };
  assert.equal(getCheckStatus(latest).label, 'Other warnings detected');
});

test('both app-closing implementations prohibit force stopping and exact RAM-freed claims', () => {
  for (const path of ['RigMD.Api/Controllers/AutonomyController.cs', 'RigMD.Infrastructure/Remediation/Actions/TerminateProcessesAction.cs']) {
    const source = readFileSync(new URL(`../../backend-dotnet/${path}`, import.meta.url), 'utf8');
    assert.doesNotMatch(source, /\.Kill\s*\(|taskkill|force-terminate after|freed ~|reclaiming approximately/);
    assert.match(source, /CloseMainWindow\(/);
  }
});
