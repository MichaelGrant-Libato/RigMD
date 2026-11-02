import test from 'node:test';
import assert from 'node:assert/strict';
import { defaultDisplayPreferences, displayPreferencesKey, normalizeDisplayPreferences, readDisplayPreferences, saveDisplayPreferences, shouldReduceMotion } from '../src/lib/displayPreferences.ts';

function memoryStorage() {
  const values = new Map();
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
}

test('a first installation uses the existing text size and Windows motion preference', () => {
  assert.deepEqual(readDisplayPreferences(memoryStorage()), { readingSize: 'standard', motion: 'system' });
});

test('reading and movement choices survive reopening without storing account or diagnostic data', () => {
  const storage = memoryStorage();
  const chosen = { readingSize: 'largest', motion: 'reduced', account: 'must-not-be-stored', result: 'must-not-be-stored' };
  assert.equal(saveDisplayPreferences(storage, chosen), true);
  assert.deepEqual(readDisplayPreferences(storage), { readingSize: 'largest', motion: 'reduced' });
  assert.equal(storage.getItem(displayPreferencesKey), '{"readingSize":"largest","motion":"reduced"}');
});

test('corrupt or unrecognized saved values cannot inject styles or disable system preferences', () => {
  const storage = memoryStorage();
  for (const value of ['invalid JSON', 'null', '[]', '42', '"large"', '{"readingSize":"500%","motion":"never"}']) {
    storage.setItem(displayPreferencesKey, value);
    assert.deepEqual(readDisplayPreferences(storage), defaultDisplayPreferences);
  }
  assert.deepEqual(normalizeDisplayPreferences({ readingSize: 'larger', motion: 'invalid' }), { readingSize: 'larger', motion: 'system' });
});

test('storage denied or full leaves the app usable and reports that persistence failed', () => {
  const denied = { getItem() { throw new Error('Denied'); }, setItem() { throw new Error('Full'); } };
  assert.deepEqual(readDisplayPreferences(denied), defaultDisplayPreferences);
  assert.deepEqual(readDisplayPreferences(null), defaultDisplayPreferences);
  assert.equal(saveDisplayPreferences(denied, defaultDisplayPreferences), false);
  assert.equal(saveDisplayPreferences(null, defaultDisplayPreferences), false);
});

test('Windows reduced motion is respected with either app setting', () => {
  for (const readingSize of ['standard', 'larger', 'largest']) {
    assert.equal(shouldReduceMotion({ readingSize, motion: 'system' }, true), true);
    assert.equal(shouldReduceMotion({ readingSize, motion: 'system' }, false), false);
    assert.equal(shouldReduceMotion({ readingSize, motion: 'reduced' }, false), true);
    assert.equal(shouldReduceMotion({ readingSize, motion: 'reduced' }, true), true);
  }
});

test('restoring defaults persists only display preferences and preserves other local data', () => {
  const storage = memoryStorage();
  storage.setItem('unrelated-setting', 'preserve');
  saveDisplayPreferences(storage, { readingSize: 'largest', motion: 'reduced' });
  saveDisplayPreferences(storage, defaultDisplayPreferences);
  assert.deepEqual(readDisplayPreferences(storage), defaultDisplayPreferences);
  assert.equal(storage.getItem('unrelated-setting'), 'preserve');
});
