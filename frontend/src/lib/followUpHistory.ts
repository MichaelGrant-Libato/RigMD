import type { DiagnosticReading } from './diagnosticLanguage';

export interface FollowUpObservation {
  observation_id: string;
  checked_at: string;
  recorded_at?: string;
  status: string;
  summary: string;
  source?: string;
  proof: DiagnosticReading[];
}

export function followUpStatusLabel(status: string) {
  switch (status) {
    case 'resolved': return 'Warning not detected in this recheck';
    case 'still_active': return 'Warning still detected';
    case 'not_applicable': return 'Check not applicable';
    case 'needs_recheck': return 'Could not fully verify';
    default: return 'Outcome not recorded';
  }
}

export function followUpTimeLabel(value: string) {
  const date = new Date(value);
  return value.trim() && Number.isFinite(date.getTime()) ? date.toLocaleString() : 'Time unavailable';
}
