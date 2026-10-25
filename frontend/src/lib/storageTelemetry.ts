import { REVIEW_THRESHOLDS, percentReviewStatus } from './reviewThresholds.ts';

export function storageReviewStatus(stats?: {
  all_disks?: Array<{ usage_percent?: number | null }>;
  storage_drives?: Array<{ usage_percent?: number | null }>;
  disk?: { total_gb?: number; usage_percent?: number | null };
} | null): 'unknown' | 'high' | 'normal' {
  const rows = stats?.all_disks?.length ? stats.all_disks : stats?.storage_drives?.length ? stats.storage_drives
    : stats?.disk?.total_gb && stats.disk.total_gb > 0 ? [stats.disk] : [];
  const statuses = rows.map(row => percentReviewStatus(row.usage_percent, REVIEW_THRESHOLDS.storage));
  return statuses.includes('high') ? 'high' : !statuses.length || statuses.includes('unknown') ? 'unknown' : 'normal';
}

/** Match physical disk numbers exactly; disk 1 must never match disk 10. */
export function findDiskReading<T extends { DeviceId: string }>(readings: T[], diskIndex: number | null | undefined): T | undefined {
  if (diskIndex == null || !Number.isInteger(diskIndex)) return undefined;
  return readings.find((reading) => {
    const match = /^(\d+)(?:\s|$)/.exec(String(reading.DeviceId).trim());
    return match !== null && Number(match[1]) === diskIndex;
  });
}

export function validReading(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null;
}

export function formatDiskRate(value: number | null): string {
  if (value === null) return 'Not available';
  return value >= 1024 ? `${(value / 1024).toFixed(1)} MB/s` : `${value.toFixed(1)} KB/s`;
}

interface StorageCapacity {
  used_gb?: number | null;
  size_gb?: number | null;
  usage_percent?: number | null;
}

export function storageUsagePercent(drive: StorageCapacity): number | null {
  const supplied = validReading(drive.usage_percent);
  if (supplied !== null && supplied <= 100) return supplied;
  const total = validReading(drive.size_gb);
  const used = validReading(drive.used_gb);
  return total !== null && total > 0 && used !== null && used <= total
    ? used / total * 100 : null;
}

export function formatStorageCapacity(usedValue: unknown, totalValue: unknown): string {
  const total = validReading(totalValue);
  const used = validReading(usedValue);
  if (total === null || total === 0) return 'Not available';
  return used !== null && used <= total
    ? `${used.toFixed(1)} / ${total.toFixed(1)} GB used`
    : `${total.toFixed(1)} GB total`;
}

export function storageCapacitySummary(
  drives: StorageCapacity[],
  fallback?: { used_gb?: number; total_gb?: number } | null,
): string {
  if (drives.length === 0) return formatStorageCapacity(fallback?.used_gb, fallback?.total_gb);
  const totals = drives.map(drive => validReading(drive.size_gb));
  if (totals.some(total => total === null || total === 0)) return 'Not available';
  const total = totals.reduce<number>((sum, value) => sum + (value ?? 0), 0);
  const usedValues = drives.map(drive => validReading(drive.used_gb));
  const hasCompleteUsage = usedValues.every((used, index) => used !== null && used <= totals[index]!);
  const used = hasCompleteUsage ? usedValues.reduce<number>((sum, value) => sum + (value ?? 0), 0) : null;
  return formatStorageCapacity(used, total);
}
