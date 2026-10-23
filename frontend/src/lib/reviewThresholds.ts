// RigMD workload review policies, not manufacturer failure limits.
export const REVIEW_THRESHOLDS = { cpu: 80, memory: 82, storage: 80, temperature: 85 } as const;
export const NETWORK_THRESHOLDS = { latencyMs: 150, lossPercent: 5, weakSignalPercent: 45 } as const;

export function networkReviewStatus(net?: {
  query_succeeded?: boolean; has_active_adapter?: boolean; has_ipv4_address?: boolean;
  has_default_gateway?: boolean; has_dns_servers?: boolean; dns_resolution_succeeded?: boolean;
  is_wifi?: boolean; ping_latency_ms?: number | null; packet_loss_percent?: number | null; wifi_signal_strength?: number | null;
} | null): 'unknown' | 'high' | 'normal' {
  if (!net || net.query_succeeded === false) return 'unknown';
  const flags = [net.has_active_adapter, net.has_ipv4_address, net.has_default_gateway, net.has_dns_servers, net.dns_resolution_succeeded];
  const ping = net.ping_latency_ms, loss = net.packet_loss_percent, signal = net.wifi_signal_strength;
  const validPing = typeof ping === 'number' && Number.isFinite(ping) && ping >= 0;
  const validLoss = typeof loss === 'number' && Number.isFinite(loss) && loss >= 0 && loss <= 100;
  const validSignal = typeof signal === 'number' && Number.isFinite(signal) && signal > 0 && signal <= 100;
  if (net.query_succeeded === true && flags.some(flag => flag === false) ||
      validPing && ping >= NETWORK_THRESHOLDS.latencyMs || validLoss && loss >= NETWORK_THRESHOLDS.lossPercent ||
      net.is_wifi && validSignal && signal <= NETWORK_THRESHOLDS.weakSignalPercent) return 'high';
  return !validPing || !validLoss || net.is_wifi && !validSignal || flags.some(flag => flag !== true) ? 'unknown' : 'normal';
}

export function percentReviewStatus(value: unknown, threshold: number): 'unknown' | 'high' | 'normal' {
  return typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > 100
    ? 'unknown' : value >= threshold ? 'high' : 'normal';
}
