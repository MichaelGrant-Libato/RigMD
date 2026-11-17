import type { GpuAdapterReading, HardwareStats } from '../types/rigmd';

export interface GpuTemperatureReading {
  celsius: number;
  source: string;
  capturedAtUtc: string;
}

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

export function gpuTemperatureReading(adapter: GpuAdapterReading, now = Date.now()): GpuTemperatureReading | null {
  const value = validTemperature(adapter.temperatureCelsius);
  const captured = adapter.temperatureCapturedAtUtc ? Date.parse(adapter.temperatureCapturedAtUtc) : NaN;
  if (value === null || !Number.isFinite(captured) || now - captured > 5000 || captured - now > 2000) return null;
  return { celsius: value, source: adapter.temperatureSource || 'Source not recorded', capturedAtUtc: adapter.temperatureCapturedAtUtc! };
}

export function selectedGpuTemperature(
  gpu: HardwareStats['gpu'] | null | undefined,
  liveAdapters?: readonly GpuAdapterReading[],
  now = Date.now(),
): GpuTemperatureReading | null {
  if (!gpu?.adapter_id) return null;
  const matches = (liveAdapters ?? gpu.adapters ?? []).filter(adapter => adapter.id === gpu.adapter_id);
  if (matches.length !== 1) return null;
  return gpuTemperatureReading(matches[0], now);
}
