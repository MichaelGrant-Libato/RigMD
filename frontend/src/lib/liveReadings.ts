import type { HardwareStats } from '../types/rigmd';

export interface LiveUsageSample {
  cpuUsagePercent?: number | null;
  cpuUsageSource?: string;
  cpuUsageCapturedAtUtc?: string | null;
  cpuUsageWindowSeconds?: number | null;
  ramUsagePercent?: number | null;
  ramUsedGb?: number | null;
  ramTotalGb?: number | null;
}

export function measuredPercent(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100 ? value : null;
}

export function mergeLiveUsage(stats: HardwareStats, sample: LiveUsageSample, now = Date.now()): HardwareStats {
  const captured = sample.cpuUsageCapturedAtUtc ? Date.parse(sample.cpuUsageCapturedAtUtc) : null;
  const cpuFresh = captured === null || (Number.isFinite(captured) && now - captured <= 5000 && captured - now <= 2000);
  const memory = measuredPercent(sample.ramUsagePercent);
  const used = typeof sample.ramUsedGb === 'number' && Number.isFinite(sample.ramUsedGb) && sample.ramUsedGb >= 0 ? sample.ramUsedGb : null;
  const total = typeof sample.ramTotalGb === 'number' && Number.isFinite(sample.ramTotalGb) && sample.ramTotalGb > 0 ? sample.ramTotalGb : stats.ram.total_gb;
  return {
    ...stats,
    cpu: { ...stats.cpu, usage_percent: cpuFresh ? measuredPercent(sample.cpuUsagePercent) : null,
      usage_source: cpuFresh ? sample.cpuUsageSource ?? 'Unavailable' : 'Unavailable',
      usage_captured_at_utc: sample.cpuUsageCapturedAtUtc, usage_window_seconds: sample.cpuUsageWindowSeconds },
    ram: { ...stats.ram, usage_percent: memory, used_gb: memory !== null && used !== null && used <= total ? used : null, total_gb: total },
  };
}
