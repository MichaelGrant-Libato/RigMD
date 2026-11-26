import type { GpuAdapterReading } from '../types/rigmd';
import type { CpuTemperatureSample } from './temperatureTelemetry';

export interface DiskSample {
  deviceId: string;
  capturedAtUtc?: string | null;
  activeTimePercent?: number | null;
  readKbps?: number | null;
  writeKbps?: number | null;
  hasTransfers?: boolean | null;
  responseTimeMs?: number | null;
}

interface NetworkSample {
  id: string;
  sendKbps?: number | null;
  receiveKbps?: number | null;
  linkMbps?: number | null;
}

export interface DeviceSample extends CpuTemperatureSample {
  capturedAtUtc: string;
  cpuUsageCapturedAtUtc?: string | null;
  cpuUsagePercent?: number | null;
  cpuSpeedMhz?: number | null;
  cpuProcesses?: number | null;
  cpuThreads?: number | null;
  cpuHandles?: number | null;
  uptimeSeconds?: number | null;
  ramUsedGb?: number | null;
  ramTotalGb?: number | null;
  ramUsagePercent?: number | null;
  ramAvailableGb?: number | null;
  ramCommittedGb?: number | null;
  ramCommitLimitGb?: number | null;
  ramCachedGb?: number | null;
  batteryChargePercent?: number | null;
  batteryCharging?: boolean | null;
  gpuAdapters?: GpuAdapterReading[];
  disks?: DiskSample[];
  networkAdapters?: NetworkSample[];
  collectorStatus?: Array<{ name: string; state: string; lastReadAt?: string | null }>;
}

export interface DeviceFeed {
  samples: DeviceSample[];
  now: number;
  disconnected: boolean;
}

export function measurement(value: unknown, ceiling = Number.POSITIVE_INFINITY): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= ceiling ? value : null;
}

export function isCurrent(timestamp: string | null | undefined, now: number): boolean {
  const age = now - Date.parse(timestamp ?? '');
  return age >= -2000 && age <= 5000;
}

export function latestDeviceSample(feed: DeviceFeed | undefined): DeviceSample | undefined {
  const sample = feed?.samples[feed.samples.length - 1];
  return sample && feed && !feed.disconnected && isCurrent(sample.capturedAtUtc, feed.now) ? sample : undefined;
}

export function keepDeviceSample(samples: DeviceSample[], sample: DeviceSample, now: number): DeviceSample[] {
  if (!isCurrent(sample.capturedAtUtc, now)) return samples;
  const cutoff = now - 60000;
  const time = Date.parse(sample.capturedAtUtc);
  if (time < Date.parse(samples[samples.length - 1]?.capturedAtUtc ?? '')) return samples;
  return [...samples.filter(row => Date.parse(row.capturedAtUtc) >= cutoff && row.capturedAtUtc !== sample.capturedAtUtc),
    sample].slice(-160);
}

export function chartReadings(samples: DeviceSample[], now: number, select: (sample: DeviceSample) => {
  value: number | null;
  timestamp?: string | null;
}): { time: number; value: number | null }[] {
  const readings = new Map<number, number | null>();
  for (const sample of samples) {
    const packetTime = Date.parse(sample.capturedAtUtc);
    if (packetTime < now - 60000 || packetTime > now + 2000) continue;
    const reading = select(sample);
    const valid = reading.timestamp === undefined || isCurrent(reading.timestamp, packetTime);
    const time = reading.value === null || !valid || reading.timestamp === undefined
      ? packetTime : Date.parse(reading.timestamp!);
    if (Number.isFinite(time) && time >= now - 60000 && time <= now)
      readings.set(time, valid ? reading.value : null);
  }
  const output: { time: number; value: number | null }[] = [];
  for (const [time, value] of [...readings.entries()].sort((a, b) => a[0] - b[0])) {
    const prior = output[output.length - 1];
    if (prior && time - prior.time > 5000 && prior.value !== null && value !== null)
      output.push({ time: prior.time + 1, value: null });
    output.push({ time, value });
  }
  return output;
}

export function formatMeasurement(value: unknown, unit = '', digits = 0, zero = 'None'): string {
  const available = measurement(value);
  if (available === null) return 'Not available';
  if (available === 0) return zero;
  const min = 10 ** -digits;
  if (available < min / 2) return '<' + min.toFixed(digits) + unit;
  return available.toFixed(digits) + unit;
}

export function trafficScale(rows: { time: number; value: number | null }[]) {
  const maximumKbps = Math.max(0, ...rows.map(row => measurement(row.value) ?? 0));
  const divisor = maximumKbps >= 1000 ? 1000 : 1;
  const unit = divisor === 1000 ? 'Mbps' : 'Kbps';
  const minimumStep = Math.max(maximumKbps / divisor / 4, 0.25);
  const magnitude = 10 ** Math.floor(Math.log10(minimumStep));
  const step = ([1, 2, 2.5, 5, 10].find(value => value * magnitude >= minimumStep) ?? 10) * magnitude;
  return {
    unit,
    upperBound: step * 4,
    ticks: [0, 1, 2, 3, 4].map(tick => Number((tick * step).toPrecision(6))),
    rows: rows.map(row => ({ ...row, value: row.value === null ? null : row.value / divisor })),
  };
}

export function formatAdapterLinkSpeed(megabits: unknown): string {
  const speed = measurement(megabits);
  if (speed === null || speed === 0) return 'Not available';
  const value = speed >= 1000 ? speed / 1000 : speed;
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value)
    + (speed >= 1000 ? ' Gbps' : ' Mbps');
}
