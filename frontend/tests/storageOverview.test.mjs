import test from 'node:test';
import assert from 'node:assert/strict';
import { readStorageOverview, storageCategoryIds, storageSize, categorySize } from '../src/lib/storageOverview.ts';

const overview = () => ({
  capturedAt: '2026-11-08T10:00:00Z',
  volume: { drive: 'D:\\', totalBytes: 1000, freeBytes: 200 },
  categories: storageCategoryIds.map(id => ({ id, name: id, description: '', state: id === 'installed_apps' ? 'review' : 'unavailable', measurement: null })),
});

test('the selected drive must match the backend response; bad capacities cannot become a storage bar', () => {
  assert.equal(readStorageOverview(overview(), 'storage_usage_D:').volume.freeBytes, 200);
  assert.throws(() => readStorageOverview(overview(), 'storage_usage_C:'));
  for (const freeBytes of [-1, 1001, NaN, undefined]) {
    const value = overview(); value.volume.freeBytes = freeBytes;
    assert.throws(() => readStorageOverview(value, 'storage_usage_D:'));
  }
  assert.throws(() => readStorageOverview(overview(), 'D:\\Windows'));
  assert.throws(() => readStorageOverview(overview(), 'storage_usage_D:\n'));
});

test('unknown and incomplete category sizes never turn into complete zero measurements', () => {
  const value = overview();
  assert.match(categorySize(value.categories[1]), /unavailable/);
  value.categories[1] = { ...value.categories[1], state: 'partial', measurement: { fileBytes: 0, partial: true, filesCounted: 0, largestFiles: [] } };
  const reading = readStorageOverview(value, 'storage_usage_D:');
  assert.match(categorySize(reading.categories[1]), /\+ found/);
  value.categories[1].state = 'measured';
  assert.throws(() => readStorageOverview(value, 'storage_usage_D:'));
});

test('category identities and bounded file metadata must be verifiable', () => {
  const value = overview();
  value.categories[1] = { ...value.categories[1], state: 'measured', measurement: { fileBytes: 100, partial: false, filesCounted: 1, largestFiles: [{ name: 'example.txt', bytes: 100 }] } };
  assert.equal(readStorageOverview(value, 'storage_usage_D:').categories[1].measurement.fileBytes, 100);
  value.categories[1].measurement.largestFiles[0].bytes = 101;
  assert.throws(() => readStorageOverview(value, 'storage_usage_D:'));
  value.categories[1].measurement.largestFiles[0].bytes = 100;
  value.categories[2].id = value.categories[0].id;
  assert.throws(() => readStorageOverview(value, 'storage_usage_D:'));
});

test('app estimates never become a drive total and small files retain useful units', () => {
  assert.equal(categorySize(overview().categories[0]), 'Review apps');
  assert.equal(storageSize(0), '0 B');
  assert.equal(storageSize(40), '40 B');
  assert.equal(storageSize(1024), '1.0 KB');
  assert.equal(storageSize(1024 ** 3), '1.0 GB');
});
