import test from 'node:test';
import assert from 'node:assert/strict';
import { hasMemoryAction, memoryWarningLabel, separateMemorySummary, crashCheckNotice, allowsMemoryAction, memoryActionDisposition } from '../src/lib/repairSupport.ts';
import { getDiagnosticOverview } from '../src/lib/diagnosticLanguage.ts';
import { getResolutionCategory } from '../src/lib/pastChecksFormatting.ts';

const memory = { metric_id: 'memory_usage', value: '84.7% (13.3 / 15.7 GB)', status: 'high' };
const events = { metric_id: 'application_crash_history', label: 'Application crash records since diagnosis', value: '0 found', status: 'normal', window_kind: 'since_diagnosis' };
const readings = [memory, events, { metric_id: 'cpu_temperature', status: 'unknown', value: 'Unavailable' }];

test('a memory warning within a crash check requires a separate memory workflow', () => {
  assert.equal(hasMemoryAction(readings), true);
  assert.equal(memoryActionDisposition({ diagnosis_mode: 'scenario', scenario_id: 'app-crashes' }, readings), 'handoff');
  assert.equal(getDiagnosticOverview('Application stability risk needs review', readings).title, 'Memory use is high');
  assert.equal(getResolutionCategory('still_active', null, 'Application stability risk needs review', false, false, readings).label, 'Memory use is high');
  assert.match(separateMemorySummary(readings), /No new matching crash records/);
  assert.match(separateMemorySummary(readings), /Processor temperature could not be checked/);
  assert.match(crashCheckNotice(readings), /does not establish a crash cause/);
});

test('memory actions follow explicit full, component and problem scope rather than a diagnosis label', () => {
  for (const scope of [{ diagnosis_mode: 'full' }, { diagnosis_mode: 'component', component_ids: '["memory"]' },
    { diagnosis_mode: 'component', component_ids: ['cpu', 'ram'] }, { diagnosis_mode: 'scenario', scenario_id: 'slow-system' },
    { diagnosis_mode: 'scenario', scenario_id: 'stuttering-freezing' }, { diagnosed_category: 'High Memory Pressure' }]) {
    assert.equal(allowsMemoryAction(scope), true);
    assert.equal(memoryActionDisposition(scope, readings), 'direct');
  }
  for (const scope of [{ diagnosis_mode: 'component', component_ids: ['cpu'] }, { diagnosis_mode: 'component', component_ids: '["storage"]' },
    { diagnosis_mode: 'component', component_ids: '["memory"' }, { diagnosis_mode: 'scenario', scenario_id: 'app-crashes' },
    { diagnosis_mode: 'scenario', scenario_id: 'blue-screen-crash' }, { diagnosis_mode: 'scenario', scenario_id: 'network-problem' },
    { diagnosis_mode: 'unknown', component_ids: ['memory'] }, {}]) {
    assert.equal(allowsMemoryAction({ ...scope, diagnosed_category: 'High Memory Pressure', diagnosis_mode: scope.diagnosis_mode ?? 'unknown' }), false);
    assert.equal(memoryActionDisposition(scope, readings), 'handoff');
  }
  assert.equal(memoryActionDisposition({ diagnosis_mode: 'full' }, [{ ...memory, value: '60%', status: 'normal' }]), 'none');
  assert.equal(memoryActionDisposition({ diagnosis_mode: 'full' }, [{ ...memory, value: 'Unavailable', status: 'unknown' }]), 'none');
});

test('crash helper preserves historical records and identifies the no-new-records window', () => {
  const original = [{ ...events, value: '3 found', status: 'high', window_kind: 'rolling_history' }, memory];
  const notice = crashCheckNotice(original, readings);
  assert.match(notice, /original check found 3 matching crash records/);
  assert.match(notice, /No new matching records were found during follow-up/);
  assert.match(notice, /earlier records remain saved/);
  assert.doesNotMatch(notice, /No matching crash records were found in the period checked/);
  assert.doesNotMatch(crashCheckNotice(original, [{ ...events, status: 'unknown' }]), /No new matching records were found/);
  assert.doesNotMatch(crashCheckNotice(original, [{ ...events, window_kind: 'rolling_history', label: 'Crash records' }]), /No new matching records were found/);
  assert.match(crashCheckNotice([...original, events], readings), /original crash records could not be verified/);
});

test('missing, cleared, historical-threshold and invalid readings do not offer memory action', () => {
  for (const rows of [[], [events], [{ ...memory, status: 'normal', value: '62%' }], [{ ...memory, status: 'unknown' }],
    [{ ...memory, value: 'Unavailable' }], [{ ...memory, value: '78.8%' }], [{ ...memory, value: '101%' }]])
    assert.equal(hasMemoryAction(rows), false);
  assert.equal(hasMemoryAction([{ ...memory, value: '82%' }]), true);
});

test('multiple warnings are not hidden behind a memory-only label', () => {
  const multiple = [...readings, { metric_id: 'cpu_usage', value: '100%', status: 'high' }];
  assert.equal(hasMemoryAction(multiple), true);
  assert.equal(memoryWarningLabel(multiple), null);
  assert.equal(separateMemorySummary(multiple), null);
});

test('an app-specific memory warning is not relabelled as high system RAM', () => {
  const app = { metric_id: 'memory_app_warning', status: 'high', evidence_kind: 'reported_status', value: 'Unusual memory growth reported' };
  assert.equal(hasMemoryAction([app]), true);
  assert.equal(memoryWarningLabel([app]), 'An app memory warning needs review');
  assert.equal(hasMemoryAction([{ ...app, value: 'Unavailable' }]), false);
});
