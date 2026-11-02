import { createMeasurement, type MeasurementOutcome as Outcome, type MeasurementDetails as Details } from './performanceMeasurement';
export { afterScreenPaint } from './performanceMeasurement';
import { apiFetch } from './api';
import { getLocalApiToken } from './localApiSession';

let deliveryFailures = 0;
export const getPerformanceDeliveryFailures = () => deliveryFailures;

export async function recordPerformance(operation: string, durationMs: number, outcome: Outcome, details: Details = {}) {
  if (!getLocalApiToken()) return false; // Never send measurements from the public download website.
  try {
    const response = await apiFetch('/api/performance', { method: 'POST', signal: AbortSignal.timeout(5000),
      body: JSON.stringify({ id: crypto.randomUUID(), operation, outcome, durationMs, ...details }) });
    if (!response.ok) throw new Error('Performance record unavailable');
    return true;
  } catch { deliveryFailures++; return false; }
}

export function beginMeasurement(operation: string, details: Details = {}) {
  return createMeasurement((duration, outcome, extra) => { void recordPerformance(operation, duration, outcome, extra); }, details);
}

type Measurement = ReturnType<typeof beginMeasurement>;
let pendingScan: { measurement: Measurement; sessionId?: string; timer: ReturnType<typeof setTimeout> } | null = null;
export function startScanMeasurement(mode: string) {
  pendingScan?.measurement.finish('canceled');
  if (pendingScan) clearTimeout(pendingScan.timer);
  const measurement = beginMeasurement('scan.ui', { mode });
  const timer = setTimeout(() => { measurement.finish('incomplete'); if (pendingScan?.measurement === measurement) pendingScan = null; }, 600_000);
  pendingScan = { measurement, timer };
  return {
    saved(sessionId: string) { if (pendingScan?.measurement === measurement) pendingScan.sessionId = sessionId; },
    failed() { measurement.finish('failed'); clearTimeout(timer); if (pendingScan?.measurement === measurement) pendingScan = null; },
  };
}

export function finishScanMeasurement(sessionId: string, unavailableCount: number, failed = false) {
  if (!pendingScan || pendingScan.sessionId !== sessionId) return;
  pendingScan.measurement.finish(failed ? 'failed' : unavailableCount ? 'incomplete' : 'completed', { unavailableCount });
  clearTimeout(pendingScan.timer);
  pendingScan = null;
}

export function notifyDesktopDashboardReady(complete: boolean) {
  if (!getLocalApiToken()) return;
  (window as Window & { chrome?: { webview?: { postMessage: (message: unknown) => void } } }).chrome?.webview?.postMessage({
    type: 'rigmd:dashboard-ready', outcome: complete ? 'completed' : 'incomplete',
  });
}
