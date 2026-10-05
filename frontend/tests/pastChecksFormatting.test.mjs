import test from 'node:test';
import assert from 'node:assert/strict';
import {
  getSessionCheckLabel,
  getResolutionCategory,
  matchesCheckType,
  getTelemetryDeltaSummary,
  parseComponentIds,
} from '../src/lib/pastChecksFormatting.ts';

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
  assert.equal(resolvedAction.label, 'Resolved After Action');

  const stillActive = getResolutionCategory('still_active', 'Troubleshoot', 'Thermal Warning');
  assert.equal(stillActive.category, 'still_active');
  assert.equal(stillActive.label, 'Still Active');

  const monitorOnly = getResolutionCategory('open', 'Monitor Only', 'No active issue detected');
  assert.equal(monitorOnly.category, 'monitor_only');
  assert.equal(monitorOnly.label, 'Monitor Only');
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
  assert.match(delta, /decreased by 45.0%/);
});
