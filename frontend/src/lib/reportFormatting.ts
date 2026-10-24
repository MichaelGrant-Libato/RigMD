import { friendlyActionCategory, expandDiagnosticReadings, getReadingPresentation, readingEvidenceLabel, readingStatusLabel, type DiagnosticReading } from './diagnosticLanguage.ts';
import { getCheckOverview, getCheckStatus, getCheckEvidenceLabel, getCheckCoverageNotice, getLatestCheckSummary } from './checkPresentation.ts';
import { getSessionCheckLabel } from './pastChecksFormatting.ts';
import { readingTone } from './diagnosticReadingVisuals.ts';
import { validReading } from './storageTelemetry.ts';
import type { DashboardSummary, HardwareStats, SessionSummary } from '../types/rigmd.ts';

export function formatDate(value: string | Date | null | undefined) {
  if (!value) return 'Not available';
  const date = value instanceof Date ? value : new Date(value);
  if (!Number.isFinite(date.getTime())) return 'Not available';
  return date.toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
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

const kv = (label: string, value: string) => `${label.padEnd(26)}: ${value}`;
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
  const records: string[] = [];
  let volumes: Volume[] = [];
  drives.forEach((drive, index) => {
    const matched = drive.volumes?.length ? drive.volumes : (stats?.all_disks ?? []).filter(v => drive.disk_index != null && v.disk_index === drive.disk_index);
    volumes.push(...matched);
    records.push(`[${index + 1}] ${drive.disk_index == null ? 'Drive' : `Disk ${drive.disk_index}`}: ${cleanValue(drive.model)}`,
      kv('    Drive type', cleanValue(drive.type || drive.media_type)),
      kv('    Physical capacity', size(drive.size_gb)),
      kv('    Reported drive status', drive.is_failing_smart ? 'Drive-health warning reported' : cleanValue(drive.status)),
      kv('    Mounted-volume usage', volumeUsage(matched)));
    matched.forEach(v => records.push(`    ${cleanValue(v.drive || v.mountpoint)}: ${volumeUsage([v])}; ${cleanValue(v.fstype)}`));
  });
  // A complete volume inventory is preferred over only the volumes matched to physical drives.
  if (stats?.all_disks?.length) volumes = stats.all_disks;
  if (!volumes.length && !drives.length && stats?.disk) {
    volumes = [{ ...stats.disk, used_gb: stats.disk.used_gb as number, drive: 'Primary volume', mountpoint: '', fstype: '' }];
  }
  if (!drives.length) {
    records.push('Physical-drive capacity and status were not recorded.');
    volumes.forEach(v => records.push(`${cleanValue(v.drive || v.mountpoint)}: ${volumeUsage([v])}`));
  }
  const completePhysical = drives.length > 0 && drives.every(d => validReading(d.size_gb) !== null && d.size_gb > 0);
  return {
    capacity: completePhysical ? size(drives.reduce((sum, d) => sum + d.size_gb, 0)) : 'Not available',
    usage: volumeUsage(volumes),
    records: [...records, 'Reported status is not a comprehensive drive-health test. Physical capacity is not the same as mounted file space.'],
  };
}

function savedReadings(title: string, readings: DiagnosticReading[] | undefined) {
  const expanded = expandDiagnosticReadings(readings ?? []);
  if (!expanded.length) return [title, 'Detailed readings were not saved for this check.'];
  const attention = expanded.filter(r => readingTone(getReadingPresentation(r).status) === 'attention').length;
  const unavailable = expanded.filter(r => readingTone(getReadingPresentation(r).status) === 'unknown').length;
  return [title, kv('Readings needing review', String(attention)), kv('Unavailable readings', String(unavailable)),
    'Unavailable readings are unverified, not confirmed hardware faults.',
    ...expanded.flatMap((reading, index) => {
      const view = getReadingPresentation(reading);
      return ['', `[${index + 1}] ${view.label}: ${view.value}`,
        kv('    Status', readingStatusLabel(reading) || 'Status not recorded'),
        kv('    Evidence', readingEvidenceLabel(reading)),
        kv('    Recorded', formatDate(reading.captured_at)),
        ...(view.meaning ? [kv('    Explanation', view.meaning)] : []),
        ...(reading.window_kind ? [kv('    Query window', reading.window_kind)] : []),
        ...(reading.window_start || reading.window_end ? [kv('    Window start', formatDate(reading.window_start)), kv('    Window end', formatDate(reading.window_end))] : []),
      ];
    })];
}

export function buildReportText({ stats, dashboard, hardwareUpdatedAt, sessions, selectedSession, historyUnavailable, generatedAt = new Date() }: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  sessions: SessionSummary[];
  selectedSession?: SessionSummary | null;
  historyUnavailable: boolean;
  generatedAt?: Date;
}) {
  const sorted = sortSessionsNewestFirst(sessions);
  const selected = selectedSession === undefined
    ? sorted[0] ?? dashboard.last_saved_session ?? dashboard.last_diagnosis : selectedSession;
  const storage = storageLines(stats);
  const sectionDivider = '-'.repeat(64);
  const divider = '='.repeat(64);
  const checkLines: string[] = [];
  if (selected) {
    const overview = getCheckOverview(selected);
    checkLines.push(kv('Saved check ID', selected.session_code || selected.session_id),
      kv('Check date', formatDate(getSessionDate(selected))),
      kv('Selected check', getSessionCheckLabel(selected)),
      kv('Trigger / symptom', cleanValue(selected.symptom_type)),
      kv('Original finding', overview.summary || overview.title),
      kv('Original next step', friendlyAction(selected.action_category)),
      kv('Evidence availability', getCheckEvidenceLabel(selected)),
      ...(getCheckCoverageNotice(selected) ? [getCheckCoverageNotice(selected)!] : []),
      kv('Latest follow-up', getCheckStatus(selected).label),
      kv('Follow-up time', formatDate(selected.resolution_checked_at)),
      '', ...savedReadings('ORIGINAL SAVED READINGS', selected.proof));
    if (selected.resolution_proof?.length) checkLines.push('', ...savedReadings('LATEST SAVED FOLLOW-UP READINGS', selected.resolution_proof), '', getLatestCheckSummary(selected));
    else checkLines.push('', 'No detailed follow-up readings are saved. A stored status alone does not verify improvement.');
    if (selected.last_action_summary) checkLines.push('', kv('Last recorded action', selected.last_action_summary), 'An action record does not, by itself, confirm a repair.');
  } else checkLines.push('No saved check is available. Run a device check to save a result.');

  const ramTotal = validReading(stats?.ram?.total_gb);
  const ramPercent = validReading(stats?.ram?.usage_percent);
  const ram = ramTotal !== null && ramTotal > 0
    ? `${size(ramTotal)} total${ramPercent !== null && ramPercent <= 100 ? ` (${ramPercent.toFixed(1)}% in use)` : '; usage not available'}` : 'Not available';
  return [divider, 'RigMD Device Check Report', divider,
    kv('Generated', formatDate(generatedAt)),
    ...(historyUnavailable ? ['Check history could not be loaded. This report may be incomplete.'] : []),
    kv('System name', cleanValue(stats?.device_name)), kv('Operating system', cleanValue(stats?.os_version)),
    kv('Hardware snapshot time', formatDate(hardwareUpdatedAt)),
    'Hardware values below are a separate snapshot, not replacements for the original saved check readings.',
    '', sectionDivider, 'HARDWARE SNAPSHOT', sectionDivider,
    kv('Processor (CPU)', cleanValue(stats?.cpu?.name)), kv('Memory (RAM)', ram), kv('Graphics (GPU)', cleanValue(stats?.gpu?.name)),
    kv('Physical storage capacity', storage.capacity), kv('Mounted-volume usage', storage.usage),
    '', sectionDivider, 'STORAGE DETAILS', sectionDivider, ...storage.records,
    '', sectionDivider, 'SELECTED SAVED CHECK', sectionDivider, ...checkLines,
    '', sectionDivider, 'SAVED HISTORY SUMMARY', sectionDivider,
    kv('Summary recorded', formatDate(dashboard.server_time)),
    kv('Total saved checks', String(dashboard.totals.total_sessions)),
    kv('Checks this month', String(dashboard.totals.this_month_count)),
    kv('Recurring patterns', String(dashboard.recurring_issues_count)),
    kv('Checks with warning signs', String(dashboard.warning_signs_active_count)),
    'Warning-sign count refers to saved warning-sign entries, not the current scan findings.',
    kv('Checks marked escalate', String(dashboard.totals.escalated_count)),
    '', 'Recent saved checks (up to 8; full findings):',
    ...(sorted.length ? sorted.slice(0, 8).flatMap((session, index) => [
      '', `[${index + 1}] ${getSessionCheckLabel(session)}`, kv('    Check date', formatDate(getSessionDate(session))),
      kv('    Saved check ID', session.session_code || session.session_id),
      kv('    Trigger / symptom', cleanValue(session.symptom_type)),
      kv('    Original finding', getCheckOverview(session).summary || getCheckOverview(session).title),
      kv('    Original next step', friendlyAction(session.action_category)), kv('    Follow-up', getCheckStatus(session).label),
    ]) : ['No saved checks loaded.']),
    '', divider, 'NOTE: RigMD provides device checkup guidance and device-reading-based triage.',
    'It does not replace in-person professional hardware diagnosis or repair.', divider,
  ].join('\n');
}
