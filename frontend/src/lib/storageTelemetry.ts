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
