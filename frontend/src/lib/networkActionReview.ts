import type { DiagnosticReading } from './diagnosticLanguage.ts';

export function dnsCacheReviewReason(readings: DiagnosticReading[]) {
  function single(id: string, normal: boolean) {
    const rows = readings.filter(row => row.metric_id === id);
    if (rows.length !== 1 || /^(unavailable|missing_follow_up)$/.test(rows[0].evidence_kind ?? '')) return false;
    const row = rows[0];
    return normal ? /^(normal|normal now)$/.test(row.status ?? '') && row.value === 'Reported'
      : /^(high|elevated|warning|critical|still needs attention)$/.test(row.status ?? '') &&
        /^(Not reported|Failed|Test did not pass)$/.test(row.value ?? '');
  }
  if (!['network_adapter', 'network_address', 'network_gateway', 'network_dns_config'].every(id => single(id, true)))
    return 'Reconnect Wi-Fi or Ethernet and check the network settings, then use Check again. Refreshing saved website addresses cannot restore a disconnected adapter or missing network configuration.';
  if (!single('network_dns', false))
    return 'These saved readings do not show a verified website-address lookup failure. Check the current connection before choosing a DNS-cache action. Slow responses or lost replies alone do not establish a DNS-cache problem.';
  return null;
}
