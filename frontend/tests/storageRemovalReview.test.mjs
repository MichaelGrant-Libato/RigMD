import test from 'node:test';
import assert from 'node:assert/strict';
import { filterStorageReviewApps, selectedStorageReviewApp, storageReviewAppKey } from '../src/lib/storageRemovalReview.ts';

const app = { name: 'Example App', publisher: 'Example', version: '1.0', scope: 'Machine registration', estimatedBytes: 1024 };

test('removal review keeps the chosen registration when display order or filtering changes', () => {
  const other = { ...app, name: 'Another App' };
  const key = storageReviewAppKey(app);
  assert.equal(selectedStorageReviewApp([other, app], key), app);
  assert.equal(selectedStorageReviewApp([app], key), app);
  assert.equal(selectedStorageReviewApp([other], key), null);
});

test('removal review cannot silently substitute a different publisher, version, account or duplicate registration', () => {
  const key = storageReviewAppKey(app);
  for (const replacement of [{ ...app, publisher: 'Other publisher' }, { ...app, version: '2.0' }, { ...app, scope: 'Other account' }]) {
    assert.equal(selectedStorageReviewApp([replacement], key), null);
  }
  assert.equal(selectedStorageReviewApp([app, { ...app }], key), null);
  assert.equal(selectedStorageReviewApp([app], ''), null);
});

test('app names remain literal selection data and unknown size is never interpreted as removal safety', () => {
  const hostileName = { ...app, name: '<script> & "quoted"; $(command)', estimatedBytes: null };
  const before = JSON.stringify(hostileName);
  assert.equal(selectedStorageReviewApp([hostileName], storageReviewAppKey(hostileName)), hostileName);
  assert.equal(JSON.stringify(hostileName), before);
  assert.equal(hostileName.estimatedBytes, null);
});

test('a filter cannot leave an unavailable or hidden app selected for uninstall', () => {
  const unavailable = { ...app, id: 'unavailable', name: 'An unavailable desktop app', canUninstall: false };
  const supported = { ...app, id: 'supported', name: 'Example supported app', canUninstall: true };
  const filtered = filterStorageReviewApps([unavailable, supported], '', true);
  assert.deepEqual(filtered, [supported]);
  assert.equal(selectedStorageReviewApp(filtered, unavailable.id), null);
  assert.equal(selectedStorageReviewApp(filterStorageReviewApps([supported], 'other app', false), supported.id), null);
  assert.deepEqual(filterStorageReviewApps([supported], '  EXAMPLE SUPPORTED  ', false), [supported]);
});
