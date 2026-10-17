import test from 'node:test';
import assert from 'node:assert/strict';
import { validTemperature, selectTemperature, formatTemperature } from '../src/lib/temperatureTelemetry.ts';

test('missing and invalid temperatures remain unavailable rather than becoming zero', () => {
  for (const value of [null, undefined, NaN, Infinity, -Infinity, -1, 151, '45', false]) {
    assert.equal(validTemperature(value), null);
    assert.equal(formatTemperature(value), 'Not available');
  }
  assert.equal(validTemperature(0), 0);
  assert.equal(formatTemperature(0), '0\u00b0C');
  assert.equal(formatTemperature(150), '150\u00b0C');
});

test('a snapshot is displayed only before the first live temperature sample', () => {
  assert.equal(selectTemperature(52, undefined, false), 52);
  assert.equal(formatTemperature(52, 61.5, true), '62\u00b0C');
  assert.equal(selectTemperature(52, 0, true), 0);
});

test('a new unavailable or invalid live reading clears a previously valid snapshot', () => {
  for (const live of [null, undefined, NaN, Infinity, -1, 151]) {
    assert.equal(selectTemperature(52, live, true), null);
    assert.equal(formatTemperature(52, live, true), 'Not available');
  }
});
