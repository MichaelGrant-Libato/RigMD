import test from 'node:test';
import assert from 'node:assert/strict';
import { getFollowUpReadings, MISSING_FOLLOW_UP } from '../src/lib/followUpReadings.ts';
import { getCheckNextSteps, getShortCheckSummary } from '../src/lib/checkNextSteps.ts';
import { getFindingDetails, getCoverageMessage } from '../src/lib/findingDetails.ts';
import { getFindingVisual, getUsageComparison } from '../src/lib/findingVisuals.ts';

const original = [
  { metric_id: 'cpu_usage', label: 'Processor activity', value: '83%', status: 'high', evidence_kind: 'measurement', captured_at: '2026-10-18T16:22:29Z' },
  { metric_id: 'cpu_temperature', label: 'Processor temperature', value: 'Unavailable', status: 'unknown', evidence_kind: 'unavailable' },
  { metric_id: 'memory_usage', label: 'Physical Memory', value: '96% (15.1 / 15.7 GB)', status: 'high', evidence_kind: 'measurement', captured_at: '2026-10-18T16:22:29Z' },
];
const latest = [{ metric_id: 'cpu_usage', label: 'Processor activity', value: '38%', status: 'normal now', evidence_kind: 'measurement', captured_at: '2026-10-18T16:23:39Z' }];

test('CPU-only follow-up discloses missing memory and temperature rather than returning a reassuring zero', () => {
  const check = { diagnosis_mode: 'component', component_ids: '["cpu","memory"]', proof: original, resolution_proof: latest, resolution_status: 'resolved' };
  const before = structuredClone(check);
  const view = getCheckNextSteps(check);
  assert.equal(view.attention.length, 0);
  assert.equal(view.unavailable.length, 2);
  assert.deepEqual(view.unavailable.map(row => row.metric_id).sort(), ['cpu_temperature', 'memory_usage']);
  assert.match(getShortCheckSummary(check), /Some checks remain unverified/);
  assert.match(getCoverageMessage(view.readings), /Not recorded in this follow-up:.*temperature.*memory/i);
  assert.deepEqual(check, before);
});

test('missing-evidence rows never copy original values, timestamps, windows, or rule versions', () => {
  const rows = getFollowUpReadings(original, latest);
  const missing = rows.filter(row => row.evidence_kind === MISSING_FOLLOW_UP);
  assert.equal(missing.length, 2);
  for (const row of missing) {
    assert.equal(row.value, 'Not recorded in follow-up');
    assert.equal(row.status, 'unknown');
    for (const key of ['captured_at', 'rule_version', 'window_kind', 'window_start', 'window_end']) assert.equal(row[key], undefined);
    assert.equal(getFindingVisual(row).kind, 'unavailable');
    assert.equal(getFindingVisual(row).title, 'No follow-up reading saved');
    assert.equal(getFindingDetails(row).statusLabel, 'Not recorded in follow-up');
    assert.equal(getUsageComparison(row, original), null);
  }
});

test('complete follow-ups keep real readings and genuine measured zero unchanged', () => {
  const readings = original.map(row => ({ ...row, value: row.metric_id === 'cpu_temperature' ? '40 C' : '0%', status: 'normal', evidence_kind: 'measurement' }));
  const result = getFollowUpReadings(original, readings);
  assert.deepEqual(result, readings);
  assert.ok(result.every(row => row.evidence_kind !== MISSING_FOLLOW_UP));
  assert.equal(getFindingVisual(result[0]).percent, 0);
});

test('an explicit unavailable follow-up is retained rather than replaced or counted twice', () => {
  const actual = { ...original[2], value: 'Unavailable', status: 'unknown', evidence_kind: 'unavailable', captured_at: '2026-10-18T16:23:39Z' };
  const result = getFollowUpReadings([original[2]], [actual]);
  assert.deepEqual(result, [actual]);
  assert.equal(getFindingDetails(result[0]).statusLabel, 'Reading unavailable');
});

test('duplicate original metric identities produce one missing item without selecting a historical value', () => {
  const result = getFollowUpReadings([original[2], { ...original[2], value: '50%', status: 'normal' }], latest);
  assert.equal(result.filter(row => row.metric_id === 'memory_usage').length, 1);
  assert.equal(result.find(row => row.metric_id === 'memory_usage').value, 'Not recorded in follow-up');
});

test('label-only legacy rows cannot invent stable metric identities or new tests', () => {
  const result = getFollowUpReadings([{ label: 'Memory', value: '95%', status: 'high' }], latest);
  assert.deepEqual(result, latest);
});

test('without a follow-up the original findings are not reclassified as missing follow-up evidence', () => {
  const view = getCheckNextSteps({ proof: original, resolution_proof: [] });
  assert.equal(view.hasFollowUp, false);
  assert.equal(view.unavailable.length, 1);
  assert.equal(view.attention.length, 2);
  assert.ok(view.readings.every(row => row.evidence_kind !== MISSING_FOLLOW_UP));
});
