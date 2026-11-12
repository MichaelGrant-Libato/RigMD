import test from 'node:test';
import assert from 'node:assert/strict';
import { memorySlotSummary, desktopDisplayCount } from '../src/lib/hardwareInventory.ts';

test('memory positions reflect complete and partial Windows reports without a four-slot fallback', () => {
  assert.equal(memorySlotSummary(1, 1), '1 of 1 reported');
  assert.equal(memorySlotSummary(2, 2), '2 of 2 reported');
  assert.equal(memorySlotSummary(2, 6), '2 of 6 reported');
  assert.equal(memorySlotSummary(2, null), '2 reported; total unavailable');
  assert.equal(memorySlotSummary(null, 2), '2 total; used count unavailable');
});

test('missing, invalid and contradictory memory counts cannot become installed or spare slots', () => {
  for (const value of [undefined, null, 0, -1, 1.5, NaN, Infinity, '2']) {
    assert.equal(memorySlotSummary(value, value), 'Unavailable');
  }
  assert.equal(memorySlotSummary(3, 2), 'Unavailable');
});

test('desktop monitor counts remain measured values instead of adapter row counts or an assumed one', () => {
  for (const count of [1, 2, 6]) assert.equal(desktopDisplayCount(count), String(count));
  for (const count of [undefined, null, 0, -1, 1.5, NaN, Infinity, '1'])
    assert.equal(desktopDisplayCount(count), 'Unavailable');
});
