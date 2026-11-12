import test from 'node:test';
import assert from 'node:assert/strict';
import { getApiErrorMessage } from '../src/lib/apiErrorMessage.ts';

test('API errors retain readable detail and use a safe fallback for unknown payloads', () => {
  assert.equal(getApiErrorMessage({ response: { data: { detail: 'Try again later.' } } }, 'Unavailable'), 'Try again later.');
  assert.equal(getApiErrorMessage({ response: { data: { detail: [{ msg: 'Choose a check.' }, null, { message: 'Try again.' }] } } }, 'Unavailable'), 'Choose a check. Try again.');
  for (const value of [null, undefined, 7, { response: { data: { detail: { other: 'value' } } } }])
    assert.equal(getApiErrorMessage(value, 'Unavailable'), 'Unavailable');
  const circular = {}; circular.message = circular;
  assert.equal(getApiErrorMessage(circular, 'Unavailable'), 'Unavailable');
});
test('stack traces do not leak into the user-facing diagnostic error', () => {
  assert.equal(getApiErrorMessage(new Error('System.IO.FileNotFoundException'), 'Could not finish'), 'Could not finish');
  assert.equal(getApiErrorMessage(new Error('Connection unavailable'), 'Could not finish'), 'Connection unavailable');
});
