import { measurementContext, targetFor, type PerformanceRecord } from './performanceMetrics.ts';

export const speedTasks = [
  { operation: 'startup.desktop', title: 'Opening RigMD', description: 'From starting the app until the Home screen is ready.', targetMs: 5000, empty: 'Close and reopen RigMD, then refresh these results.' },
  { operation: 'history.ui', title: 'Opening 50 saved checks', description: 'Until at least 50 saved checks are ready to view.', targetMs: 3000, empty: 'In Past Checks, clear the filters and choose Show 50 checks. Then refresh here.' },
  { operation: 'report.ui', title: 'Preparing your report', description: 'Until the selected report preview is ready to view.', targetMs: 5000, empty: 'Open Share Report and prepare a report. Then refresh here.' },
] as const;

export function secondsLabel(ms: number) {
  return `${(ms / 1000).toFixed(2)} seconds`;
}

export function friendlyBuildLabel(row: PerformanceRecord, index: number) {
  const version = row.appVersion.split('+')[0];
  const build = row.buildConfiguration === 'Debug' ? 'Development build' : row.buildConfiguration === 'Release' ? 'Release build' : 'Other build';
  return `Version ${version} · ${build} · Saved setup ${index + 1}`;
}

export function outcomeLabel(outcome: string) {
  return ({ completed: 'Finished', incomplete: 'Not fully measured', failed: 'Failed', canceled: 'Canceled' } as Record<string, string>)[outcome] ?? 'Unknown outcome';
}

export function appSpeedSummary(records: PerformanceRecord[], context: string) {
  // The visible summary uses the same build/device boundaries and targets as the evidence export.
  const selected = records.filter(row => measurementContext(row) === context && Number.isFinite(row.durationMs) && row.durationMs >= 0);
  return speedTasks.map(task => {
    const attempts = selected.filter(row => row.operation === task.operation)
      .sort((a, b) => (Date.parse(b.createdAt) || 0) - (Date.parse(a.createdAt) || 0));
    const eligible = attempts.filter(row => targetFor(row) !== null);
    const latest = eligible[0] ?? null;
    return { ...task, latest, latestAttempt: attempts[0] ?? null, measured: eligible.length,
      withinTarget: eligible.filter(row => row.durationMs <= task.targetMs).length,
      overTarget: eligible.filter(row => row.durationMs > task.targetMs).length,
      status: latest ? latest.durationMs <= task.targetMs ? 'within' : 'over' : 'unmeasured',
      incomplete: attempts.filter(row => row.outcome === 'incomplete').length,
      failed: attempts.filter(row => row.outcome === 'failed').length,
      canceled: attempts.filter(row => row.outcome === 'canceled').length };
  });
}
