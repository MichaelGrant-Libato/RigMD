import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveFindingGroup } from '../src/lib/findingGroupSelection.ts';

const groups = (attention, unknown, other) => [
  { id: 'attention', count: attention }, { id: 'unknown', count: unknown }, { id: 'other', count: other },
];
test('a cleared warning shows available normal readings after recheck', () => {
  assert.equal(resolveFindingGroup(groups(0, 0, 3), { id: 'attention', revision: 'before' }, 'after'), 'other');
});
test('unavailable follow-up evidence is shown when warnings disappear', () => {
  assert.equal(resolveFindingGroup(groups(0, 1, 2), { id: 'attention', revision: 'before' }, 'after'), 'unknown');
});
test('an intentional empty-tab selection remains available until the evidence changes', () => {
  assert.equal(resolveFindingGroup(groups(0, 0, 3), { id: 'attention', revision: 'current' }, 'current'), 'attention');
});
test('a still-populated tab remains selected when other warnings change', () => {
  assert.equal(resolveFindingGroup(groups(1, 0, 2), { id: 'other', revision: 'before' }, 'after'), 'other');
});
test('empty or removed groups have a stable fallback without inventing a reading', () => {
  assert.equal(resolveFindingGroup(groups(0, 0, 0), { id: 'missing', revision: 'before' }, 'after'), 'attention');
  assert.equal(resolveFindingGroup([], { id: 'missing', revision: 'before' }, 'after'), 'attention');
});
