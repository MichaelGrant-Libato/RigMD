import test from 'node:test';
import assert from 'node:assert/strict';
import { findDiskReading, validReading, formatDiskRate } from '../src/lib/storageTelemetry.ts';

test('matches physical disk IDs rather than array positions or prefixes', () => {
  const readings = [{ DeviceId: '10 D:', value: 90 }, { DeviceId: '1 C:', value: 12 }];
  assert.equal(findDiskReading(readings, 1)?.value, 12);
  assert.equal(findDiskReading(readings, 10)?.value, 90);
  assert.equal(findDiskReading(readings, 0), undefined);
  assert.equal(findDiskReading(readings, undefined), undefined);
});

test('missing and invalid readings stay unavailable; measured zero remains valid', () => {
  for (const value of [undefined, null, NaN, Infinity, -1, '0']) assert.equal(validReading(value), null);
  assert.equal(validReading(0), 0);
  assert.equal(validReading(12.5), 12.5);
  assert.equal(formatDiskRate(null), 'Not available');
  assert.equal(formatDiskRate(0), '0.0 KB/s');
  assert.equal(formatDiskRate(2048), '2.0 MB/s');
});
