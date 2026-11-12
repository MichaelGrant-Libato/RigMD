import { apiGet, apiPost } from '../lib/api';
import { readCleanupFiles, readCleanupOperation, readCleanupPreview, type CleanupKind } from '../lib/storageCleanup';

async function post(path: string, body: unknown) {
  try { return await apiPost<unknown>(path, body, { timeout: 20000 }); }
  catch (error) {
    const response = (error as { response?: { status?: number; data?: { remediation_guard?: unknown } } }).response;
    if (response?.status === 409 && response.data?.remediation_guard) window.dispatchEvent(new Event('rigmd-remediation-evidence-changed'));
    throw error;
  }
}

export async function getCleanupFiles(metricId: string, categoryId: string, signal: AbortSignal) {
  const { data } = await apiGet<unknown>(`/api/autonomy/storage-cleanup/files?metricId=${encodeURIComponent(metricId)}&categoryId=${encodeURIComponent(categoryId)}`, { signal, timeout: 15000 });
  return readCleanupFiles(data, metricId, categoryId);
}
export async function previewStorageCleanup(sessionId: string, metricId: string, kind: CleanupKind, fileIds: string[]) {
  const { data } = await post('/api/autonomy/storage-cleanup/preview', { sessionId, metricId, kind, fileIds });
  return readCleanupPreview(data, metricId, kind, fileIds);
}
export async function executeStorageCleanup(sessionId: string, metricId: string, approvalToken: string) {
  const { data } = await post('/api/autonomy/storage-cleanup/execute', { sessionId, metricId, approvalToken, confirmed: true });
  return readCleanupOperation(data, sessionId, metricId);
}
export async function getStorageCleanupOperation(id: string, sessionId: string, metricId: string, signal: AbortSignal) {
  const { data } = await apiGet<unknown>(`/api/autonomy/storage-cleanup/operations/${encodeURIComponent(id)}`, { signal, timeout: 12000 });
  const operation = readCleanupOperation(data, sessionId, metricId);
  if (operation.id !== id) throw new Error('The cleanup operation changed. Check its status before continuing.');
  return operation;
}
export async function getRecentStorageCleanups(sessionId: string, metricId: string, signal: AbortSignal) {
  const { data } = await apiGet<unknown>(`/api/autonomy/storage-cleanup/operations?sessionId=${encodeURIComponent(sessionId)}&metricId=${encodeURIComponent(metricId)}`, { signal, timeout: 12000 });
  if (!Array.isArray(data) || data.length > 16) throw new Error('Cleanup history could not be verified.');
  return data.map(value => readCleanupOperation(value, sessionId, metricId));
}
