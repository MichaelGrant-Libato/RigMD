import test from 'node:test';
import assert from 'node:assert/strict';
import { readUninstallApp, readUninstallOperation, readUninstallPreview } from '../src/lib/appUninstall.ts';

const app = { id: 'A'.repeat(64), name: 'Example Editor', publisher: 'Example Company', version: '1.0', scope: 'Machine registration', estimatedBytes: 1024, canUninstall: true, uninstallReason: 'Preview required' };
const before = { capturedAt: '2026-11-08T04:00:00Z', installerStatus: 'installed', volumes: [{ drive: 'C:', freeBytes: 10, totalBytes: 100 }] };
const preview = { app, before, canExecute: true, summary: 'Preview', approvalToken: 'B'.repeat(64), approvalExpiresAt: '2026-11-08T04:05:00Z' };
const operation = { id: 'c'.repeat(32), sessionId: 'saved-session', metricId: 'storage_usage_C:', app, status: 'removed', summary: 'Registration absent', before, after: { ...before, installerStatus: 'absent' }, exitCode: 0, rebootRequired: false };

test('inventory source is verified and Store metadata does not invent removal permission', () => {
  const store = { ...app, sourceType: 'store', canUninstall: false, estimatedBytes: null };
  assert.equal(readUninstallApp(store).canUninstall, false);
  assert.equal(readUninstallApp(store).estimatedBytes, null);
  assert.throws(() => readUninstallApp({ ...app, sourceType: 'unknown-source' }));
});

test('uninstall previews must match the selected app and contain a real approval and verified before-state', () => {
  assert.equal(readUninstallPreview(preview, app.id), preview);
  for (const value of [{ ...preview, approvalToken: null }, { ...preview, approvalExpiresAt: 'unknown' },
    { ...preview, app: { ...app, id: 'D'.repeat(64) } }, { ...preview, before: { ...before, installerStatus: 'unknown' } }]) {
    assert.throws(() => readUninstallPreview(value, app.id));
  }
});

test('installer exit success cannot hide missing or contradictory post-execution evidence', () => {
  assert.equal(readUninstallOperation(operation, operation.sessionId, operation.metricId), operation);
  for (const after of [null, { ...before, installerStatus: 'installed' }, { ...before, installerStatus: 'unknown' }]) {
    assert.throws(() => readUninstallOperation({ ...operation, after }, operation.sessionId, operation.metricId));
  }
  assert.throws(() => readUninstallOperation({ ...operation, exitCode: 1603 }, operation.sessionId, operation.metricId));
  assert.throws(() => readUninstallOperation(operation, 'another-session', operation.metricId));
  assert.throws(() => readUninstallOperation(operation, operation.sessionId, 'storage_usage_D:'));
  assert.throws(() => readUninstallOperation(operation, operation.sessionId, operation.metricId, 'D'.repeat(64)));
  assert.throws(() => readUninstallOperation({ ...operation, exitCode: 3010 }, operation.sessionId, operation.metricId));
});

test('missing storage readings remain missing and malformed capacities cannot become recovered space', () => {
  const missing = { ...operation, after: { ...operation.after, volumes: [] } };
  assert.deepEqual(readUninstallOperation(missing, operation.sessionId, operation.metricId).after.volumes, []);
  for (const volumes of [[{ drive: 'C:', freeBytes: -1, totalBytes: 100 }], [{ drive: 'C:', freeBytes: 101, totalBytes: 100 }]]) {
    assert.throws(() => readUninstallOperation({ ...operation, after: { ...operation.after, volumes } }, operation.sessionId, operation.metricId));
  }
});
