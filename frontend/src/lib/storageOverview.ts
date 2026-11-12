export const storageCategoryIds = ['installed_apps', 'temporary_files', 'documents', 'pictures', 'desktop', 'music', 'videos', 'system_other', 'cloud'] as const;
export type StorageCategoryId = typeof storageCategoryIds[number];
export interface StorageCategory {
  id: StorageCategoryId;
  name: string;
  description: string;
  state: 'review' | 'measured' | 'partial' | 'unavailable';
  measurement: null | { fileBytes: number | null; partial: boolean; filesCounted: number; largestFiles: { name: string; bytes: number }[] };
}
export interface StorageOverview {
  capturedAt: string;
  volume: { drive: string; freeBytes: number; totalBytes: number };
  categories: StorageCategory[];
}
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Storage readings could not be verified. Try updating the review.');
  return value as Record<string, unknown>;
}
function nonnegative(value: unknown): value is number { return Number.isSafeInteger(value) && Number(value) >= 0; }
export function readStorageOverview(value: unknown, metricId: string): StorageOverview {
  const data = object(value);
  const volume = object(data.volume);
  const match = /^storage_usage_([a-z]:)\\?$/i.exec(metricId);
  const expectedDrive = match?.[0] === metricId ? match[1] : undefined;
  if (typeof data.capturedAt !== 'string' || !Number.isFinite(Date.parse(data.capturedAt)) ||
    typeof volume.drive !== 'string' || volume.drive.length !== 3 || !/^[a-z]:\\$/i.test(volume.drive) ||
    expectedDrive && volume.drive.slice(0, 2).toLowerCase() !== expectedDrive.toLowerCase() ||
    !expectedDrive && metricId !== 'storage_usage' ||
    !nonnegative(volume.totalBytes) || volume.totalBytes === 0 || !nonnegative(volume.freeBytes) || volume.freeBytes > volume.totalBytes ||
    !Array.isArray(data.categories) || data.categories.length !== storageCategoryIds.length)
    throw new Error('This drive’s storage readings could not be verified.');
  const ids = new Set<string>();
  for (const raw of data.categories) {
    const category = object(raw);
    if (!storageCategoryIds.includes(category.id as StorageCategoryId) || ids.has(String(category.id)) ||
      typeof category.name !== 'string' || !category.name.trim() || typeof category.description !== 'string' ||
      !['review', 'measured', 'partial', 'unavailable'].includes(String(category.state)))
      throw new Error('Storage categories could not be verified.');
    ids.add(String(category.id));
    if (category.measurement === null) {
      if (category.state === 'measured' || category.state === 'partial') throw new Error('A folder measurement is missing.');
      continue;
    }
    const measurement = object(category.measurement);
    if (typeof measurement.partial !== 'boolean' || !nonnegative(measurement.filesCounted) ||
      measurement.fileBytes !== null && !nonnegative(measurement.fileBytes) ||
      !Array.isArray(measurement.largestFiles) || measurement.largestFiles.length > 5 || measurement.largestFiles.length > measurement.filesCounted ||
      measurement.largestFiles.some(raw => { const file = object(raw); return typeof file.name !== 'string' || !file.name.trim() || !nonnegative(file.bytes) || Number(file.bytes) > Number(measurement.fileBytes); }) ||
      category.state !== (measurement.fileBytes === null ? 'unavailable' : measurement.partial ? 'partial' : 'measured'))
      throw new Error('A folder measurement could not be verified.');
  }
  return data as unknown as StorageOverview;
}
export function storageSize(bytes: number) {
  if (bytes === 0) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  const power = Math.min(4, Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${(bytes / 1024 ** power).toFixed(1)} ${['B', 'KB', 'MB', 'GB', 'TB'][power]}`;
}
export function categorySize(category: StorageCategory) {
  if (category.id === 'installed_apps') return 'Review apps';
  const measured = category.measurement;
  if (!measured || measured.fileBytes === null) return 'Size unavailable';
  return `${storageSize(measured.fileBytes)}${measured.partial ? '+ found' : ' in files'}`;
}
