export interface PerformanceRecord {
  id: string; createdAt: string; operation: string; outcome: string; durationMs: number;
  recordCount: number | null; unavailableCount: number | null; mode: string; engine: string;
  httpStatus: number | null; apiWorkingSetMb: number | null; apiCpuTimeMs: number | null;
  appVersion: string; buildConfiguration: string; deviceKey: string; osVersion: string; architecture: string; logicalProcessors: number;
}

export function unavailableReadingCount(readings: unknown): number {
  if (!Array.isArray(readings)) return 0;
  return readings.filter(row => row && typeof row === 'object' &&
    (/unknown|unavailable|not_assessed|not_verified|unverified/.test(String(row.status ?? '').toLowerCase()) ||
      ['unavailable', 'not_assessed', 'not_verified'].includes(String(row.evidence_kind ?? '').toLowerCase()))).length;
}

export const operationLabels: Record<string, string> = {
  'startup.api_start.desktop': 'Startup · starting the local service',
  'startup.api_ready.desktop': 'Startup · waiting for the local service',
  'startup.webview.desktop': 'Startup · preparing the app window',
  'startup.dashboard.desktop': 'Startup · loading the Home screen',
  'startup.summary.desktop': 'Desktop launch → saved-check summary ready',
  'startup.desktop': 'Desktop launch → dashboard ready', 'scan.ui': 'Start check → result displayed',
  'scan.backend': 'Check request · backend', 'history.ui': 'Saved checks · screen ready',
  'history.backend': 'Saved checks · backend', 'report.ui': 'Report · preview ready',
  'pdf.desktop': 'PDF · generate and write file', 'pdf_request.ui': 'PDF · request including Save dialog',
  'text.ui': 'Text · download requested', 'recheck.backend': 'Recheck request · backend',
  'action_request.backend': 'Action request · backend (not repair verification)',
};

export function measurementContext(row: PerformanceRecord) {
  return [row.appVersion, row.buildConfiguration, row.deviceKey, row.osVersion, row.architecture, row.logicalProcessors].join(' | ');
}

export function targetFor(row: PerformanceRecord): number | null {
  if (row.outcome !== 'completed') return null;
  if (row.operation === 'startup.desktop' || row.operation === 'report.ui') return 5000;
  if (row.operation === 'history.ui' && (row.recordCount ?? 0) >= 50) return 3000;
  return null;
}

export function summarizePerformance(records: PerformanceRecord[]) {
  const groups = new Map<string, PerformanceRecord[]>();
  for (const row of records) {
    if (!Number.isFinite(row.durationMs) || row.durationMs < 0) continue;
    // Never mix release/debug builds, devices, operating systems or different operation boundaries.
    const key = `${measurementContext(row)} | ${row.operation}`;
    groups.set(key, [...(groups.get(key) ?? []), row]);
  }
  return [...groups.entries()].map(([key, rows]) => {
    const timings = rows.filter(r => r.outcome === 'completed' || r.outcome === 'incomplete').map(r => r.durationMs).sort((a, b) => a - b);
    const percentile = (p: number) => timings.length ? timings[Math.max(0, Math.ceil(p * timings.length) - 1)] : null;
    const eligible = rows.filter(r => targetFor(r) !== null);
    return { key, operation: rows[0].operation, context: measurementContext(rows[0]), runs: rows.length,
      completed: rows.filter(r => r.outcome === 'completed').length,
      incomplete: rows.filter(r => r.outcome === 'incomplete').length,
      failed: rows.filter(r => r.outcome === 'failed').length, canceled: rows.filter(r => r.outcome === 'canceled').length,
      medianMs: timings.length ? (timings[Math.floor((timings.length - 1) / 2)] + timings[Math.floor(timings.length / 2)]) / 2 : null, p95Ms: percentile(0.95), maxMs: timings.length ? timings[timings.length - 1] : null,
      eligible: eligible.length, withinTarget: eligible.filter(r => r.durationMs <= targetFor(r)!).length,
      overTarget: eligible.filter(r => r.durationMs > targetFor(r)!).length };
  });
}

// JSON retains numeric precision and explicit nulls and cannot execute spreadsheet formulas.
export function performanceExport(records: PerformanceRecord[], health: unknown, retentionLimit: number) {
  return JSON.stringify({ schemaVersion: 1, exportedAt: new Date().toISOString(), retentionLimit, health,
    method: 'Observed local operations; durations use monotonic clocks. Screen-ready means React committed and two animation frames elapsed, not a human usability result. API CPU time includes concurrent API activity. PDF request time includes the user dialog; native PDF generation time excludes it. Target counts are observations, not overall acceptance certification.',
    targets: { 'NFR-1': 'Desktop launch to dashboard ready <=5000 ms', 'NFR-3': 'At least 50 saved checks displayed <=3000 ms', 'NFR-4': 'Selected report preview ready <=5000 ms' },
    summaries: summarizePerformance(records), records }, null, 2);
}
