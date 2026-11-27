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

export function explanationStatusLabel(status?: string) {
  switch (status) {
    case 'pending': return 'Requesting a Gemini explanation…';
    case 'local-only': return 'Local-only mode was enabled.';
    case 'not-needed': return 'Gemini is used only when readings need review.';
    case 'sign-in-required': return 'Connect a verified RigMD account to use Gemini.';
    case 'rate-limited': return 'A cloud usage limit was reached.';
    case 'timed-out': return 'The cloud request timed out.';
    case 'incomplete': return 'Gemini returned an incomplete explanation.';
    case 'service-unavailable': return 'The cloud explanation service was unavailable.';
    case 'interrupted': return 'The cloud explanation request was interrupted.';
    default: return null;
  }
}
