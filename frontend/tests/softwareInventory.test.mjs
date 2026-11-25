import test from 'node:test';
import assert from 'node:assert/strict';
import { sortInstalledApps } from '../src/lib/softwareInventory.ts';

test('size sorting puts missing measurements last and keeps equal sizes predictable', () => {
  const entries = [
    { name: 'Unknown' },
    { name: 'Small 10', estimatedSizeBytes: 1024 },
    { name: 'Invalid', estimatedSizeBytes: -42 },
    { name: 'Large', estimatedSizeBytes: 1024 ** 3 },
    { name: 'Small 2', estimatedSizeBytes: 1024 },
    { name: 'Unavailable', estimatedSizeBytes: null },
  ];
  assert.deepEqual(sortInstalledApps(entries, 'size').map(e => e.name),
    ['Large', 'Small 2', 'Small 10', 'Invalid', 'Unavailable', 'Unknown']);
  assert.deepEqual(sortInstalledApps(entries, 'name').map(e => e.name),
    ['Invalid', 'Large', 'Small 2', 'Small 10', 'Unavailable', 'Unknown']);
  assert.equal(entries[0].name, 'Unknown');
});
