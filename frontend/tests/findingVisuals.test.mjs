import test from 'node:test';
import assert from 'node:assert/strict';
import { getFindingVisual, getUsageComparison } from '../src/lib/findingVisuals.ts';
import { REVIEW_THRESHOLDS } from '../src/lib/reviewThresholds.ts';

const memory = { metric_id: 'memory_usage', label: 'Physical Memory', value: '92.3% (14.5 / 15.7 GB)', status: 'high', evidence_kind: 'measurement', captured_at: '2026-10-25T14:00:00Z' };

test('memory usage shows recorded proportion and bounded derived capacity without changing evidence', () => {
  const copy = structuredClone(memory);
  const view = getFindingVisual(memory);
  assert.equal(view.kind, 'usage');
  assert.equal(view.percent, 92.3);
  assert.equal(view.remainder, 7.7);
  assert.deepEqual(view.capacity, { used: 14.5, total: 15.7, available: 1.2 });
  assert.equal(view.reviewAt, REVIEW_THRESHOLDS.memory);
  assert.match(view.summary, /does not mean the RAM is damaged/);
  assert.match(view.nextStep, /unsaved work/);
  assert.deepEqual(memory, copy);
});

test('percentages alone and inconsistent or different-unit capacity never invent GB values', () => {
  for (const value of ['92.3%', '92.3% (1 / 15.7 GB)', '92.3% (14.5 / 0 GB)', '92.3% (18 / 15.7 GB)', '92.3% (14.5 / 15.7 MB)', `0% (0 / ${'9'.repeat(400)} GB)`]) {
    assert.equal(getFindingVisual({ ...memory, value }).capacity, null);
  }
});

test('unavailable and invalid values never render a usage gauge, zero, healthy status or derived capacity', () => {
  for (const value of ['Unavailable', 'Not verified', '101%', '-5%', 'NaN%', '20 ms', '']) {
    const view = getFindingVisual({ ...memory, value, status: 'normal' });
    assert.equal(view.kind, 'unavailable');
    assert.equal(view.tone, 'unknown');
    assert.equal(view.percent, undefined);
    assert.doesNotMatch(view.summary, /was below|no warning was found/i);
  }
  const contradictory = getFindingVisual({ ...memory, status: 'normal', evidence_kind: 'unavailable' });
  assert.equal(contradictory.kind, 'unavailable');
  assert.equal(contradictory.tone, 'unknown');
});

test('measured zero and full usage remain valid while unclassified legacy values stay neutral', () => {
  for (const percent of [0, 100]) assert.equal(getFindingVisual({ ...memory, value: `${percent}%` }).percent, percent);
  const legacy = getFindingVisual({ ...memory, status: 'info' });
  assert.equal(legacy.tone, 'info');
  assert.equal(legacy.reviewAt, undefined);
});

test('storage space is a usage proportion, never a drive-health score', () => {
  const storage = getFindingVisual({ metric_id: 'storage_usage', value: '75.5% used', status: 'normal' });
  assert.equal(storage.kind, 'usage');
  assert.equal(storage.percent, 75.5);
  assert.equal(storage.remainder, 24.5);
  assert.equal(storage.reviewAt, REVIEW_THRESHOLDS.storage);
  assert.match(storage.summary, /does not test the health/);
  const volume = getFindingVisual({ metric_id: 'storage_usage_C:', value: '75.5% used; 20 GB free', status: 'normal' });
  assert.equal(volume.percent, 75.5);
  assert.equal(volume.capacity, null);
});

test('temperature absence and unassessed tests have distinct non-healthy visuals', () => {
  const unavailable = getFindingVisual({ metric_id: 'cpu_temperature', value: 'Unavailable', status: 'unknown' });
  assert.equal(unavailable.kind, 'unavailable');
  assert.equal(unavailable.temperature, true);
  assert.equal(unavailable.title, 'No verified reading');
  const fan = getFindingVisual({ metric_id: 'fan_operation', value: 'Not verified', status: 'unknown', meaning: 'This scan does not measure fan speed, airflow or noise.' });
  assert.equal(fan.title, 'Not tested by this scan');
  for (const value of ['0\u00b0C', '200\u00b0C', '85%']) assert.equal(getFindingVisual({ metric_id: 'cpu_temperature', value, status: 'normal' }).kind, 'unavailable');
  assert.equal(getFindingVisual({ metric_id: 'cpu_temperature', value: '64\u00b0C', status: 'normal' }).celsius, 64);
});

test('drive reports and battery charge do not become a comprehensive health or lifetime score', () => {
  const report = getFindingVisual({ metric_id: 'storage_health', value: 'No warning reported', status: 'info' });
  assert.equal(report.kind, 'report');
  assert.equal(report.tone, 'info');
  assert.match(report.summary, /not a full drive-health test/);
  assert.match(getFindingVisual({ metric_id: 'storage_health', value: 'Warning reported', status: 'high' }).nextStep, /Back up/);
  const charge = getFindingVisual({ metric_id: 'battery_charge', value: '67%', status: 'normal' });
  assert.equal(charge.usedLabel, 'Charge remaining');
  assert.equal(charge.capacity, null);
  assert.equal(charge.reviewAt, undefined);
  assert.match(charge.summary, /not battery wear/);
});

test('comparison uses only the same metric, one original and a genuinely later numeric reading', () => {
  const later = { ...memory, value: '76.6%', status: 'normal', captured_at: '2026-10-25T14:10:00Z' };
  assert.deepEqual(getUsageComparison(later, [memory]), { original: 92.3, latest: 76.6, description: 'Lower use in this follow-up' });
  for (const originals of [[], [memory, memory], [{ ...memory, metric_id: 'storage_usage' }], [{ ...memory, value: 'Unavailable' }], [{ ...memory, captured_at: '' }]]) {
    assert.equal(getUsageComparison(later, originals), null);
  }
  assert.equal(getUsageComparison(memory, [memory]), null);
  assert.equal(getUsageComparison({ ...later, captured_at: '2026-10-24T14:00:00Z' }, [memory]), null);
  assert.equal(getUsageComparison({ ...later, value: 'Unavailable', status: 'unknown' }, [memory]), null);
});

test('crash counts in different periods cannot be turned into misleading improvement charts', () => {
  const event = { metric_id: 'application_crash_history', value: '2 found', status: 'high', captured_at: memory.captured_at, window_kind: 'rolling_history' };
  const later = { ...event, value: '0 found', status: 'normal', captured_at: '2026-10-25T14:10:00Z', window_kind: 'since_diagnosis' };
  assert.equal(getFindingVisual(later).kind, 'observation');
  assert.equal(getUsageComparison(later, [event]), null);
});
