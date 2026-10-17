import assert from 'node:assert/strict';
import test from 'node:test';
import { getLocalApiToken, localApiAuthHeaders } from '../src/lib/localApiSession.ts';

test('ordinary browser has no desktop credential', () => {
  assert.equal(getLocalApiToken(), '');
  assert.deepEqual(localApiAuthHeaders(), {});
});

test('desktop credential is read from native injection, not persisted storage', () => {
  const previous = globalThis.window;
  try {
    globalThis.window = { __rigmdLocalApiToken: 'temporary-test-credential' };
    assert.equal(getLocalApiToken(), 'temporary-test-credential');
    assert.deepEqual(localApiAuthHeaders(), { Authorization: 'Bearer temporary-test-credential' });
    globalThis.window = {};
    assert.equal(getLocalApiToken(), '');
    assert.deepEqual(localApiAuthHeaders(), {});
  } finally {
    if (previous === undefined) delete globalThis.window;
    else globalThis.window = previous;
  }
});
