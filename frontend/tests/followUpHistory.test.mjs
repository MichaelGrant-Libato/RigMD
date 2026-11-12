import test from 'node:test';
import assert from 'node:assert/strict';
import { followUpStatusLabel, followUpTimeLabel } from '../src/lib/followUpHistory.ts';

test('normal follow-up wording does not claim a repair or overall health', () => {
  assert.equal(followUpStatusLabel('resolved'), 'Warning not detected in this recheck');
  assert.equal(followUpStatusLabel('still_active'), 'Warning still detected');
  assert.equal(followUpStatusLabel('needs_recheck'), 'Could not fully verify');
  assert.equal(followUpStatusLabel('unknown-legacy-state'), 'Outcome not recorded');
});
test('missing and invalid historical times never become the current time', () => {
  for (const value of ['', ' ', 'invalid']) assert.equal(followUpTimeLabel(value), 'Time unavailable');
  assert.equal(followUpTimeLabel('2026-11-11T00:00:00Z'), new Date('2026-11-11T00:00:00Z').toLocaleString());
});
