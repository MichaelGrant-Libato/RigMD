import test from 'node:test';
import assert from 'node:assert/strict';
import { readCleanupFiles, readCleanupPreview, readCleanupOperation } from '../src/lib/storageCleanup.ts';

const metric = 'storage_usage_C:';
const file = { id: 'a'.repeat(32), name: 'notes.pdf', bytes: 100 };
function preview() { return { capturedAt: new Date().toISOString(), kind: 'recycle_files', metricId: metric, drive: 'C:\\', files: [file], bytes: 100,
  bin: { bytes: 200, items: 2 }, freeBytes: 1000, canExecute: true, summary: 'Move selected files to the Recycle Bin; this does not free space yet.', approvalToken: 'b'.repeat(64), approvalExpiresAt: new Date(Date.now() + 300000).toISOString() }; }
function operation() { return { id: 'c'.repeat(32), sessionId: 'session', metricId: metric, status: 'recycled', summary: 'Windows confirmed recycling.', before: preview(), afterFreeBytes: 1000, afterBin: { bytes: 300, items: 3 }, exitCode: 0 }; }

test('file selection requires real backend identities and the selected drive', () => {
  const value = { capturedAt: new Date().toISOString(), drive: 'C:\\', categoryId: 'documents', files: [file], partial: true };
  assert.equal(readCleanupFiles(value, metric, 'documents').files.length, 1);
  assert.throws(() => readCleanupFiles(value, 'storage_usage_D:', 'documents'));
  assert.throws(() => readCleanupFiles({ ...value, files: [file, file] }, metric, 'documents'));
  assert.throws(() => readCleanupFiles({ ...value, files: [{ ...file, id: 'C:\\Windows\\file' }] }, metric, 'documents'));
});
test('preview binds precisely the files selected and requires a valid approval', () => {
  const value = preview();
  assert.equal(readCleanupPreview(value, metric, 'recycle_files', [file.id]).bytes, 100);
  for (const changed of [{ files: [] }, { bytes: 101 }, { approvalToken: null }, { metricId: 'storage_usage_D:' }, { kind: 'empty_recycle_bin' }])
    assert.throws(() => readCleanupPreview({ ...value, ...changed }, metric, 'recycle_files', [file.id]));
});
test('moving files to the bin can succeed without any free-space increase', () => {
  const value = operation();
  assert.equal(readCleanupOperation(value, 'session', metric).status, 'recycled');
  assert.equal(value.before.freeBytes, value.afterFreeBytes);
});
test('emptying the bin is a different action and needs a confirmed empty bin after execution', () => {
  const value = operation(); value.before = { ...preview(), kind: 'empty_recycle_bin', files: [], bytes: 200 }; value.status = 'emptied'; value.afterBin = { bytes: 0, items: 0 };
  assert.equal(readCleanupOperation(value, 'session', metric).status, 'emptied');
  assert.throws(() => readCleanupOperation({ ...value, afterBin: { bytes: 100, items: 1 } }, 'session', metric));
  assert.throws(() => readCleanupOperation({ ...value, exitCode: 13 }, 'session', metric));
  assert.throws(() => readCleanupOperation({ ...value, afterFreeBytes: null }, 'session', metric));
});
test('unknown outcomes and mismatched sessions can never be displayed as confirmed cleanup', () => {
  assert.throws(() => readCleanupOperation(operation(), 'another-session', metric));
  assert.throws(() => readCleanupOperation({ ...operation(), status: 'deleted' }, 'session', metric));
  assert.throws(() => readCleanupOperation({ ...operation(), afterBin: null }, 'session', metric));
});
