import test from 'node:test';
import assert from 'node:assert/strict';
import {
  getSessionCheckLabel,
  getResolutionCategory,
  matchesCheckType,
  getTelemetryDeltaSummary,
  parseComponentIds,
} from '../src/lib/pastChecksFormatting.ts';

test('unavailable temperature is not presented as a normal monitoring result', () => {
  assert.equal(getResolutionCategory('open', 'Monitor', 'Thermal telemetry unavailable').label, 'Could not verify');
  assert.equal(getResolutionCategory('open', 'Monitor', 'No Active Issue Detected').label, 'Monitor Only');
  assert.equal(getResolutionCategory('resolved', 'Monitor', 'Thermal telemetry unavailable').label, 'Not detected in this check');
});

test('parseComponentIds parses array, json, and delimited strings', () => {
  assert.deepEqual(parseComponentIds(['cpu', 'ram']), ['cpu', 'ram']);
  assert.deepEqual(parseComponentIds('["cpu", "disk"]'), ['cpu', 'disk']);
  assert.deepEqual(parseComponentIds('cpu, ram; disk'), ['cpu', 'ram', 'disk']);
  assert.deepEqual(parseComponentIds(''), []);
  assert.deepEqual(parseComponentIds(null), []);
});

test('getSessionCheckLabel produces readable grammatical labels', () => {
  assert.equal(
    getSessionCheckLabel({ diagnosis_mode: 'full' }),
    'Full device check'
  );

  assert.equal(
    getSessionCheckLabel({ diagnosis_mode: 'component', component_ids: 'ram' }),
    'Memory check'
  );

  assert.equal(
    getSessionCheckLabel({ diagnosis_mode: 'component', component_ids: 'cpu, ram' }),
    'Processor and Memory check'
  );

  assert.equal(
    getSessionCheckLabel({ diagnosis_mode: 'component', component_ids: 'cpu, ram, disk' }),
    'Processor, Memory, and Storage check'
  );

  assert.equal(
    getSessionCheckLabel({ diagnosis_mode: 'scenario', scenario_id: 'slow-boot' }),
    'Slow boot check'
  );
});

test('getResolutionCategory correctly assigns the 4 standardized status categories', () => {
  const crashNoEvents = getResolutionCategory('resolved', 'Monitor', 'Application crash scenario', false, true);
  assert.equal(crashNoEvents.category, 'no_new_events');
  assert.equal(crashNoEvents.label, 'No New Events');

  const resolvedAction = getResolutionCategory('resolved', 'Automated', 'High Temp Cache Pressure', true);
  assert.equal(resolvedAction.category, 'resolved_after_action');
  assert.equal(resolvedAction.label, 'Not detected after action');

  const stillActive = getResolutionCategory('still_active', 'Troubleshoot', 'Thermal Warning');
  assert.equal(stillActive.category, 'still_active');
  assert.equal(stillActive.label, 'Still Active');

  const monitorOnly = getResolutionCategory('open', 'Monitor Only', 'No active issue detected');
  assert.equal(monitorOnly.category, 'monitor_only');
  assert.equal(monitorOnly.label, 'Monitor Only');
  const unknown = getResolutionCategory('unknown', 'Monitor', 'No active issue detected');
  assert.equal(unknown.category, 'needs_recheck');
  assert.equal(unknown.label, 'Could not verify');
});

test('a lower workload is not described as a repair or proof of component health', () => {
  for (const finding of ['Elevated CPU Utilization', 'High Memory Pressure', 'OS performance degradation']) {
    const condition = getResolutionCategory('resolved', 'Troubleshoot', finding);
    assert.equal(condition.label, 'Not detected in this check');
    assert.doesNotMatch(condition.label, /resolved|fixed|healthy/i);
    assert.match(condition.fallbackText, /No repair is implied/);
    assert.match(condition.fallbackText, /not a complete hardware-health assessment/);
    const afterAction = getResolutionCategory('resolved', 'Troubleshoot', finding, true);
    assert.match(afterAction.fallbackText, /does not establish overall hardware health/);
  }
});

test('matchesCheckType correctly filters checks by type', () => {
  const fullSession = { diagnosis_mode: 'full', component_ids: '', scenario_id: '' };
  const compSession = { diagnosis_mode: 'component', component_ids: 'cpu', scenario_id: '' };
  const scenSession = { diagnosis_mode: 'scenario', component_ids: '', scenario_id: 'app-crashes' };
  const reactSession = { diagnosis_mode: 'full', last_action_summary: 'Cleaned temp files', action_category: 'Automated' };
  const recSession = { diagnosis_mode: 'full', is_recurring: true };

  assert.equal(matchesCheckType(fullSession, 'full'), true);
  assert.equal(matchesCheckType(compSession, 'full'), false);
  assert.equal(matchesCheckType(compSession, 'component'), true);
  assert.equal(matchesCheckType(scenSession, 'scenario'), true);
  assert.equal(matchesCheckType(reactSession, 'react'), true);
  assert.equal(matchesCheckType(recSession, 'recurring'), true);
});

test('getTelemetryDeltaSummary calculates differences accurately', () => {
  const original = [
    { label: 'Physical Memory (RAM)', value: '88.0%' },
    { label: 'Storage & S.M.A.R.T.', value: '88.4%' },
  ];
  const resolution = [
    { label: 'Physical Memory (RAM)', value: '43.0%' },
    { label: 'Storage & S.M.A.R.T.', value: '88.4%' },
  ];

  const delta = getTelemetryDeltaSummary(original, resolution);
  assert.ok(delta);
  assert.match(delta, /decreased by 45.0 percentage points/);
});

test('comparisons never concatenate numbers or subtract incompatible units', () => {
  const label = 'Memory use';
  const compare = (before, after) => getTelemetryDeltaSummary([{ label, value: before }], [{ label, value: after }]);
  assert.match(compare('90.1%', '89%'), /1.1 percentage points/);
  assert.match(compare('14.2 / 15.7 GB (90.5%)', '13 / 15.7 GB (82.8%)'), /changed from/);
  assert.doesNotMatch(compare('14.2 / 15.7 GB (90.5%)', '13 / 15.7 GB (82.8%)'), /decreased by/);
  assert.match(compare('1 GB', '900 MB'), /changed from/);
  assert.equal(compare('Not available', 'Not available'), null);
  assert.equal(getTelemetryDeltaSummary([{ label: 'Processor', value: '20%' }], [{ label, value: '30%' }]), null);
  assert.match(compare('90.1%', '90.1%'), /No measurable change/);
});

test('comparisons use measurement identity, not wording that may describe another part', () => {
  assert.match(getTelemetryDeltaSummary(
    [{ metric_id: 'memory_usage', label: 'Physical Memory (RAM)', value: '87.3%' }],
    [{ metric_id: 'memory_usage', label: 'Memory use', value: '87.8%' }]), /increased by 0.5 percentage points/);
  assert.equal(getTelemetryDeltaSummary(
    [{ metric_id: 'cpu_temperature', label: 'Temperature', value: '50\u00b0C' }],
    [{ metric_id: 'gpu_temperature', label: 'Temperature', value: '40\u00b0C' }]), null);
});
