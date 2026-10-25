import { getCheckOverview } from '../lib/checkPresentation';
import { getSessionCheckLabel } from '../lib/pastChecksFormatting';
import { buildReportText, formatDate, friendlyAction, getSessionDate } from '../lib/reportFormatting';
import { loadReportData } from '../lib/reportData';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
  hardwareUpdatedAt: Date | null;
  onStartNewDiagnosis?: () => void;
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
  hardwareUpdatedAt,
  onStartNewDiagnosis,
}: ShareReportViewProps) {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<string>('');
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const liveSnapshot = useRef({ stats, hardwareUpdatedAt });
  liveSnapshot.current = { stats, hardwareUpdatedAt };
  const request = useRef<AbortController | null>(null);
  const [snapshot, setSnapshot] = useState<{
    stats: HardwareStats | null; dashboard: DashboardSummary; hardwareUpdatedAt: Date | null; generatedAt: Date;
  } | null>(null);
  const captureSnapshot = useCallback(() => {
    setSnapshot(previous => previous ? { ...previous, ...liveSnapshot.current, generatedAt: new Date() } : previous);
  }, []);

  const refreshReport = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setIsLoading(true);
    setLoadError(null);
    setMessage(null);

    try {
      const fresh = await loadReportData(apiFetch, controller.signal);
      if (controller.signal.aborted) return;
      setSessions(fresh.sessions);
      setSelectedSessionId((prev) =>
        prev && fresh.sessions.some((s) => s.session_id === prev)
          ? prev
          : (fresh.sessions[0]?.session_id ?? '')
      );
      setSnapshot({ ...liveSnapshot.current, dashboard: fresh.dashboard, generatedAt: new Date() });
    } catch {
      if (!controller.signal.aborted) setLoadError('The report could not be refreshed. Saved checks or summary totals are unavailable. Try Refresh again.');
    } finally {
      if (!controller.signal.aborted) setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void refreshReport();
    return () => request.current?.abort();
  }, [refreshReport]);

  const activeSession = useMemo(
    () => sessions.find((s) => s.session_id === selectedSessionId) ?? sessions[0] ?? null,
    [selectedSessionId, sessions]
  );
  const canExport = snapshot !== null && !isLoading && !loadError;
  const statusMessage = loadError ? { type: 'error', text: loadError } : message;

  const isLatestSelected =
    !selectedSessionId ||
    !sessions.length ||
    selectedSessionId === sessions[0]?.session_id;

  const reportText = useMemo(
    () =>
      snapshot ? buildReportText({
        ...snapshot,
        sessions,
        selectedSession: activeSession,
        historyUnavailable: false,
      }) : '',
    [activeSession, snapshot, sessions]
  );

  const reportFileName = `RigMD-Device-Report-${(snapshot?.generatedAt ?? new Date()).toISOString().slice(0, 10)}.txt`;

  const handleCopy = async () => {
    if (!canExport) return;
    try {
      await navigator.clipboard.writeText(reportText);
      setMessage({ type: 'success', text: 'Report copied. You can paste it into chat, email, or a support ticket.' });
    } catch {
      setMessage({ type: 'error', text: 'Copy did not work on this device. Download the report instead.' });
    }
  };

  const handleDownload = () => {
    if (!canExport) return;
    downloadTextFile(reportFileName, reportText);
    setMessage({ type: 'success', text: 'Text file download requested.' });
  };

  const handleEmailDraft = () => {
    if (!canExport) return;
    const subject = encodeURIComponent('RigMD Device Check Report');
    const body = encodeURIComponent(reportText);
    window.location.href = `mailto:?subject=${subject}&body=${body}`;
  };

  const handlePrint = () => {
    if (!canExport) return;
    window.print();
  };

  return (
    <>
      {canExport && createPortal(<article className="rigmd-print-report" aria-label="Device check report"><pre>{reportText}</pre></article>, document.body)}
      <TopHeader
        title="Share Report"
        subtitle="Select a saved check, review your results, then print or share them"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar min-w-0 flex-1 overflow-y-auto px-6 py-6 lg:px-8"
      >
        <div className="mx-auto grid w-full min-w-0 max-w-[1360px] gap-5 xl:grid-cols-[minmax(0,1fr)_380px]">
          <div className="min-w-0 space-y-5">
            {statusMessage && (
              <div
                role="status"
                className={`rounded-xl border px-4 py-3 text-sm ${
                  statusMessage.type === 'success'
                    ? 'border-emerald-400/25 bg-emerald-400/10 text-emerald-200'
                    : 'border-amber-400/30 bg-amber-400/10 text-amber-200'
                }`}
              >
                {statusMessage.text}
              </div>
            )}

            <motion.section
              variants={cardFadeUp}
              transition={cardTransition}
              className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6"
            >
              <div className="flex flex-col justify-between gap-4 lg:flex-row lg:items-start">
                <div>
                  <p className="mb-3 text-xs font-bold uppercase tracking-[0.16em] text-cyan-300">Report Preview</p>
                  <h3 className="text-2xl font-bold text-white">Your device report</h3>
                  <p className="mt-2 max-w-2xl text-sm leading-relaxed text-slate-300">
                    Includes your computer name, device information, and the selected diagnostic check. Review it before sharing.
                  </p>
                </div>

                {sessions.length > 0 && (
                  <div className="w-full min-w-0 lg:w-96">
                    <label
                      htmlFor="report-session-selector"
                      className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-400"
                    >
                      Select Saved Check
                    </label>
                    <select
                      id="report-session-selector"
                      value={selectedSessionId || sessions[0]?.session_id || ''}
                      onChange={(e) => { setSelectedSessionId(e.target.value); captureSnapshot(); }}
                      disabled={!canExport}
                      className="w-full rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-3.5 py-2.5 text-xs font-semibold text-white transition focus:border-cyan-400 focus:outline-none"
                    >
                      {sessions.map((s, idx) => {
                        const codePrefix = s.session_code ? `${s.session_code} • ` : '';
                        const dateLabel = formatDate(getSessionDate(s));
                        const findingLabel = getSessionCheckLabel(s);
                        const latestTag = idx === 0 ? ' (Latest)' : '';
                        return (
                          <option key={s.session_id} value={s.session_id}>
                            {`${codePrefix}${dateLabel} — ${findingLabel}${latestTag}`}
                          </option>
                        );
                      })}
                    </select>
                  </div>
                )}
              </div>

              <div className="mt-5 grid gap-4 md:grid-cols-3">
                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">
                    {isLatestSelected ? 'Latest result' : 'Selected result'}
                  </p>
                  <p className="mt-2 break-words font-bold text-white">{canExport ? getCheckOverview(activeSession ?? {}).title : 'Not available'}</p>
                </div>

                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">What to do</p>
                  <p className="mt-2 font-bold text-white">{canExport ? friendlyAction(activeSession?.action_category) : 'Not available'}</p>
                </div>

                <div className="rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-4">
                  <p className="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Saved checks</p>
                  <p className="mt-2 font-bold text-white">{canExport ? snapshot!.dashboard.totals.total_sessions : 'Not available'}</p>
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
                  onClick={refreshReport}
                  disabled={isLoading}
                  className="inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-3 py-2 text-xs font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300 disabled:cursor-wait disabled:opacity-70"
                >
                  <RefreshCw size={14} className={isLoading ? 'animate-spin' : ''} />
                  Refresh
                </button>
              </div>

              <pre tabIndex={0} aria-label="Report preview" className="font-sans max-h-[560px] overflow-auto whitespace-pre-wrap [overflow-wrap:anywhere] p-5 text-sm leading-relaxed text-slate-200">
                {isLoading ? 'Loading report...' : loadError ? 'Report unavailable. Refresh to try again.' : reportText}
              </pre>
            </motion.section>
          </div>

          <aside className="min-w-0 space-y-4">
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
                  disabled={!canExport}
                  onClick={handleCopy}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg bg-cyan-400 px-4 py-3 text-sm font-bold text-[#041014] transition hover:bg-cyan-300"
                >
                  <Clipboard size={16} />
                  {isLoading ? 'Loading report...' : 'Copy Report'}
                </motion.button>

                <motion.button
                  type="button"
                  disabled={!canExport}
                  onClick={handleDownload}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-400/30 bg-cyan-400/10 px-4 py-3 text-sm font-bold text-cyan-300 transition hover:bg-cyan-400/15"
                >
                  <Download size={16} />
                  Download Text File
                </motion.button>

                <motion.button
                  type="button"
                  disabled={!canExport}
                  onClick={handlePrint}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-3 text-sm font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300"
                >
                  <Printer size={16} />
                  Print Report
                </motion.button>

                <motion.button
                  type="button"
                  disabled={!canExport}
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
