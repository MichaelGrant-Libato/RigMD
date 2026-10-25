import test from 'node:test';
import assert from 'node:assert/strict';
import { getFindingDetails } from '../src/lib/findingDetails.ts';
import { getShortCheckSummary } from '../src/lib/checkNextSteps.ts';

const memory = (value = '92.3% (14.5 / 15.7 GB)', status = 'high') => ({ metric_id: 'memory_usage', label: 'Physical Memory', value, status, captured_at: '2026-10-25T09:09:22Z' });
test('memory highlights stay short while full evidence and risk remain available', () => {
  const reading = memory();
  const before = JSON.stringify(reading);
  const view = getFindingDetails(reading);
  assert.equal(view.valueSummary, '92.3%');
  assert.equal(view.value, reading.value);
  assert.equal(view.tone, 'attention');
  assert.match(view.explanation, /does not prove that the RAM is damaged/);
  assert.match(view.nextStep, /unsaved work/);
  assert.equal(JSON.stringify(reading), before);
});
test('unavailable or contradictory readings never become healthy summaries', () => {
  for (const status of ['unknown', 'normal', 'high']) {
    const view = getFindingDetails({ ...memory('Unavailable', status), evidence_kind: 'unavailable' });
    assert.equal(view.tone, 'unknown');
    assert.equal(view.statusLabel, 'Reading unavailable');
    assert.match(view.explanation, /could not obtain a verified reading/);
    assert.doesNotMatch(view.explanation, /below the review level/);
  }
});
test('legacy information-only memory is not described as low memory pressure', () => {
  const view = getFindingDetails(memory('90%', 'info'));
  assert.equal(view.tone, 'info');
  assert.doesNotMatch(view.explanation, /was below|RAM is damaged\.$/);
  assert.match(view.nextStep, /new reading/);
});
test('crash and storage details keep their limits understandable', () => {
  const crash = getFindingDetails({ metric_id: 'application_crash_history', value: '0 found', status: 'normal', window_kind: 'since_diagnosis' });
  assert.match(crash.explanation, /after the original check/);
  assert.match(crash.explanation, /does not prove/);
  const unclassified = getFindingDetails({ metric_id: 'application_crash_history', value: '2 found', status: 'info' });
  assert.doesNotMatch(unclassified.explanation, /no matching crash/i);
  const disk = getFindingDetails({ metric_id: 'storage_usage', value: '95% used', status: 'high' });
  assert.match(disk.explanation, /does not test drive health/);
  assert.match(disk.nextStep, /back up/);
});
test('short summaries preserve pending, partial and scoped crash distinctions', () => {
  assert.equal(getShortCheckSummary({ proof: [memory()] }), '1 reading needs attention.');
  assert.equal(getShortCheckSummary({ proof: [] }), 'Detailed readings were not saved for this check.');
  assert.equal(getShortCheckSummary({ proof: [{ ...memory('Unavailable', 'unknown'), evidence_kind: 'unavailable' }] }), 'These readings could not be checked.');
  const cleared = { proof: [memory()], resolution_proof: [memory('76.6%', 'normal')], resolution_status: 'resolved' };
  assert.equal(getShortCheckSummary(cleared), 'No warning was found in the available readings.');
  const crash = { scenario_id: 'app-crashes', proof: [], resolution_status: 'resolved', resolution_proof: [{ metric_id: 'application_crash_history', value: '0 found', status: 'normal', window_kind: 'since_diagnosis' }, memory()] };
  assert.match(getShortCheckSummary(crash), /does not confirm a repair/);
});
