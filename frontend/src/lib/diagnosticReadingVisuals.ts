import type { DiagnosticReading } from './diagnosticLanguage';

export function readingTone(status?: string) {
  const value = (status ?? '').toLowerCase();
  if (/unknown|unavailable|could not/.test(value)) return 'unknown';
  if (/high|elevated|attention|active|warning/.test(value)) return 'attention';
  if (/normal|observed|within/.test(value)) return 'normal';
  return 'info';
}

export function readingPercent(reading: DiagnosticReading): number | null {
  if (!['cpu_usage', 'memory_usage', 'storage_usage', 'battery_charge'].includes(reading.metric_id ?? '')) return null;
  const match = /^(\d+(?:\.\d+)?)%(?:\s|$)/.exec(reading.value ?? '');
  const number = match ? Number(match[1]) : NaN;
  return Number.isFinite(number) && number >= 0 && number <= 100 ? number : null;
}

export function explanationSourceLabel(source?: string, model?: string) {
  if (source === 'gemini') return `Gemini${model ? ` (${model})` : ''}`;
  if (source === 'local') return 'Built-in guidance (not Gemini)';
  return 'Source not recorded';
}
