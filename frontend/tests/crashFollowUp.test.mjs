import test from 'node:test';
import assert from 'node:assert/strict';
import { getFindingChanges } from '../src/lib/findingChanges.ts';
import { getTelemetryDeltaSummary, getResolutionCategory } from '../src/lib/pastChecksFormatting.ts';
import { reviewOnlyNotice } from '../src/lib/repairSupport.ts';
import { readingStatusLabel, getReadingPresentation } from '../src/lib/diagnosticLanguage.ts';

test('a zero event count is a scoped observation and power context is not a health verdict', () => {
  assert.equal(readingStatusLabel({ metric_id: 'application_crash_history', status: 'normal', value: '0 found' }), 'No matching records in this period');
  const context = { label: 'OS & Power State', status: 'normal', value: 'Battery 78% (AC Power)', evidence_kind: 'context' };
  assert.equal(getReadingPresentation(context).status, 'info');
  assert.equal(readingStatusLabel(context), 'Information only');
  assert.equal(readingStatusLabel({ metric_id: 'application_crash_history', status: 'unknown', value: 'Unavailable' }), 'Could not be checked');
});

test('seven-day crash history is never cleared by a short no-new-records window', () => {
  const before = [{ metric_id: 'application_crash_history', label: 'Application crash records (last 7 days)', status: 'high', value: '2 found' }];
  const after = [{ metric_id: 'application_crash_history', label: 'Application crash records since diagnosis', status: 'normal', value: '0 found', window_kind: 'since_diagnosis' }];
  const changes = getFindingChanges(before, after);
  assert.deepEqual(changes.cleared, []);
  assert.match(changes.summary, /No new matching crash records/);
  assert.match(changes.summary, /does not prove a repair/);
  assert.doesNotMatch(changes.summary, /no longer detected/);
  assert.equal(getTelemetryDeltaSummary(before, after), null);
  assert.equal(getResolutionCategory('resolved', null, 'Application crash history requires review').label, 'No New Events');
});

test('legacy or incomplete event windows cannot imply improvement', () => {
  const original = [{ metric_id: 'system_crash_history', label: 'System crash records', status: 'high', value: '2 found' }];
  for (const latest of [[], [{ ...original[0], value: '0 found', status: 'normal' }], [{ ...original[0], label: 'System crash records since diagnosis', value: 'Unavailable', status: 'unknown' }]]) {
    const result = getFindingChanges(original, latest);
    if (result) { assert.deepEqual(result.cleared, []); assert.doesNotMatch(result.summary, /no longer detected/); }
  }
});

test('crash records and missing evidence do not justify a generic repair', () => {
  for (const category of ['Application crash history requires review', 'System crash or stop error requires review', 'Scanner evidence unavailable', 'No Active Issue Detected'])
    assert.ok(reviewOnlyNotice(category));
  assert.equal(reviewOnlyNotice('High Memory Pressure'), null);
  assert.equal(reviewOnlyNotice('Elevated Storage Utilization'), null);
});
