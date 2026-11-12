export type CleanupKind = 'recycle_files' | 'empty_recycle_bin';
export interface CleanupFile { id: string; name: string; bytes: number }
export interface CleanupFiles { capturedAt: string; drive: string; categoryId: string; files: CleanupFile[]; partial: boolean }
export interface CleanupPreview {
  capturedAt: string; kind: CleanupKind; metricId: string; drive: string; files: CleanupFile[]; bytes: number;
  bin: { bytes: number; items: number }; freeBytes: number; canExecute: boolean; summary: string;
  approvalToken: string | null; approvalExpiresAt: string | null;
}
export interface CleanupOperation {
  id: string; sessionId: string; metricId: string; status: 'running' | 'recycled' | 'emptied' | 'partial' | 'canceled' | 'unverified';
  summary: string; before: CleanupPreview; afterFreeBytes: number | null; afterBin: { bytes: number; items: number } | null; exitCode: number | null;
}
const fail = () => new Error('The cleanup response could not be verified. Refresh its status before continuing.');
function record(value: unknown): Record<string, unknown> { if (!value || typeof value !== 'object' || Array.isArray(value)) throw fail(); return value as Record<string, unknown>; }
function count(value: unknown) { return Number.isSafeInteger(value) && Number(value) >= 0; }
function date(value: unknown) { return typeof value === 'string' && Number.isFinite(Date.parse(value)); }
function files(value: unknown): CleanupFile[] {
  if (!Array.isArray(value) || value.length > 20) throw fail();
  const seen = new Set<string>();
  for (const raw of value) {
    const item = record(raw);
    if (typeof item.id !== 'string' || !/^[a-f\d]{32}$/i.test(item.id) || seen.has(item.id) || typeof item.name !== 'string' || !item.name.trim() || !count(item.bytes)) throw fail();
    seen.add(item.id);
  }
  return value as CleanupFile[];
}
function bin(value: unknown) { const item = record(value); if (!count(item.bytes) || !count(item.items)) throw fail(); }
function drive(value: unknown, metric: string) {
  if (typeof value !== 'string' || !/^[a-z]:\\$/i.test(value) || metric !== 'storage_usage' && metric.toLowerCase().replace(/\\$/, '') !== `storage_usage_${value.slice(0, 2).toLowerCase()}`) throw fail();
}
export function readCleanupFiles(value: unknown, metric: string, category: string): CleanupFiles {
  const item = record(value); drive(item.drive, metric); files(item.files);
  if (!date(item.capturedAt) || item.categoryId !== category || typeof item.partial !== 'boolean') throw fail();
  return item as unknown as CleanupFiles;
}
function preview(value: unknown, metric: string): CleanupPreview {
  const item = record(value); drive(item.drive, metric); const list = files(item.files); bin(item.bin);
  if (!date(item.capturedAt) || item.metricId !== metric || !['recycle_files', 'empty_recycle_bin'].includes(String(item.kind)) ||
    !count(item.bytes) || !count(item.freeBytes) || typeof item.canExecute !== 'boolean' || typeof item.summary !== 'string' ||
    item.kind === 'empty_recycle_bin' && list.length !== 0 || item.kind === 'recycle_files' && (!list.length || list.reduce((sum, file) => sum + file.bytes, 0) !== item.bytes)) throw fail();
  return item as unknown as CleanupPreview;
}
export function readCleanupPreview(value: unknown, metric: string, kind: CleanupKind, ids: string[]): CleanupPreview {
  const item = preview(value, metric);
  if (item.kind !== kind || item.files.length !== ids.length || item.files.some(file => !ids.includes(file.id)) || item.canExecute &&
    (typeof item.approvalToken !== 'string' || !/^[a-f\d]{64}$/i.test(item.approvalToken) || !date(item.approvalExpiresAt))) throw fail();
  return item;
}
export function readCleanupOperation(value: unknown, session: string, metric: string): CleanupOperation {
  const item = record(value); const before = preview(item.before, metric);
  if (typeof item.id !== 'string' || !/^[a-f\d]{32}$/i.test(item.id) || item.sessionId !== session || item.metricId !== metric ||
    !['running', 'recycled', 'emptied', 'partial', 'canceled', 'unverified'].includes(String(item.status)) || typeof item.summary !== 'string' ||
    item.afterFreeBytes !== null && !count(item.afterFreeBytes) || item.exitCode !== null && !Number.isSafeInteger(item.exitCode)) throw fail();
  if (item.afterBin !== null) bin(item.afterBin);
  if (['recycled', 'emptied'].includes(String(item.status)) && (item.exitCode !== 0 || item.afterFreeBytes === null || item.afterBin === null ||
    item.status === 'emptied' && (before.kind !== 'empty_recycle_bin' || record(item.afterBin).items !== 0) || item.status === 'recycled' && before.kind !== 'recycle_files')) throw fail();
  return item as unknown as CleanupOperation;
}
