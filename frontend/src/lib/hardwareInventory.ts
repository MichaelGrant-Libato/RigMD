function positiveCount(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
}

export function memorySlotSummary(used?: number | null, total?: number | null): string {
  const installed = positiveCount(used);
  const availablePositions = positiveCount(total);
  if (installed !== null && availablePositions !== null)
    return installed <= availablePositions ? `${installed} of ${availablePositions} reported` : 'Unavailable';
  if (installed !== null) return `${installed} reported; total unavailable`;
  if (availablePositions !== null) return `${availablePositions} total; used count unavailable`;
  return 'Unavailable';
}

export function desktopDisplayCount(count?: number | null): string {
  const reported = positiveCount(count);
  return reported === null ? 'Unavailable' : String(reported);
}
