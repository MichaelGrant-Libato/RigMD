import { expandDiagnosticReadings, type DiagnosticReading } from './diagnosticLanguage.ts';
import { getFindingDetails } from './findingDetails.ts';

export function getRemediationFindings(readings: DiagnosticReading[]) {
  const findings = new Map<string, { id: string; label: string; kind: 'memory' | 'storage' | 'guidance'; readings: DiagnosticReading[] }>();
  for (const reading of expandDiagnosticReadings(readings)) {
    const view = getFindingDetails(reading);
    if (view.tone !== 'attention') continue;
    const metric = reading.metric_id ?? '';
    const kind = /^memory_(usage|app_warning)$/.test(metric) ? 'memory'
      : /^storage_usage(?:_|$)/.test(metric) ? 'storage' : 'guidance';
    const id = kind === 'memory' ? 'memory' : metric || `legacy-${findings.size}`;
    const existing = findings.get(id);
    if (existing) existing.readings.push(reading);
    else findings.set(id, { id, label: kind === 'memory' ? 'Memory use' : view.label, kind, readings: [reading] });
  }
  return [...findings.values()];
}
