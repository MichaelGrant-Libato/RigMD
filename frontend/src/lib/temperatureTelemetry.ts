export function validTemperature(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 150
    ? value
    : null;
}

export function selectTemperature(snapshot: unknown, live: unknown, hasLiveSample: boolean): number | null {
  // An unavailable live sample supersedes an older snapshot; it is not a fallback request.
  return validTemperature(hasLiveSample ? live : snapshot);
}

export function formatTemperature(snapshot: unknown, live?: unknown, hasLiveSample = false): string {
  const value = selectTemperature(snapshot, live, hasLiveSample);
  return value === null ? 'Not available' : `${Math.round(value)}\u00b0C`;
}
