export type MeasurementOutcome = 'completed' | 'incomplete' | 'failed' | 'canceled';
export interface MeasurementDetails { recordCount?: number; unavailableCount?: number; mode?: string; engine?: string; }
interface VisibilitySource {
  readonly visibilityState: string;
  addEventListener(type: string, listener: () => void): void;
  removeEventListener(type: string, listener: () => void): void;
}
// The clock and visibility source are injectable so timing boundaries can be verified without a running API.
export function createMeasurement(
  save: (durationMs: number, outcome: MeasurementOutcome, details: MeasurementDetails) => void,
  details: MeasurementDetails = {}, now: () => number = () => performance.now(), visibilitySource: VisibilitySource = document,
) {
  const started = now();
  let finished = false;
  let wasHidden = visibilitySource.visibilityState === 'hidden';
  const visibility = () => { wasHidden ||= visibilitySource.visibilityState === 'hidden'; };
  visibilitySource.addEventListener('visibilitychange', visibility);
  return { finish(outcome: MeasurementOutcome = 'completed', extra: MeasurementDetails = {}) {
    if (finished) return;
    finished = true;
    visibilitySource.removeEventListener('visibilitychange', visibility);
    save(Math.max(0, now() - started), wasHidden && outcome === 'completed' ? 'incomplete' : outcome, { ...details, ...extra });
  } };
}

export function afterScreenPaint(callback: () => void,
  schedule: (callback: FrameRequestCallback) => number = requestAnimationFrame,
  cancel: (handle: number) => void = cancelAnimationFrame,
) {
  let second = 0;
  const first = schedule(() => { second = schedule(callback); });
  return () => { cancel(first); cancel(second); };
}
