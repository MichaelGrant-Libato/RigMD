import type { DiagnosticReading } from './diagnosticLanguage.ts';
import { readingPercent } from './diagnosticReadingVisuals.ts';
import { getFindingDetails } from './findingDetails.ts';
import { REVIEW_THRESHOLDS } from './reviewThresholds.ts';
import { MISSING_FOLLOW_UP } from './followUpReadings.ts';

const usageMetrics: Record<string, { used: string; remaining: string; reviewAt?: number; summary: string }> = {
  memory_usage: { used: 'In use', remaining: 'Available', reviewAt: REVIEW_THRESHOLDS.memory,
    summary: 'This is the memory running apps use, not space for saved files. High use does not mean the RAM is damaged.' },
  storage_usage: { used: 'Space used', remaining: 'Space free', reviewAt: REVIEW_THRESHOLDS.storage,
    summary: 'Windows, apps and files share this space. This does not test the health of the drive.' },
  cpu_usage: { used: 'Busy', remaining: 'Not busy', reviewAt: REVIEW_THRESHOLDS.cpu,
    summary: 'This shows how busy the processor was, not how hot it was or whether it is damaged.' },
  battery_charge: { used: 'Charge remaining', remaining: 'Used charge',
    summary: 'This is remaining charge, not battery wear or an estimate of how long it will last.' },
};

export function getFindingVisual(reading: DiagnosticReading) {
  const view = getFindingDetails(reading);
  const id = reading.metric_id ?? '';
  const temperature = id === 'cpu_temperature' || id === 'gpu_temperature';
  const unavailable = view.tone === 'unknown' || reading.evidence_kind === 'unavailable';
  const base = { label: view.label, value: view.value, tone: view.tone, statusLabel: view.statusLabel,
    summary: view.explanation, nextStep: view.nextStep };
  if (reading.evidence_kind === MISSING_FOLLOW_UP) return { ...base, kind: 'unavailable' as const, tone: 'unknown' as const,
    title: 'No follow-up reading saved', temperature, caption: 'Original value was not reused' };
  if (unavailable) return { ...base, kind: 'unavailable' as const, tone: 'unknown' as const,
    statusLabel: view.notAssessed ? 'Not assessed' : 'Reading unavailable',
    title: view.notAssessed ? 'Not tested by this scan' : 'No verified reading', temperature,
    caption: view.notAssessed ? 'No test result to rate' : 'Not confirmed normal or faulty',
    summary: view.tone === 'unknown' ? view.explanation : 'RigMD could not obtain a verified reading. This does not tell us whether this part is working normally.',
    nextStep: view.tone === 'unknown' ? view.nextStep : 'Review the recorded evidence or take a new reading before deciding whether an action is needed.' };

  const config = usageMetrics[id] ?? (id.startsWith('storage_usage_') ? usageMetrics.storage_usage : undefined);
  const volume = id.startsWith('storage_usage_') ? /^(\d+(?:\.\d+)?)% used(?:;\s*\d+(?:\.\d+)? GB free)?$/.exec(reading.value ?? '') : null;
  const rawPercent = volume ? Number(volume[1]) : readingPercent(reading);
  const percent = rawPercent !== null && Number.isFinite(rawPercent) && rawPercent >= 0 && rawPercent <= 100 ? rawPercent : null;
  if (config && percent !== null) {
    // Only the known saved format can supply capacity; a percentage alone cannot invent GB values.
    const capacityMatch = /^(\d+(?:\.\d+)?)%\s+\((\d+(?:\.\d+)?)\s*\/\s*(\d+(?:\.\d+)?)\s+GB\)$/.exec(reading.value ?? '');
    const used = capacityMatch ? Number(capacityMatch[2]) : NaN;
    const total = capacityMatch ? Number(capacityMatch[3]) : NaN;
    const capacity = id !== 'battery_charge' && Number.isFinite(total) && Number.isFinite(used) && total > 0 && used >= 0 && used <= total && Math.abs(used / total * 100 - percent) <= 1
      ? { used, total, available: Number((total - used).toFixed(1)) } : null;
    return { ...base, kind: 'usage' as const, percent, remainder: Number((100 - percent).toFixed(2)),
      usedLabel: config.used, remainingLabel: config.remaining, capacity,
      reviewAt: view.tone === 'normal' || view.tone === 'attention' ? config.reviewAt : undefined,
      summary: config.summary };
  }
  if (config) return { ...base, kind: 'unavailable' as const, tone: 'unknown' as const, statusLabel: 'Reading unavailable',
    title: 'No verified reading', temperature: false, caption: 'A usable percentage was not recorded',
    summary: 'The saved value cannot be shown as a reliable usage reading. Review the recorded evidence before drawing a conclusion.',
    nextStep: 'Take a new reading before deciding whether any action is needed.' };

  if (temperature) {
    const match = /^(\d+(?:\.\d+)?)\s*(?:\u00b0C|C)$/.exec(reading.value ?? '');
    const celsius = match ? Number(match[1]) : NaN;
    if (!(celsius > 0 && celsius <= 125)) return { ...base, kind: 'unavailable' as const, tone: 'unknown' as const,
      statusLabel: 'Reading unavailable', title: 'No verified reading', temperature: true, caption: 'No usable sensor value',
      summary: 'A usable temperature was not recorded. This does not tell us whether the part was hot or cool.',
      nextStep: 'Use a manufacturer temperature check if overheating symptoms continue.' };
    return { ...base, kind: 'temperature' as const, celsius,
      summary: 'A sensor reported this temperature during the check. It does not test the whole cooling system.' };
  }
  if (id === 'storage_health') return { ...base, kind: 'report' as const,
    summary: /^no warning reported$/i.test(reading.value ?? '') ? 'Windows did not report a drive-health warning. This is not a full drive-health test.' : view.explanation,
    nextStep: view.tone === 'attention' ? 'Back up important files before further drive checks or repairs.' : 'No repair is indicated by this report alone. Seek a drive-specific check if symptoms continue.' };
  return { ...base, kind: 'observation' as const };
}

export function getUsageComparison(reading: DiagnosticReading, original: DiagnosticReading[]) {
  if (!reading.metric_id) return null;
  const matches = original.filter(row => row.metric_id === reading.metric_id);
  if (matches.length !== 1) return null;
  const first = matches[0];
  const initial = getFindingVisual(first), latest = getFindingVisual(reading);
  const at = Date.parse(reading.captured_at ?? ''), before = Date.parse(first.captured_at ?? '');
  if (initial.kind !== 'usage' || latest.kind !== 'usage' || !Number.isFinite(at) || !Number.isFinite(before) || at <= before) return null;
  return { original: initial.percent, latest: latest.percent,
    description: initial.percent === latest.percent ? 'Same percentage in both saved readings'
      : `${latest.percent < initial.percent ? 'Lower' : 'Higher'} ${reading.metric_id === 'battery_charge' ? 'charge' : 'use'} in this follow-up` };
}
