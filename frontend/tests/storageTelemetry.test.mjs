import test from 'node:test';
import assert from 'node:assert/strict';
import { findDiskReading, validReading, formatDiskRate, formatStorageCapacity, storageCapacitySummary, storageUsagePercent } from '../src/lib/storageTelemetry.ts';

test('matches physical disk IDs rather than array positions or prefixes', () => {
  const readings = [{ DeviceId: '10 D:', value: 90 }, { DeviceId: '1 C:', value: 12 }];
  assert.equal(findDiskReading(readings, 1)?.value, 12);
  assert.equal(findDiskReading(readings, 10)?.value, 90);
  assert.equal(findDiskReading(readings, 0), undefined);
  assert.equal(findDiskReading(readings, undefined), undefined);
});

test('storage capacity includes used and total GB for one or multiple physical drives', () => {
  assert.equal(storageCapacitySummary([{ used_gb: 343, size_gb: 477 }]), '343.0 / 477.0 GB used');
  assert.equal(storageCapacitySummary([{ used_gb: 343, size_gb: 477 }, { used_gb: 100, size_gb: 1000 }]), '443.0 / 1477.0 GB used');
  assert.equal(storageCapacitySummary([], { used_gb: 50, total_gb: 100 }), '50.0 / 100.0 GB used');
});

test('partial, invalid, and missing storage readings do not become zero usage', () => {
  assert.equal(storageCapacitySummary([{ used_gb: null, size_gb: 477 }]), '477.0 GB total');
  assert.equal(storageCapacitySummary([{ used_gb: 20, size_gb: 100 }, { size_gb: 100 }]), '200.0 GB total');
  assert.equal(storageCapacitySummary([{ used_gb: 10, size_gb: NaN }]), 'Not available');
  assert.equal(storageCapacitySummary([]), 'Not available');
  assert.equal(formatStorageCapacity(0, 477), '0.0 / 477.0 GB used');
  assert.equal(formatStorageCapacity(200, 100), '100.0 GB total');
  assert.equal(storageUsagePercent({ size_gb: 100, used_gb: 25 }), 25);
  assert.equal(storageUsagePercent({ usage_percent: NaN }), null);
  assert.equal(storageUsagePercent({ usage_percent: 101 }), null);
  assert.equal(storageUsagePercent({ usage_percent: 0 }), 0);
});

test('missing and invalid readings stay unavailable; measured zero remains valid', () => {
  for (const value of [undefined, null, NaN, Infinity, -1, '0']) assert.equal(validReading(value), null);
  assert.equal(validReading(0), 0);
  assert.equal(validReading(12.5), 12.5);
  assert.equal(formatDiskRate(null), 'Not available');
  assert.equal(formatDiskRate(0), '0.0 KB/s');
  assert.equal(formatDiskRate(2048), '2.0 MB/s');
});
