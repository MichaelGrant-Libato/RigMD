import { friendlyActionCategory, expandDiagnosticReadings, getReadingPresentation, readingEvidenceLabel, readingStatusLabel, type DiagnosticReading } from './diagnosticLanguage.ts';
import { getCheckOverview, getCheckStatus, getCheckEvidenceLabel, getCheckCoverageNotice, getLatestCheckSummary } from './checkPresentation.ts';
import { getSessionCheckLabel } from './pastChecksFormatting.ts';
import { readingTone } from './diagnosticReadingVisuals.ts';
import { validReading } from './storageTelemetry.ts';
import { getFollowUpReadings } from './followUpReadings.ts';
import type { DashboardSummary, HardwareStats, SessionSummary } from '../types/rigmd.ts';

export function formatDate(value: string | Date | null | undefined) {
  if (!value) return 'Not available';
  const date = value instanceof Date ? value : new Date(value);
  if (!Number.isFinite(date.getTime())) return 'Not available';
  return date.toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
}

export function buildReportFileName(sessionCode: string | null | undefined, generatedAt: Date) {
  const code = sessionCode?.replace(/[^a-zA-Z0-9_-]/g, '') || 'device';
  // Match the local calendar date shown by formatDate for this frozen report.
  const date = [generatedAt.getFullYear(), String(generatedAt.getMonth() + 1).padStart(2, '0'),
    String(generatedAt.getDate()).padStart(2, '0')].join('-');
  return `RigMD-Device-Report-${code}-${date}`;
}

function cleanValue(value: string | number | null | undefined) {
  return value == null || !String(value).trim() || /^unknown$/i.test(String(value)) ? 'Not available' : String(value);
}

export function friendlyAction(action: string | null | undefined) {
  const value = (action ?? '').toLowerCase();
  for (const category of ['Monitor', 'Maintain', 'Troubleshoot', 'Escalate']) {
    if (value.includes(category.toLowerCase())) return friendlyActionCategory(category);
  }
  return cleanValue(action);
}

export function getSessionDate(session: SessionSummary) {
  return session.created_at ?? session.display_date ?? null;
}

export function sortSessionsNewestFirst(sessions: SessionSummary[]) {
  const timestamp = (session: SessionSummary) => Date.parse(getSessionDate(session) ?? '') || 0;
  return [...sessions].sort((a, b) => timestamp(b) - timestamp(a));
}

const kv = (label: string, value: string) => `${label} : ${value}`;
const size = (value: unknown) => validReading(value) === null ? 'Not available' : `${(value as number).toFixed(1)} GB`;
type Volume = NonNullable<HardwareStats['all_disks']>[number];

function volumeUsage(volumes: Volume[]) {
  if (!volumes.length) return 'Not available';
  const complete = volumes.every(v => validReading(v.total_gb) !== null && v.total_gb > 0 && validReading(v.used_gb) !== null && v.used_gb <= v.total_gb);
  if (!complete) return 'Not available (one or more volume readings are missing or invalid)';
  const total = volumes.reduce((sum, v) => sum + v.total_gb, 0);
  const used = volumes.reduce((sum, v) => sum + v.used_gb, 0);
  return `${size(used)} used / ${size(total - used)} free (${size(total)} mounted capacity; ${(used / total * 100).toFixed(1)}% in use)`;
}

function storageLines(stats: HardwareStats | null) {
  const drives = stats?.storage_drives ?? [];
  const entries: ReportEntry[] = [];
  let volumes: Volume[] = [];
  drives.forEach(drive => {
    const matched = drive.volumes?.length ? drive.volumes : (stats?.all_disks ?? []).filter(v => drive.disk_index != null && v.disk_index === drive.disk_index);
    volumes.push(...matched);
    entries.push({
      title: `${drive.disk_index == null ? 'Drive' : `Disk ${drive.disk_index}`}: ${cleanValue(drive.model)}`,
      fields: [
        field('Drive type', cleanValue(drive.type || drive.media_type)),
        field('Physical capacity', size(drive.size_gb)),
        field('Reported drive status', drive.is_failing_smart ? 'Drive-health warning reported' : cleanValue(drive.status)),
        field('Mounted-volume usage', volumeUsage(matched)),
        ...matched.map(v => field(cleanValue(v.drive || v.mountpoint), `${volumeUsage([v])}; ${cleanValue(v.fstype)}`)),
      ],
    });
  });
  if (stats?.all_disks?.length) volumes = stats.all_disks;
  if (!volumes.length && !drives.length && stats?.disk) {
    volumes = [{ ...stats.disk, used_gb: stats.disk.used_gb as number, drive: 'Primary volume', mountpoint: '', fstype: '' }];
  }
  if (!drives.length) entries.push({
    title: 'Mounted volumes',
    notes: ['Physical-drive capacity and status were not recorded.'],
    fields: volumes.map(v => field(cleanValue(v.drive || v.mountpoint), volumeUsage([v]))),
  });
  const completePhysical = drives.length > 0 && drives.every(d => validReading(d.size_gb) !== null && d.size_gb > 0);
  return {
    capacity: completePhysical ? size(drives.reduce((sum, d) => sum + d.size_gb, 0)) : 'Not available',
    usage: volumeUsage(volumes),
    entries,
    notes: ['Reported status is not a comprehensive drive-health test. Physical capacity is not the same as mounted file space.'],
  };
}

export type ReportField = { label: string; value: string };
export type ReportEntry = { title: string; fields: ReportField[]; notes?: string[]; status?: string };
export type ReportSection = { title: string; description?: string; fields?: ReportField[]; notes?: string[]; entries?: ReportEntry[] };
export type ReportDocument = { title: string; subtitle: string; sections: ReportSection[]; footer: string };
export type ReportInput = {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  sessions: SessionSummary[];
  selectedSession?: SessionSummary | null;
  historyUnavailable: boolean;
  generatedAt?: Date;
  includeHistory?: boolean;
};
const field = (label: string, value: string): ReportField => ({ label, value });

function savedReadings(title: string, readings: DiagnosticReading[] | undefined): ReportSection {
  const expanded = expandDiagnosticReadings(readings ?? []);
  if (!expanded.length) return { title, notes: ['Detailed readings were not saved for this check.'] };
  return {
    title,
    fields: [
      field('Readings needing review', String(expanded.filter(r => readingTone(getReadingPresentation(r).status) === 'attention').length)),
      field('Unavailable readings', String(expanded.filter(r => readingTone(getReadingPresentation(r).status) === 'unknown').length)),
    ],
    description: 'Unavailable readings are unverified, not confirmed hardware faults.',
    entries: expanded.map(reading => {
      const view = getReadingPresentation(reading);
      return {
        title: view.label,
        status: readingTone(view.status),
        fields: [
          field('Reading', view.value),
          field('Status', readingStatusLabel(reading) || 'Status not recorded'),
          field('Evidence', readingEvidenceLabel(reading)),
          field('Recorded', formatDate(reading.captured_at)),
          ...(view.meaning ? [field('Explanation', view.meaning)] : []),
          ...(reading.window_kind ? [field('Query window', reading.window_kind)] : []),
          ...(reading.window_start || reading.window_end ? [
            field('Window start', formatDate(reading.window_start)), field('Window end', formatDate(reading.window_end)),
          ] : []),
        ],
      };
    }),
  };
}

export function buildReportDocument({
  stats, dashboard, hardwareUpdatedAt, sessions, selectedSession, historyUnavailable,
  generatedAt = new Date(), includeHistory = true,
}: ReportInput): ReportDocument {
  const sorted = sortSessionsNewestFirst(sessions);
  const selected = selectedSession === undefined
    ? sorted[0] ?? dashboard.last_saved_session ?? dashboard.last_diagnosis : selectedSession;
  const storage = storageLines(stats);
  const sections: ReportSection[] = [{
    title: 'Report details',
    fields: [field('Generated', formatDate(generatedAt)), field('System name', cleanValue(stats?.device_name)),
      field('Operating system', cleanValue(stats?.os_version))],
    notes: historyUnavailable ? ['Check history could not be loaded. This report may be incomplete.'] : [],
  }];
  if (selected) {
    const overview = getCheckOverview(selected);
    const followUp = selected.resolution_proof?.length
      ? getFollowUpReadings(selected.proof, selected.resolution_proof) : [];
    const latest = followUp.length ? { ...selected, resolution_proof: followUp } : selected;
    sections.push({
      title: 'Selected saved check',
      fields: [
        field('Saved check ID', selected.session_code || selected.session_id),
        field('Check date', formatDate(getSessionDate(selected))),
        field('Selected check', getSessionCheckLabel(selected)),
        field('Trigger / symptom', cleanValue(selected.symptom_type)),
        field('Original finding', overview.summary || overview.title),
        field('Original next step', friendlyAction(selected.action_category)),
        field('Evidence availability', getCheckEvidenceLabel(selected)),
        field('Latest follow-up', getCheckStatus(latest).label),
        field('Follow-up time', formatDate(selected.resolution_checked_at)),
      ],
      notes: [
        ...(getCheckCoverageNotice(selected) ? [getCheckCoverageNotice(selected)!] : []),
        ...(followUp.length ? [] : ['No detailed follow-up readings are saved. A stored status alone does not verify improvement.']),
      ],
    }, savedReadings('Original saved readings', selected.proof));
    if (followUp.length) sections.push({
      ...savedReadings('Latest saved follow-up readings', followUp),
      notes: [getLatestCheckSummary(latest)],
    });
    if (selected.last_action_summary) sections.push({
      title: 'Recorded action',
      fields: [field('Last recorded action', selected.last_action_summary)],
      notes: ['An action record does not, by itself, confirm a repair.'],
    });
  } else sections.push({ title: 'Selected saved check', notes: ['No saved check is available. Run a device check to save a result.'] });

  const ramTotal = validReading(stats?.ram?.total_gb);
  const ramPercent = validReading(stats?.ram?.usage_percent);
  const ram = ramTotal !== null && ramTotal > 0
    ? `${size(ramTotal)} total${ramPercent !== null && ramPercent <= 100 ? ` (${ramPercent.toFixed(1)}% in use)` : '; usage not available'}` : 'Not available';
  sections.push({
    title: 'Hardware snapshot',
    description: 'Hardware values below are a separate snapshot, not replacements for the original saved check readings.',
    fields: [
      field('Hardware snapshot time', formatDate(hardwareUpdatedAt)),
      field('Processor (CPU)', cleanValue(stats?.cpu?.name)), field('Memory (RAM)', ram),
      field('Graphics (GPU)', cleanValue(stats?.gpu?.name)),
      field('Physical storage capacity', storage.capacity), field('Mounted-volume usage', storage.usage),
    ],
  }, {
    title: 'Storage details',
    // Keep each physical drive and its mounted volumes together without parsing presentation text.
    entries: storage.entries,
    notes: storage.notes,
  });

  if (includeHistory) sections.push({
    title: 'Saved history summary',
    description: 'Optional context from saved history; these totals do not describe only the selected check.',
    fields: [
      field('Summary recorded', formatDate(dashboard.server_time)),
      field('Total saved checks', String(dashboard.totals.total_sessions)),
      field('Checks this month', String(dashboard.totals.this_month_count)),
      field('Recurring patterns', String(dashboard.recurring_issues_count)),
      field('Checks with warning signs', String(dashboard.warning_signs_active_count)),
      field('Checks marked escalate', String(dashboard.totals.escalated_count)),
    ],
    notes: ['Warning-sign count refers to saved warning-sign entries, not the current scan findings.'],
  }, {
    title: 'Recent saved checks (up to 8; full findings)',
    entries: sorted.slice(0, 8).map(session => ({
      title: `${session.session_code || session.session_id} - ${getSessionCheckLabel(session)}`,
      fields: [
        field('Check date', formatDate(getSessionDate(session))),
        field('Saved check ID', session.session_code || session.session_id),
        field('Trigger / symptom', cleanValue(session.symptom_type)),
        field('Original finding', getCheckOverview(session).summary || getCheckOverview(session).title),
        field('Original next step', friendlyAction(session.action_category)),
        field('Follow-up', getCheckStatus(session.resolution_proof?.length
          ? { ...session, resolution_proof: getFollowUpReadings(session.proof, session.resolution_proof) } : session).label),
      ],
    })),
    notes: sorted.length ? [] : ['No saved checks loaded.'],
  });
  return {
    title: 'RigMD Device Check Report',
    subtitle: selected ? getSessionCheckLabel(selected) : 'Device information',
    sections,
    footer: 'RigMD provides device checkup guidance and device-reading-based triage. It does not replace in-person professional hardware diagnosis or repair.',
  };
}

export function reportDocumentText(report: ReportDocument) {
  const fields = (rows: ReportField[] = []) => rows.map(row => kv(row.label, row.value));
  return [report.title, report.subtitle,
    ...report.sections.flatMap(section => [
      '', section.title.toUpperCase(),
      ...(section.description ? [section.description] : []),
      ...fields(section.fields),
      ...(section.notes ?? []),
      ...(section.entries ?? []).flatMap((entry, index) => [
        '', `${index + 1}. ${entry.title}`, ...fields(entry.fields), ...(entry.notes ?? []),
      ]),
    ]),
    '', 'ABOUT THIS REPORT', report.footer,
  ].join('\n');
}

export function buildReportText(input: ReportInput) {
  return reportDocumentText(buildReportDocument(input));
}
