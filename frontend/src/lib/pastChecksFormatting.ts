import { isEventReading } from './eventEvidence.ts';
import { memoryWarningLabel } from './repairSupport.ts';
import type { DiagnosticReading } from './diagnosticLanguage';

export const COMPONENT_LABELS: Record<string, string> = {
  cpu: 'Processor',
  processor: 'Processor',
  memory: 'Memory',
  ram: 'Memory',
  gpu: 'Graphics',
  graphics: 'Graphics',
  storage: 'Storage',
  disk: 'Storage',
  os: 'Windows',
  drivers: 'Drivers',
  driver: 'Drivers',
  battery: 'Battery / Power',
  network: 'Network / Internet',
  display: 'Display',
  displays: 'Connected displays',
  audio: 'Audio devices',
  motherboard: 'Motherboard',
};

export const SCENARIO_LABELS: Record<string, string> = {
  'slow-system': 'Slow system check',
  'slow-boot': 'Slow boot check',
  'blue-screen-crash': 'Blue screen / crash check',
  'system-crashes': 'System stop error check',
  'driver-error': 'Driver problem check',
  'driver-conflict': 'Hardware drivers check',
  'no-display': 'Display problem check',
  'display-artifacts': 'Display and graphics check',
  'overheating-loud-fan': 'Overheating / fan check',
  'thermal-throttling': 'Thermals and cooling check',
  'network-problem': 'Network problem check',
  'network-issue': 'Network problem check',
  'network-drops': 'Network latency check',
  'app-crashes': 'Application crashes check',
  'stuttering-freezing': 'Stuttering / freezing check',
  'storage-problem': 'Storage problem check',
  'disk-space': 'Storage volume and space check',
  'disk-full': 'Storage space check',
  'rapid-battery-drain': 'Battery drain check',
  'battery-drain': 'Battery drain check',
};

export function titleFromId(id: string): string {
  return id
    .replace(/[-_]/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

export function parseComponentIds(raw?: string | string[] | null): string[] {
  if (!raw) return [];
  if (Array.isArray(raw)) return raw.map((item) => String(item).trim()).filter(Boolean);

  const trimmed = raw.trim();
  if (trimmed.startsWith('[') && trimmed.endsWith(']')) {
    try {
      const parsed = JSON.parse(trimmed);
      if (Array.isArray(parsed)) {
        return parsed.map((item) => String(item).trim()).filter(Boolean);
      }
    } catch {
      // Ignore JSON parse failure and treat as delimited string
    }
  }

  return trimmed
    .split(/[,;\s]+/)
    .map((item) => item.trim())
    .filter(Boolean);
}

export function getSessionCheckLabel(session: {
  diagnosis_mode?: string | null;
  scenario_id?: string | null;
  component_ids?: string | string[] | null;
  symptom_type?: string | null;
  symptom?: string | null;
  diagnosed_category?: string | null;
}): string {
  const mode = session.diagnosis_mode?.trim().toLowerCase();
  const scenarioId = session.scenario_id?.trim();
  const componentIds = parseComponentIds(session.component_ids);

  if (mode === 'scenario' && scenarioId) {
    return SCENARIO_LABELS[scenarioId] ?? `${titleFromId(scenarioId)} check`;
  }

  if (mode === 'component' && componentIds?.length) {
    const labels = componentIds.map(
      (id) => COMPONENT_LABELS[id] ?? titleFromId(id),
    );

    if (labels.length === 1) {
      return `${labels[0]} check`;
    }
    if (labels.length === 2) {
      return `${labels[0]} and ${labels[1]} check`;
    }
    return `${labels.slice(0, -1).join(', ')}, and ${labels[labels.length - 1]} check`;
  }

  if (mode === 'full') {
    return 'Full device check';
  }

  const alternateSymptom = session.symptom;
  const symptom = session.symptom_type?.trim() || alternateSymptom?.trim();

  if (symptom && symptom.toLowerCase() !== 'not available' && symptom.toLowerCase() !== 'unknown') {
    return symptom;
  }

  if (session.diagnosed_category) {
    return `${session.diagnosed_category} check`;
  }

  return 'Full device check';
}

export type ResolutionCategoryType =
  | 'no_new_events'
  | 'resolved_after_action'
  | 'still_active'
  | 'needs_recheck'
  | 'monitor_only'
  | 'not_applicable'
  | 'open';

export interface ResolutionCategoryInfo {
  category: ResolutionCategoryType;
  label: string;
  className: string;
  fallbackText: string;
}

export function getResolutionCategory(
  status?: string | null,
  actionCategory?: string | null,
  diagnosedCategory?: string | null,
  hasAction = false,
  noNewCrashes = false,
  proof?: DiagnosticReading[],
): ResolutionCategoryInfo {
  const normStatus = (status ?? '').toLowerCase().trim();
  const normAction = (actionCategory ?? '').toLowerCase().trim();
  const normCategory = (diagnosedCategory ?? '').toLowerCase().trim();

  if (normStatus === 'not_applicable') return {
    category: 'not_applicable', label: 'Not applicable',
    className: 'border-slate-500/40 bg-slate-500/10 text-slate-300',
    fallbackText: 'Windows reported that the selected component is not present. No repair is indicated by this check.',
  };

  if (normStatus === 'resolved') {
    if (normCategory.includes('crash') || normCategory.includes('stop error') || noNewCrashes) {
      return {
        category: 'no_new_events',
        label: 'No New Events',
        className: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300',
        fallbackText: 'No new matching crash records were found during this follow-up period. This does not prove that the recurring problem is fixed.',
      };
    }

    return {
      category: 'resolved_after_action',
      label: hasAction ? 'Not detected after action' : 'Not detected in this check',
      className: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300',
      fallbackText: hasAction
        ? 'The checked warning is not detected after the action. This does not establish overall hardware health or prove the action caused the change.'
        : 'The checked warning is not detected in these readings. No repair is implied; workload can change on its own. This is not a complete hardware-health assessment.',
    };
  }

  if (normStatus === 'still_active' || normStatus === 'unresolved') {
    return {
      category: 'still_active',
      label: memoryWarningLabel(proof) || 'Still Active',
      className: 'border-red-500/40 bg-red-500/10 text-red-300',
      fallbackText: 'The issue is still detected after checking the latest live scan.',
    };
  }

  if (normStatus === 'needs_recheck') {
    return {
      category: 'needs_recheck',
      label: proof?.length ? 'Partially checked' : 'Awaiting follow-up',
      className: 'border-orange-500/40 bg-orange-500/10 text-orange-300',
      fallbackText: proof?.length
        ? 'Review the unavailable measurements separately. Repeating a scan cannot supply an unsupported sensor or test.'
        : 'A follow-up reading has not been recorded yet. The effect of the action is not confirmed.',
    };
  }

  if (normStatus === 'unknown' || normStatus === 'unavailable' || normCategory.includes('unavailable') || normCategory.includes('could not verify')) {
    return {
      category: 'needs_recheck', label: 'Could not verify',
      className: 'border-slate-500/40 bg-slate-500/10 text-slate-300',
      fallbackText: 'The follow-up check could not verify the result. Missing readings do not mean the issue is resolved.',
    };
  }

  if (normAction.includes('monitor') || normAction.includes('guidance')) {
    return {
      category: 'monitor_only',
      label: 'Monitor Only',
      className: 'border-slate-500/40 bg-slate-500/10 text-slate-300',
      fallbackText: 'No urgent hardware repair required. Keep monitoring live metrics as you work.',
    };
  }

  return {
    category: 'open',
    label: 'Open',
    className: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-300',
    fallbackText: 'Check the current status to compare this diagnosis with fresh Windows readings.',
  };
}

export function matchesCheckType(
  session: {
    diagnosis_mode?: string | null;
    scenario_id?: string | null;
    component_ids?: string | string[] | null;
    last_action_status?: string | null;
    last_action_summary?: string | null;
    action_category?: string | null;
    is_recurring?: boolean | null;
  },
  checkTypeFilter: string,
): boolean {
  const normFilter = checkTypeFilter.trim().toLowerCase();
  if (normFilter === 'all' || normFilter === 'all checks' || normFilter === 'all types') {
    return true;
  }

  const mode = session.diagnosis_mode?.trim().toLowerCase();
  const componentIds = parseComponentIds(session.component_ids);
  const hasScenario = Boolean(session.scenario_id?.trim());
  const hasComponents = componentIds.length > 0;

  if (normFilter === 'full' || normFilter === 'full device') {
    return mode === 'full' || (!hasScenario && !hasComponents);
  }

  if (normFilter === 'component' || normFilter === 'component checks') {
    return mode === 'component' || hasComponents;
  }

  if (normFilter === 'scenario' || normFilter === 'scenario checks') {
    return mode === 'scenario' || hasScenario;
  }

  if (normFilter === 'react' || normFilter === 'react actions') {
    return (
      Boolean(session.last_action_status || session.last_action_summary) ||
      (session.action_category?.toLowerCase().includes('maintain') ?? false) ||
      (session.action_category?.toLowerCase().includes('automated') ?? false)
    );
  }

  if (normFilter === 'recurring' || normFilter === 'recurring only') {
    return Boolean(session.is_recurring);
  }

  return true;
}

export function getTelemetryDeltaSummary(
  originalProof?: Array<{ metric_id?: string; label: string; value: string }>,
  resolutionProof?: Array<{ metric_id?: string; label: string; value: string }>,
): string | null {
  if (!originalProof || !resolutionProof || resolutionProof.length === 0) {
    return null;
  }

  const deltas: string[] = [];
  let comparisons = 0;

  for (const res of resolutionProof) {
    if (isEventReading(res)) continue;
    const orig = originalProof.find(
      (p) => p.metric_id && res.metric_id ? p.metric_id === res.metric_id
        : p.label?.toLowerCase() === res.label?.toLowerCase(),
    );
    if (!orig) continue;

    const parseMetric = (value: string) => {
      const match = /^\s*(-?\d+(?:\.\d+)?)\s*(%|GB|MB|ms|\u00b0C)?\s*$/i.exec(value ?? '');
      return match ? { number: Number(match[1]), unit: (match[2] ?? '').toLowerCase() } : null;
    };
    const before = parseMetric(orig.value);
    const after = parseMetric(res.value);
    const comparable = before && after && before.unit === after.unit;
    const numOrig = comparable ? before.number : Number.NaN;
    const numRes = comparable ? after.number : Number.NaN;

    if (!Number.isNaN(numOrig) && !Number.isNaN(numRes)) {
      comparisons++;
      const diff = numRes - numOrig;
      if (Math.abs(diff) >= 0.05) {
        const unit = orig.value.includes('%')
          ? ' percentage points'
          : orig.value.includes('GB')
            ? ' GB'
            : orig.value.includes('MB')
              ? ' MB'
              : before?.unit === 'ms' ? ' ms' : before?.unit === '\u00b0c' ? ' \u00b0C' : '';
        const direction =
          diff < 0
            ? `decreased by ${Math.abs(diff).toFixed(1)}${unit}`
            : `increased by ${diff.toFixed(1)}${unit}`;
        deltas.push(`${orig.label} ${direction} (${orig.value} → ${res.value})`);
      } else if (orig.label.toLowerCase().includes('crash')) {
        deltas.push(`${orig.label} remained unchanged at ${res.value}`);
      }
    } else if (orig.value !== res.value) {
      deltas.push(`${orig.label} changed from "${orig.value}" to "${res.value}"`);
    }
  }

  if (deltas.length === 0) {
    return comparisons > 0 ? 'No measurable change was detected in the comparable saved readings.' : null;
  }

  return deltas.join('; ');
}
