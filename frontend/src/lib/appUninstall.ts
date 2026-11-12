export interface UninstallApp {
  sourceType?: 'desktop' | 'store';
  id: string; name: string; publisher: string; version: string; scope: string;
  estimatedBytes: number | null; canUninstall: boolean; uninstallReason: string;
}
export interface UninstallSnapshot {
  capturedAt: string; installerStatus: string;
  volumes: { drive: string; freeBytes: number; totalBytes: number }[];
}
export interface UninstallPreview {
  app: UninstallApp; canExecute: boolean; summary: string; before: UninstallSnapshot;
  approvalToken?: string | null; approvalExpiresAt?: string | null;
}
export interface UninstallOperation {
  id: string; sessionId: string; metricId: string; app: UninstallApp; status: string; summary: string;
  before: UninstallSnapshot; after?: UninstallSnapshot | null; exitCode?: number | null; rebootRequired: boolean;
}

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Uninstall response could not be verified.');
  return value as Record<string, unknown>;
}
export function readUninstallApp(value: unknown): UninstallApp {
  const app = object(value);
  if (typeof app.id !== 'string' || !/^[a-f\d]{64}$/i.test(app.id) ||
    ['name', 'publisher', 'version', 'scope', 'uninstallReason'].some(key => typeof app[key] !== 'string') ||
    typeof app.canUninstall !== 'boolean' || !String(app.name).trim() ||
    app.sourceType !== undefined && app.sourceType !== 'desktop' && app.sourceType !== 'store' ||
    app.estimatedBytes !== null && (!Number.isSafeInteger(app.estimatedBytes) || Number(app.estimatedBytes) <= 0))
    throw new Error('App identity could not be verified. Refresh the app list.');
  return app as unknown as UninstallApp;
}
function readSnapshot(value: unknown): UninstallSnapshot {
  const snapshot = object(value);
  if (typeof snapshot.capturedAt !== 'string' || !Number.isFinite(Date.parse(snapshot.capturedAt)) ||
    !['installed', 'absent', 'unknown', 'unsupported'].includes(String(snapshot.installerStatus)) || !Array.isArray(snapshot.volumes) || snapshot.volumes.length > 32)
    throw new Error('Uninstall readings could not be verified.');
  for (const item of snapshot.volumes) {
    const volume = object(item);
    if (typeof volume.drive !== 'string' || !Number.isSafeInteger(volume.freeBytes) || !Number.isSafeInteger(volume.totalBytes) ||
      Number(volume.freeBytes) < 0 || Number(volume.totalBytes) <= 0 || Number(volume.freeBytes) > Number(volume.totalBytes))
      throw new Error('Storage readings could not be verified.');
  }
  return snapshot as unknown as UninstallSnapshot;
}
export function readUninstallPreview(value: unknown, appId: string): UninstallPreview {
  const preview = object(value);
  const app = readUninstallApp(preview.app);
  readSnapshot(preview.before);
  if (app.id !== appId || typeof preview.canExecute !== 'boolean' || typeof preview.summary !== 'string' ||
    preview.canExecute && (!app.canUninstall || object(preview.before).installerStatus !== 'installed' ||
      typeof preview.approvalToken !== 'string' || !/^[a-f\d]{64}$/i.test(preview.approvalToken) ||
      typeof preview.approvalExpiresAt !== 'string' || !Number.isFinite(Date.parse(preview.approvalExpiresAt))))
    throw new Error('Uninstall approval could not be verified. Request a new preview.');
  return preview as unknown as UninstallPreview;
}
export function readUninstallOperation(value: unknown, sessionId: string, metricId: string, appId?: string): UninstallOperation {
  const operation = object(value);
  const app = readUninstallApp(operation.app);
  readSnapshot(operation.before);
  if (operation.after != null) readSnapshot(operation.after);
  if (typeof operation.id !== 'string' || !/^[a-f\d]{32}$/i.test(operation.id) || operation.sessionId !== sessionId || operation.metricId !== metricId || appId !== undefined && app.id !== appId ||
    !['running', 'removed', 'reboot_required', 'canceled', 'failed', 'unverified'].includes(String(operation.status)) ||
    typeof operation.summary !== 'string' || typeof operation.rebootRequired !== 'boolean' ||
    operation.exitCode != null && !Number.isSafeInteger(operation.exitCode) ||
    ['removed', 'reboot_required'].includes(String(operation.status)) &&
      (operation.after == null || object(operation.after).installerStatus !== 'absent' || ![0, 3010, 1641].includes(Number(operation.exitCode))) ||
    operation.status === 'removed' && (operation.exitCode !== 0 || operation.rebootRequired) ||
    operation.status === 'reboot_required' && (![3010, 1641].includes(Number(operation.exitCode)) || !operation.rebootRequired))
    throw new Error('The uninstall result could not be verified. Do not repeat the uninstall without checking its status.');
  return operation as unknown as UninstallOperation;
}
