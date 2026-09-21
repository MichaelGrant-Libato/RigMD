import { useCallback, useEffect, useMemo, useState } from 'react';
import { motion } from 'motion/react';
import { createPortal } from 'react-dom';
import {
  Clipboard,
  Download,
  FileText,
  Mail,
  Printer,
  RefreshCw,
  Send,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import { buttonTap, cardFadeUp, cardTransition, pageFade, pageTransition } from '../lib/motion';
import { apiFetch } from '../lib/api';
import type { DashboardSummary, HardwareStats, SessionSummary } from '../types/rigmd';

interface ShareReportViewProps {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  onStartNewDiagnosis?: () => void;
}

function formatDate(value: string | Date | null | undefined) {
  if (!value) return 'Not available';

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return 'Not available';

  return date.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
}

function cleanValue(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') return 'Not available';
  if (String(value).toLowerCase() === 'unknown') return 'Not available';
  return String(value);
}

function friendlyAction(action: string | null | undefined) {
  const value = (action ?? '').toLowerCase();
  if (value.includes('monitor')) return 'Keep an eye on it';
  if (value.includes('maintain')) return 'Do simple care';
  if (value.includes('troubleshoot')) return 'Try a safe fix';
  if (value.includes('escalate')) return 'Get help';
  return cleanValue(action);
}

function friendlyResult(result: string | null | undefined) {
  if ((result ?? '').trim().toLowerCase() === 'no active issue detected') {
    return 'No active problem found';
  }

  return cleanValue(result);
}

function getSessionDate(session: SessionSummary) {
  return session.created_at ?? session.display_date ?? null;
}

function sortSessionsNewestFirst(sessions: SessionSummary[]) {
  return [...sessions].sort((a, b) => {
    const first = new Date(getSessionDate(a) ?? 0).getTime();
    const second = new Date(getSessionDate(b) ?? 0).getTime();
    return second - first;
  });
}

function formatStorageSize(sizeGb: number | null | undefined) {
  if (typeof sizeGb !== 'number' || Number.isNaN(sizeGb)) return 'Not available';
  return sizeGb >= 1000
    ? `${(sizeGb / 1024).toFixed(1)} TB`
    : `${Math.round(sizeGb * 10) / 10} GB`;
}

interface ResolvedDriveInfo {
  model: string;
  type: string;
  size_gb: number;
  interface?: string;
  disk_index?: number | null;
  used_gb?: number | null;
  usage_percent?: number | null;
  is_failing_smart?: boolean;
  status?: string | null;
  volumes?: Array<{
    drive: string;
    mountpoint: string;
    fstype?: string;
    disk_index?: number | null;
    total_gb?: number;
    used_gb?: number;
    usage_percent?: number;
  }>;
}

function resolveAllStorageDrives(stats: HardwareStats | null): ResolvedDriveInfo[] {
  if (!stats) return [];

  // 1. Primary: Use physical storage_drives if provided
  if (stats.storage_drives && stats.storage_drives.length > 0) {
    return stats.storage_drives.map((drive, idx) => {
      const driveVolumes =
        drive.volumes && drive.volumes.length > 0
          ? drive.volumes
          : (stats.all_disks ?? []).filter(
              (d) => drive.disk_index != null && d.disk_index === drive.disk_index
            );

      let usedGb = drive.used_gb;
      let usagePercent = drive.usage_percent;
      if ((usedGb == null || usagePercent == null) && driveVolumes.length > 0) {
        const totalVol = driveVolumes.reduce((acc, v) => acc + (v.total_gb || 0), 0);
        const usedVol = driveVolumes.reduce((acc, v) => acc + (v.used_gb || 0), 0);
        if (usedVol > 0) usedGb = Math.round(usedVol * 100) / 100;
        if (totalVol > 0) usagePercent = Math.round((usedVol / totalVol) * 1000) / 10;
      }

      return {
        model: drive.model,
        type: drive.type || drive.media_type || stats.storage_type || 'SSD',
        size_gb: drive.size_gb,
        interface: drive.interface,
        disk_index: drive.disk_index ?? idx,
        used_gb: usedGb,
        usage_percent: usagePercent,
        is_failing_smart: drive.is_failing_smart,
        status: drive.status,
        volumes: driveVolumes,
      };
    });
  }

  // 2. Secondary fallback: Group all_disks by disk_index
  if (stats.all_disks && stats.all_disks.length > 0) {
    const byIndex = new Map<number | string, typeof stats.all_disks>();
    stats.all_disks.forEach((disk, i) => {
      const key = disk.disk_index != null ? disk.disk_index : `disk-${i}`;
      const group = byIndex.get(key) ?? [];
      group.push(disk);
      byIndex.set(key, group);
    });

    return Array.from(byIndex.entries()).map(([key, volumes], idx) => {
      const diskIndex = typeof key === 'number' ? key : idx;
      const totalGb = volumes.reduce((acc, v) => acc + (v.total_gb || 0), 0);
      const usedGb = volumes.reduce((acc, v) => acc + (v.used_gb || 0), 0);
      const usagePercent = totalGb > 0 ? Math.round((usedGb / totalGb) * 1000) / 10 : undefined;
      const driveLetter = volumes.map((v) => v.drive).filter(Boolean).join(', ');

      return {
        model: driveLetter ? `Storage Drive (${driveLetter})` : `Storage Drive ${idx + 1}`,
        type: stats.storage_type || 'SSD',
        size_gb: totalGb,
        disk_index: diskIndex,
        used_gb: usedGb,
        usage_percent: usagePercent,
        status: 'Healthy (OK)',
        volumes,
      };
    });
  }

  // 3. Last fallback: Single stats.disk summary
  if (stats.disk?.total_gb) {
    return [
      {
        model: 'Primary Storage Drive',
        type: stats.storage_type || 'SSD',
        size_gb: stats.disk.total_gb,
        disk_index: 0,
        used_gb: stats.disk.used_gb,
        usage_percent: stats.disk.usage_percent,
        status: 'Healthy (OK)',
        volumes: [],
      },
    ];
  }

  return [];
}

function padCol(val: string, width: number): string {
  if (val.length > width) return val.slice(0, Math.max(0, width - 1)) + '…';
  return val.padEnd(width);
}

function kv(label: string, value: string, pad = 24): string {
  return `${label.padEnd(pad)}: ${value}`;
}

function buildReportText({
  stats,
  dashboard,
  hardwareUpdatedAt,
  sessions,
  historyUnavailable,
}: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  sessions: SessionSummary[];
  historyUnavailable: boolean;
}) {
  const latest = sessions[0] ?? dashboard.last_saved_session ?? dashboard.last_diagnosis;
  const recentSessions = sessions.slice(0, 8);
  const drives = resolveAllStorageDrives(stats);

  // Storage aggregation
  const totalStorageGb = drives.length > 0
    ? drives.reduce((acc, d) => acc + (d.size_gb || 0), 0)
    : (stats?.disk?.total_gb ?? 0);

  const totalUsedGb = drives.length > 0
    ? drives.reduce((acc, d) => acc + (d.used_gb || 0), 0)
    : (stats?.disk?.used_gb ?? 0);

  const totalUsagePercent = totalStorageGb > 0
    ? (totalUsedGb / totalStorageGb) * 100
    : (stats?.disk?.usage_percent ?? null);

  const freeStorageGb = Math.max(0, totalStorageGb - totalUsedGb);

  let storagePoolSummary = formatStorageSize(totalStorageGb);
  if (drives.length > 1) {
    storagePoolSummary += ` across ${drives.length} drives`;
  }
  if (typeof totalUsagePercent === 'number' && !Number.isNaN(totalUsagePercent)) {
    const usedPart = totalUsedGb > 0 ? ` (${formatStorageSize(totalUsedGb)} used / ${formatStorageSize(freeStorageGb)} free)` : '';
    storagePoolSummary += ` • ${totalUsagePercent.toFixed(1)}% in use${usedPart}`;
  }

  // OS display string
  const osString = cleanValue(stats?.os_version)
    .replace(/^Microsoft\s+/i, '')
    .replace(/\s*\([\d.]+\)$/, '');

  // CPU display string
  const cpuString = cleanValue(stats?.cpu?.name).replace(/\s+\d+-Core Processor$/i, '');

  // RAM display string
  let ramString = 'Not available';
  if (stats?.ram?.total_gb) {
    ramString = `${stats.ram.total_gb} GB total`;
    if (typeof stats.ram.usage_percent === 'number' && !Number.isNaN(stats.ram.usage_percent)) {
      ramString += ` (${stats.ram.usage_percent.toFixed(1)}% in use)`;
    }
  }

  // Storage Drive block
  const driveLines: string[] = [];
  if (drives.length > 0) {
    drives.forEach((disk, index) => {
      const diskId = disk.disk_index != null ? `Disk ${disk.disk_index}` : `Disk ${index}`;
      const diskModel = cleanValue(disk.model);
      const diskType = cleanValue(disk.type || 'SSD');
      const diskCapacity = formatStorageSize(disk.size_gb);

      const driveLetters = (disk.volumes ?? [])
        .map((v) => v.drive || v.mountpoint)
        .filter(Boolean)
        .join(', ');

      const usedSpaceStr =
        typeof disk.used_gb === 'number' ? formatStorageSize(disk.used_gb) : null;
      const freeSpaceStr =
        typeof disk.size_gb === 'number' && typeof disk.used_gb === 'number'
          ? formatStorageSize(Math.max(0, disk.size_gb - disk.used_gb))
          : null;
      const usagePctStr =
        typeof disk.usage_percent === 'number' && !Number.isNaN(disk.usage_percent)
          ? `${disk.usage_percent.toFixed(1)}% in use`
          : null;

      let capUsageLine = diskCapacity;
      if (usedSpaceStr && freeSpaceStr) {
        capUsageLine = `${usedSpaceStr} used / ${freeSpaceStr} free (${diskCapacity} total${usagePctStr ? ` • ${usagePctStr}` : ''})`;
      } else if (usagePctStr) {
        capUsageLine = `${diskCapacity} (${usagePctStr})`;
      }

      const healthStatus =
        disk.status ||
        (disk.is_failing_smart ? 'Warning / Failing S.M.A.R.T.' : 'Healthy (OK)');

      driveLines.push(`[${index + 1}] ${diskId}: ${diskModel}`);
      driveLines.push(kv('    Drive Type', diskType));
      driveLines.push(kv('    Drive Letter', driveLetters || 'Unmounted / System Reserved'));
      driveLines.push(kv('    Capacity & Usage', capUsageLine));
      driveLines.push(kv('    Status (S.M.A.R.T.)', healthStatus));

      if (disk.volumes && disk.volumes.length > 0) {
        if (disk.volumes.length === 1) {
          const v = disk.volumes[0];
          const letter = v.drive || v.mountpoint || 'Volume';
          const vTotal = formatStorageSize(v.total_gb);
          const vUsed = typeof v.used_gb === 'number' ? `${formatStorageSize(v.used_gb)} used` : '';
          const vFree =
            typeof v.total_gb === 'number' && typeof v.used_gb === 'number'
              ? `${formatStorageSize(Math.max(0, v.total_gb - v.used_gb))} free`
              : '';
          const vPct =
            typeof v.usage_percent === 'number' ? `${v.usage_percent.toFixed(1)}% in use` : '';
          const vFs = v.fstype || '';
          const details = [vTotal, vUsed, vFree, vPct, vFs].filter(Boolean).join(', ');
          driveLines.push(kv('    Partitions', `${letter} (${details})`));
        } else {
          driveLines.push('    Partitions          :');
          disk.volumes.forEach((v) => {
            const letter = v.drive || v.mountpoint || 'Volume';
            const vTotal = formatStorageSize(v.total_gb);
            const vUsed = typeof v.used_gb === 'number' ? `${formatStorageSize(v.used_gb)} used` : '';
            const vFree =
              typeof v.total_gb === 'number' && typeof v.used_gb === 'number'
                ? `${formatStorageSize(Math.max(0, v.total_gb - v.used_gb))} free`
                : '';
            const vPct =
              typeof v.usage_percent === 'number' ? `${v.usage_percent.toFixed(1)}% in use` : '';
            const vFs = v.fstype || '';
            const details = [vTotal, vUsed, vFree, vPct, vFs].filter(Boolean).join(', ');
            driveLines.push(`      • ${letter} (${details})`);
          });
        }
      }

      if (index < drives.length - 1) {
        driveLines.push('');
      }
    });
  } else {
    driveLines.push('No storage drives detected or telemetry unavailable.');
  }

  // Latest check formatting
  const latestLines: string[] = [];
  if (latest) {
    const rawSymptom = latest.symptom_type?.trim();
    const triggerText =
      rawSymptom && rawSymptom.toLowerCase() !== 'not available' && rawSymptom.toLowerCase() !== 'unknown'
        ? cleanValue(rawSymptom)
        : 'Routine System Check';

    latestLines.push(
      kv('Check Date', formatDate(getSessionDate(latest))),
      kv('Trigger / Symptom', triggerText),
      kv('Diagnostic Finding', friendlyResult(latest.diagnosed_category)),
      kv('Recommended Action', friendlyAction(latest.action_category)),
      kv('Match Strength', cleanValue(latest.confidence_label))
    );
  } else {
    latestLines.push(
      kv('Check Date', 'No saved checks yet'),
      kv('Status', 'Run a Device Check to record system triage')
    );
  }

  // Recent history table
  const historyTableLines: string[] = [];
  if (recentSessions.length > 0) {
    historyTableLines.push(
      `  ${padCol('Date', 21)} | ${padCol('Trigger / Symptom', 23)} | ${padCol('Diagnostic Finding', 24)} | Recommended Action`,
      '  ----------------------+-------------------------+--------------------------+-----------------------'
    );
    recentSessions.forEach((session) => {
      const dateStr = formatDate(getSessionDate(session));
      const rawSym = session.symptom_type?.trim();
      const symStr =
        rawSym && rawSym.toLowerCase() !== 'not available' && rawSym.toLowerCase() !== 'unknown'
          ? cleanValue(rawSym)
          : 'Routine System Check';
      const findStr = friendlyResult(session.diagnosed_category);
      const actStr = friendlyAction(session.action_category);

      historyTableLines.push(
        `  ${padCol(dateStr, 21)} | ${padCol(symStr, 23)} | ${padCol(findStr, 24)} | ${actStr}`
      );
    });
  } else {
    historyTableLines.push('  No saved checks yet.');
  }

  const divider = '='.repeat(80);
  const sectionDivider = '-'.repeat(80);

  const lines = [
    divider,
    '                           RigMD Device Check Report',
    divider,
    kv('Generated', formatDate(new Date())),
    ...(historyUnavailable ? ['Check history could not be loaded. This report may be incomplete.'] : []),
    kv('System Name', cleanValue(stats?.device_name)),
    kv('Operating System', osString),
    kv('Hardware Scan Time', formatDate(hardwareUpdatedAt)),
    '',
    sectionDivider,
    'CORE HARDWARE SUMMARY',
    sectionDivider,
    kv('Processor (CPU)', cpuString),
    kv('Memory (RAM)', ramString),
    kv('Graphics (GPU)', cleanValue(stats?.gpu?.name)),
    kv('Storage Capacity', storagePoolSummary),
    '',
    sectionDivider,
    `STORAGE DRIVES (${drives.length} Detected)`,
    sectionDivider,
    ...driveLines,
    '',
    sectionDivider,
    'LATEST SYSTEM CHECK',
    sectionDivider,
    ...latestLines,
    '',
    sectionDivider,
    'CHECK HISTORY & HEALTH SUMMARY',
    sectionDivider,
    kv('Total Saved Checks', String(dashboard.totals.total_sessions || sessions.length)),
    kv('Checks This Month', String(dashboard.totals.this_month_count)),
    kv('Recurring Issues', String(dashboard.recurring_issues_count)),
    kv('Active Warnings', String(dashboard.warning_signs_active_count)),
    kv('Escalations Needed', String(dashboard.totals.escalated_count)),
    '',
    'Recent Saved Checks:',
    ...historyTableLines,
    '',
    divider,
    'NOTE: RigMD provides device checkup guidance and telemetry-based triage.',
    '      It does not replace in-person professional hardware diagnosis or repair.',
    divider,
  ];

  return lines.join('\n');
}

function downloadTextFile(filename: string, text: string) {
  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

export default function ShareReportView({
  stats,
  dashboard,
  hardwareUpdatedAt,
  onStartNewDiagnosis,
}: ShareReportViewProps) {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const fetchSessions = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);

    try {
      const response = await apiFetch('/api/diagnosis/sessions');

      if (!response.ok) {
        throw new Error(`Server returned ${response.status}`);
      }

      const data = await response.json();
      const rows = Array.isArray(data) ? data : Array.isArray(data?.sessions) ? data.sessions : [];
      setSessions(sortSessionsNewestFirst(rows as SessionSummary[]));
    } catch {
      setSessions([]);
      setLoadError('Saved checks are not available right now.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchSessions();
  }, [fetchSessions]);

  const reportText = useMemo(
    () =>
      buildReportText({
        stats,
        dashboard,
        hardwareUpdatedAt,
        sessions,
        historyUnavailable: Boolean(loadError),
      }),
    [dashboard, hardwareUpdatedAt, sessions, stats, loadError]
  );

  const latestSession = sessions[0] ?? dashboard.last_saved_session ?? dashboard.last_diagnosis;
  const reportFileName = `RigMD-Device-Report-${new Date().toISOString().slice(0, 10)}.txt`;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(reportText);
      setMessage({ type: 'success', text: 'Report copied. You can paste it into chat, email, or a support ticket.' });
    } catch {
      setMessage({ type: 'error', text: 'Copy did not work on this device. Download the report instead.' });
    }
  };

  const handleDownload = () => {
    downloadTextFile(reportFileName, reportText);
    setMessage({ type: 'success', text: 'Report downloaded as a text file.' });
  };

  const handleEmailDraft = () => {
    const subject = encodeURIComponent('RigMD Device Check Report');
    const body = encodeURIComponent(reportText);
    window.location.href = `mailto:?subject=${subject}&body=${body}`;
  };

  const handlePrint = () => {
    window.print();
  };

  return (
    <>
      {createPortal(<article className="rigmd-print-report" aria-label="Device check report"><h1>RigMD Device Check Report</h1><pre>{reportText.split('\n').slice(1).join('\n')}</pre></article>, document.body)}
      <TopHeader
        title="Share Report"
        subtitle="Review your results, then print or share them"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8"
      >
        <div className="mx-auto grid w-full max-w-[1360px] gap-5 xl:grid-cols-[minmax(0,1fr)_380px]">
          <div className="space-y-5">
            {(loadError || message) && (
              <div
                role="status"
                className={`rounded-xl border px-4 py-3 text-sm ${
                  message?.type === 'success'
                    ? 'border-emerald-400/25 bg-emerald-400/10 text-emerald-200'
                    : 'border-amber-400/30 bg-amber-400/10 text-amber-200'
                }`}
              >
                {message?.text || loadError}
              </div>
            )}

            <motion.section
              variants={cardFadeUp}
              transition={cardTransition}
              className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6"
            >
              <p className="mb-3 text-xs font-bold uppercase tracking-[0.16em] text-cyan-300">Report Preview</p>
              <h3 className="text-2xl font-bold text-white">Your device report</h3>
              <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">
                Includes your computer name, device information, and recent checks. Review it before sharing.
              </p>

              <div className="mt-5 grid gap-4 md:grid-cols-3">
                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Latest result</p>
                  <p className="mt-2 break-words font-bold text-white">{friendlyResult(latestSession?.diagnosed_category)}</p>
                </div>

                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">What to do</p>
                  <p className="mt-2 font-bold text-white">{friendlyAction(latestSession?.action_category)}</p>
                </div>

                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Saved checks</p>
                  <p className="mt-2 font-bold text-white">{dashboard.totals.total_sessions || sessions.length}</p>
                </div>
              </div>
            </motion.section>

            <motion.section
              variants={cardFadeUp}
              transition={cardTransition}
              className="overflow-hidden rounded-2xl border border-[var(--rigmd-border)] bg-[#101821]"
            >
              <div className="flex items-center justify-between border-b border-[var(--rigmd-border)] px-5 py-4">
                <div className="flex items-center gap-2">
                  <FileText size={17} className="text-cyan-400" />
                  <h3 className="font-bold text-white">Report Content</h3>
                </div>

                <button
                  type="button"
                  onClick={fetchSessions}
                  disabled={isLoading}
                  className="inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-3 py-2 text-xs font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300 disabled:cursor-wait disabled:opacity-70"
                >
                  <RefreshCw size={14} className={isLoading ? 'animate-spin' : ''} />
                  Refresh
                </button>
              </div>

              <pre tabIndex={0} aria-label="Report preview" className="font-sans max-h-[560px] overflow-auto whitespace-pre-wrap p-5 text-sm leading-relaxed text-slate-200">
                {reportText}
              </pre>
            </motion.section>
          </div>

          <aside className="space-y-4">
            <motion.section
              variants={cardFadeUp}
              transition={cardTransition}
              className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5"
            >
              <h3 className="font-bold text-white">Share Options</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">
                Choose how you want to send the report. Nothing is sent automatically.
              </p>

              <div className="mt-5 space-y-3">
                <motion.button
                  type="button"
                  disabled={isLoading}
                  onClick={handleCopy}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg bg-cyan-400 px-4 py-3 text-sm font-bold text-[#041014] transition hover:bg-cyan-300"
                >
                  <Clipboard size={16} />
                  {isLoading ? 'Loading report...' : 'Copy Report'}
                </motion.button>

                <motion.button
                  type="button"
                  disabled={isLoading}
                  onClick={handleDownload}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-400/30 bg-cyan-400/10 px-4 py-3 text-sm font-bold text-cyan-300 transition hover:bg-cyan-400/15"
                >
                  <Download size={16} />
                  Download Text File
                </motion.button>

                <motion.button
                  type="button"
                  disabled={isLoading}
                  onClick={handlePrint}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-3 text-sm font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300"
                >
                  <Printer size={16} />
                  Print Report
                </motion.button>

                <motion.button
                  type="button"
                  disabled={isLoading}
                  onClick={handleEmailDraft}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-3 text-sm font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300"
                >
                  <Mail size={16} />
                  Open Email Draft
                </motion.button>
              </div>
            </motion.section>

            <motion.section
              variants={cardFadeUp}
              transition={cardTransition}
              className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5"
            >
              <h3 className="font-bold text-white">Before Sharing</h3>
              <ul className="mt-3 space-y-2 text-sm leading-relaxed text-slate-400">
                <li>Review the report content first.</li>
                <li>Send it only to someone you trust.</li>
                <li>Run a new check if the Device changed since the last scan.</li>
              </ul>

              {sessions.length === 0 && onStartNewDiagnosis && (
                <motion.button
                  type="button"
                  onClick={onStartNewDiagnosis}
                  whileTap={buttonTap}
                  className="mt-5 flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-400/30 bg-cyan-400/10 px-4 py-3 text-sm font-bold text-cyan-300 transition hover:bg-cyan-400/15"
                >
                  <Send size={16} />
                  Run First Check
                </motion.button>
              )}
            </motion.section>
          </aside>
        </div>
      </motion.div>
    </>
  );
}
