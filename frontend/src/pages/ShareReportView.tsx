import { useCallback, useEffect, useMemo, useState } from 'react';
import { motion } from 'motion/react';
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

function buildReportText({
  stats,
  dashboard,
  hardwareUpdatedAt,
  sessions,
}: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  sessions: SessionSummary[];
}) {
  const latest = sessions[0] ?? dashboard.last_saved_session ?? dashboard.last_diagnosis;
  const recentSessions = sessions.slice(0, 8);

  const lines = [
    'RigMD PC Check Report',
    `Created: ${formatDate(new Date())}`,
    '',
    'PC summary',
    `Computer: ${cleanValue(stats?.device_name)}`,
    `Processor: ${cleanValue(stats?.cpu?.name).replace(/\s+\d+-Core Processor$/i, '')}`,
    `Memory: ${stats?.ram?.total_gb ? `${stats.ram.total_gb} GB total` : 'Not available'}`,
    `Memory during last scan: ${typeof stats?.ram?.usage_percent === 'number' ? `${stats.ram.usage_percent.toFixed(1)}% in use` : 'Not available'}`,
    `Storage: ${stats?.disk?.total_gb ? `${stats.disk.total_gb} GB ${cleanValue(stats.storage_type)}` : 'Not available'}`,
    `Storage during last scan: ${typeof stats?.disk?.usage_percent === 'number' ? `${stats.disk.usage_percent.toFixed(1)}% in use` : 'Not available'}`,
    `Graphics: ${cleanValue(stats?.gpu?.name)}`,
    `Windows: ${cleanValue(stats?.os_version).replace(/^Microsoft\s+/i, '').replace(/\s*\([\d.]+\)$/, '')}`,
    `PC info last collected: ${formatDate(hardwareUpdatedAt)}`,
    '',
    'Latest check',
    latest
      ? `Date: ${formatDate(getSessionDate(latest))}`
      : 'Date: No saved checks yet',
    latest
      ? `Symptom: ${cleanValue(latest.symptom_type)}`
      : 'Symptom: Not available',
    latest
      ? `Result: ${friendlyResult(latest.diagnosed_category)}`
      : 'Result: Not available',
    latest
      ? `What to do: ${friendlyAction(latest.action_category)}`
      : 'What to do: Run a PC check first',
    latest
      ? `Match strength: ${cleanValue(latest.confidence_label)}`
      : 'Match strength: Not available',
    '',
    'Saved check summary',
    `Total saved checks: ${dashboard.totals.total_sessions || sessions.length}`,
    `Checks this month: ${dashboard.totals.this_month_count}`,
    `Repeated problems: ${dashboard.recurring_issues_count}`,
    `Warning signs active: ${dashboard.warning_signs_active_count}`,
    `Needs help: ${dashboard.totals.escalated_count}`,
    '',
    'Recent saved checks',
    recentSessions.length
      ? recentSessions
          .map(
            (session, index) =>
              `${index + 1}. ${formatDate(getSessionDate(session))} - ${cleanValue(session.symptom_type)} - ${friendlyResult(session.diagnosed_category)} - ${friendlyAction(session.action_category)}`
          )
          .join('\n')
      : 'No saved checks yet.',
    '',
    'Note',
    'RigMD provides PC checkup guidance only. It does not replace professional hardware inspection.',
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
      }),
    [dashboard, hardwareUpdatedAt, sessions, stats]
  );

  const latestSession = sessions[0] ?? dashboard.last_saved_session ?? dashboard.last_diagnosis;
  const reportFileName = `RigMD-PC-Report-${new Date().toISOString().slice(0, 10)}.txt`;

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
    const subject = encodeURIComponent('RigMD PC Check Report');
    const body = encodeURIComponent(reportText);
    window.location.href = `mailto:?subject=${subject}&body=${body}`;
  };

  const handlePrint = () => {
    window.print();
  };

  return (
    <>
      <TopHeader
        title="Share Report"
        subtitle="Create a simple PC check report for support or repair help"
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
              <h3 className="text-2xl font-bold text-white">Ready to share with support</h3>
              <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">
                This report uses your saved PC information and recent checks. It does not include passwords, personal files, or account details.
              </p>

              <div className="mt-5 grid gap-4 md:grid-cols-3">
                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Latest result</p>
                  <p className="mt-2 break-words font-bold text-white">{friendlyResult(latestSession?.diagnosed_category)}</p>
                </div>

                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">What to do</p>
                  <p className="mt-2 font-bold text-white">{friendlyAction(latestSession?.action_category)}</p>
                </div>

                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Saved checks</p>
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

              <pre className="max-h-[560px] overflow-auto whitespace-pre-wrap p-5 text-sm leading-relaxed text-slate-200">
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
                  onClick={handleCopy}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg bg-cyan-400 px-4 py-3 text-sm font-bold text-[#041014] transition hover:bg-cyan-300"
                >
                  <Clipboard size={16} />
                  Copy Report
                </motion.button>

                <motion.button
                  type="button"
                  onClick={handleDownload}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-400/30 bg-cyan-400/10 px-4 py-3 text-sm font-bold text-cyan-300 transition hover:bg-cyan-400/15"
                >
                  <Download size={16} />
                  Download Text File
                </motion.button>

                <motion.button
                  type="button"
                  onClick={handlePrint}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-3 text-sm font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300"
                >
                  <Printer size={16} />
                  Print Report
                </motion.button>

                <motion.button
                  type="button"
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
                <li>Run a new check if the PC changed since the last scan.</li>
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
