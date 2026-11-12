import { percentReviewStatus, REVIEW_THRESHOLDS } from './reviewThresholds.ts';

export interface HomeOverview {
  scope: 'essential'; capturedAt: string; deviceName: string;
  cpu: { name: string; usagePercent: number | null; state: 'warming' | 'measured' | 'unavailable'; source: string; capturedAt: string | null; windowSeconds: number | null };
  memory: { totalGb: number | null; usedGb: number | null; usagePercent: number | null };
  storageQuerySucceeded: boolean;
  volumes: Array<{ drive: string; totalGb: number | null; usedGb: number | null; usagePercent: number | null }>;
  network: { querySucceeded: boolean; hasActiveAdapter: boolean | null; hasIpv4Address: boolean | null; hasDefaultGateway: boolean | null; hasDnsServers: boolean | null; kind: 'wireless' | 'wired' | 'other' | null; ipAddress: string | null };
}
const percent = (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100;
const nonnegative = (value: unknown) => value === null || typeof value === 'number' && Number.isFinite(value) && value >= 0;
const boolOrNull = (value: unknown) => value === null || typeof value === 'boolean';
export function parseHomeOverview(value: unknown): HomeOverview {
  const row = value as HomeOverview;
  if (!row || row.scope !== 'essential' || typeof row.deviceName !== 'string' || !Number.isFinite(Date.parse(row.capturedAt)) ||
      !row.cpu || !['warming', 'measured', 'unavailable'].includes(row.cpu.state) || typeof row.cpu.name !== 'string' || typeof row.cpu.source !== 'string' ||
      row.cpu.usagePercent !== null && !percent(row.cpu.usagePercent) ||
      row.cpu.state === 'measured' && (!percent(row.cpu.usagePercent) || typeof row.cpu.capturedAt !== 'string' || !Number.isFinite(Date.parse(row.cpu.capturedAt)) || typeof row.cpu.windowSeconds !== 'number' || !Number.isFinite(row.cpu.windowSeconds) || row.cpu.windowSeconds <= 0) ||
      row.cpu.state !== 'measured' && row.cpu.usagePercent !== null ||
      !row.memory || !nonnegative(row.memory.totalGb) || row.memory.totalGb === 0 || !nonnegative(row.memory.usedGb) ||
      row.memory.usedGb !== null && (row.memory.totalGb === null || row.memory.usedGb > row.memory.totalGb) ||
      row.memory.usagePercent !== null && !percent(row.memory.usagePercent) ||
      typeof row.storageQuerySucceeded !== 'boolean' || !Array.isArray(row.volumes) || row.volumes.some(volume => !volume || typeof volume.drive !== 'string' || !nonnegative(volume.totalGb) || volume.totalGb === 0 || !nonnegative(volume.usedGb) || volume.usedGb !== null && (volume.totalGb === null || volume.usedGb > volume.totalGb) || volume.usagePercent !== null && !percent(volume.usagePercent)) ||
      !row.network || typeof row.network.querySucceeded !== 'boolean' || ![row.network.hasActiveAdapter, row.network.hasIpv4Address, row.network.hasDefaultGateway, row.network.hasDnsServers].every(boolOrNull) || !['wireless', 'wired', 'other', null].includes(row.network.kind) || row.network.ipAddress !== null && typeof row.network.ipAddress !== 'string') {
    throw new Error('Essential readings are incomplete or invalid.');
  }
  return row;
}
export function essentialHomeReady(row: HomeOverview | null) { return row !== null && row.cpu.state !== 'warming'; }
export function fullInventoryNeeded(page: string) { return page === 'systemProfile' || page === 'reports'; }
export function sensorStreamingNeeded(page: string) { return fullInventoryNeeded(page) || page === 'newDiagnosis'; }
export function overviewPills(row: HomeOverview) {
  const variant = (state: string): 'ok' | 'warning' | 'neutral' => state === 'high' ? 'warning' : state === 'normal' ? 'ok' : 'neutral';
  const storage = row.volumes.map(volume => percentReviewStatus(volume.usagePercent, REVIEW_THRESHOLDS.storage));
  return [
    { label: 'Memory', variant: variant(percentReviewStatus(row.memory.usagePercent, REVIEW_THRESHOLDS.memory)) },
    { label: 'Storage', variant: variant(storage.includes('high') ? 'high' : !row.storageQuerySucceeded || !storage.length || storage.includes('unknown') ? 'unknown' : 'normal') },
    ...(percentReviewStatus(row.cpu.usagePercent, REVIEW_THRESHOLDS.cpu) === 'high' ? [{ label: 'Processor', variant: 'warning' as const }] : []),
    { label: 'Network', variant: row.network.querySucceeded && [row.network.hasActiveAdapter, row.network.hasIpv4Address, row.network.hasDefaultGateway, row.network.hasDnsServers].some(flag => flag === false) ? 'warning' as const : 'neutral' as const },
  ];
}
