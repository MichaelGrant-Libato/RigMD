import { useCallback, useEffect, useMemo, useState } from 'react';
import axios from 'axios';
import {
  Activity,
  AlertTriangle,
  Calendar,
  Clock3,
  Copy,
  Download,
  Eye,
  FileText,
  RefreshCw,
  Search,
  X,
  Zap,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8000';

type HistoryFilter = 'all' | 'monitor' | 'maintain' | 'troubleshoot' | 'escalate' | 'recurring';
type HistorySort = 'newest' | 'oldest';

interface HistoryMetrics {
  total_sessions: number;
  recurring_issues: number;
  escalated: number;
  this_month: number;
}

interface HistoryRecommendation {
  warning_sign: string;
  threshold: string;
  recommended_action: string;
}

interface HistorySession {
  id: string;
  session_code: string;
  display_date: string;
  display_time: string;
  duration: string;
  created_at: string | null;
  symptom: string;
  affected_activity: string | null;
  frequency: string;
  severity: string;
  recent_changes: string | null;
  system_state: string | null;
  probable_cause: string;
  action_category: string;
  confidence_label: string;
  ai_explanation: string | null;
  is_recurring: boolean;
  warning_signs: string[];
  recommendations: HistoryRecommendation[];
  recommended_next_step: string;
}

interface HistoryResponse {
  metrics: HistoryMetrics;
  sessions: HistorySession[];
}

const emptyHistory: HistoryResponse = {
  metrics: {
    total_sessions: 0,
    recurring_issues: 0,
    escalated: 0,
    this_month: 0,
  },
  sessions: [],
};

function getActionStyle(action: string) {
  const value = action.toLowerCase();

  if (value.includes('monitor')) {
    return {
      text: 'text-blue-400',
      border: 'border-blue-500/70',
      bg: 'bg-blue-500/10',
      row: 'border-l-blue-500',
    };
  }

  if (value.includes('maintain')) {
    return {
      text: 'text-emerald-400',
      border: 'border-emerald-500/70',
      bg: 'bg-emerald-500/10',
      row: 'border-l-emerald-500',
    };
  }

  if (value.includes('troubleshoot')) {
    return {
      text: 'text-orange-400',
      border: 'border-orange-500/70',
      bg: 'bg-orange-500/10',
      row: 'border-l-orange-500',
    };
  }

  if (value.includes('escalate')) {
    return {
      text: 'text-red-400',
      border: 'border-red-500/70',
      bg: 'bg-red-500/10',
      row: 'border-l-red-500',
    };
  }

  return {
    text: 'text-cyan-400',
    border: 'border-cyan-500/70',
    bg: 'bg-cyan-500/10',
    row: 'border-l-cyan-500',
  };
}

function getConfidenceStyle(confidence: string) {
  const value = confidence.toLowerCase();

  if (value.includes('high')) {
    return 'border-emerald-500/70 bg-emerald-500/10 text-emerald-400';
  }

  if (value.includes('moderate') || value.includes('medium')) {
    return 'border-orange-500/70 bg-orange-500/10 text-orange-400';
  }

  return 'border-red-500/70 bg-red-500/10 text-red-400';
}

function MetricCard({
  icon,
  label,
  value,
  color,
}: {
  icon: React.ReactNode;
  label: string;
  value: number;
  color: string;
}) {
  return (
    <section className={`rounded-2xl border bg-[#161b22] p-5 ${color}`}>
      <div className="flex items-center gap-4">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[#0d1117]">
          {icon}
        </div>

        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">{label}</p>
          <h3 className="text-3xl font-bold text-white">{value}</h3>
        </div>
      </div>
    </section>
  );
}

function ActionBadge({ action }: { action: string }) {
  const style = getActionStyle(action);

  return (
    <span
      className={`inline-flex items-center justify-center rounded border px-2.5 py-1 text-xs font-bold uppercase ${style.border} ${style.bg} ${style.text}`}
    >
      {action}
    </span>
  );
}

function ConfidenceBadge({ confidence }: { confidence: string }) {
  return (
    <span
      className={`inline-flex items-center justify-center rounded border px-2.5 py-1 text-xs font-bold uppercase ${getConfidenceStyle(
        confidence
      )}`}
    >
      {confidence}
    </span>
  );
}

function FilterButton({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-full border px-4 py-2 text-xs font-bold uppercase transition ${
        active
          ? 'border-cyan-500/60 bg-cyan-500/10 text-cyan-400'
          : 'border-[#30363d] bg-[#0d1117] text-slate-500 hover:border-cyan-500/40 hover:text-cyan-400'
      }`}
    >
      {label}
    </button>
  );
}

function SessionDetailPanel({
  session,
  onClose,
}: {
  session: HistorySession;
  onClose: () => void;
}) {
  const copySummary = async () => {
    const summary = [
      `RigMD Diagnostic Session: ${session.session_code}`,
      `Date: ${session.display_date} ${session.display_time}`,
      `Symptom: ${session.symptom}`,
      `Probable Cause: ${session.probable_cause}`,
      `Action: ${session.action_category}`,
      `Confidence: ${session.confidence_label}`,
      `Recommended Next Step: ${session.recommended_next_step}`,
    ].join('\n');

    await navigator.clipboard.writeText(summary);
  };

  return (
    <aside className="rounded-2xl border border-[#30363d] bg-[#161b22]">
      <div className="flex items-center justify-between border-b border-[#30363d] px-5 py-4">
        <div className="flex items-center gap-2">
          <Zap size={15} className="text-cyan-400" />
          <h3 className="text-sm font-bold uppercase tracking-wider text-cyan-400">Session Detail</h3>
        </div>

        <button
          type="button"
          onClick={onClose}
          className="rounded-lg p-1 text-slate-500 transition hover:bg-[#0d1117] hover:text-white"
        >
          <X size={18} />
        </button>
      </div>

      <div className="space-y-0">
        <section className="border-b border-[#30363d] p-5">
          <p className="text-sm font-bold text-cyan-400">{session.session_code}</p>
          <p className="mt-1 text-xs text-slate-500">
            {session.display_date} · {session.display_time}
          </p>
        </section>

        <section className="border-b border-[#30363d] p-5">
          <p className="text-[11px] font-bold uppercase tracking-wider text-slate-500">Symptom</p>
          <h4 className="mt-2 text-lg font-bold text-white">{session.symptom}</h4>

          <p className="mt-5 text-[11px] font-bold uppercase tracking-wider text-slate-500">Probable Cause</p>
          <p className="mt-2 font-semibold text-white">{session.probable_cause}</p>

          <div className="mt-5 grid grid-cols-2 gap-4">
            <div>
              <p className="mb-2 text-[11px] font-bold uppercase tracking-wider text-slate-500">Action</p>
              <ActionBadge action={session.action_category} />
            </div>

            <div>
              <p className="mb-2 text-[11px] font-bold uppercase tracking-wider text-slate-500">Confidence</p>
              <ConfidenceBadge confidence={session.confidence_label} />
            </div>
          </div>
        </section>

        <section className="border-b border-[#30363d] p-5">
          <p className="text-[11px] font-bold uppercase tracking-wider text-slate-500">Warning Signs</p>

          {session.warning_signs.length === 0 ? (
            <p className="mt-3 text-sm text-slate-500">No warning signs recorded.</p>
          ) : (
            <div className="mt-3 space-y-2">
              {session.warning_signs.map((warning) => (
                <div key={warning} className="flex items-start gap-2 text-sm text-orange-400">
                  <AlertTriangle size={14} className="mt-0.5 shrink-0" />
                  <span>{warning}</span>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="border-b border-[#30363d] p-5">
          <p className="text-[11px] font-bold uppercase tracking-wider text-slate-500">Recommended Next Step</p>
          <div className="mt-3 rounded-xl border border-[#30363d] bg-[#1f2937] p-4 text-sm leading-relaxed text-white">
            {session.recommended_next_step}
          </div>
        </section>

        <section className="space-y-3 p-5">
          <button
            type="button"
            onClick={copySummary}
            className="flex w-full items-center justify-center gap-2 rounded-xl border border-cyan-500/30 bg-cyan-500/10 px-4 py-3 text-sm font-bold text-cyan-400 transition hover:bg-cyan-500/20"
          >
            <Copy size={16} />
            Copy Summary
          </button>

          <button
            type="button"
            disabled
            className="flex w-full cursor-not-allowed items-center justify-center gap-2 rounded-xl border border-[#30363d] bg-[#1f2937] px-4 py-3 text-sm font-bold text-slate-500"
          >
            <Download size={16} />
            Export Report
          </button>
        </section>
      </div>
    </aside>
  );
}

export default function DiagnosticHistoryView() {
  const [history, setHistory] = useState<HistoryResponse>(emptyHistory);
  const [selectedSession, setSelectedSession] = useState<HistorySession | null>(null);
  const [activeFilter, setActiveFilter] = useState<HistoryFilter>('all');
  const [sort, setSort] = useState<HistorySort>('newest');
  const [search, setSearch] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const selectedAction = activeFilter === 'recurring' ? 'all' : activeFilter;
  const recurringOnly = activeFilter === 'recurring';

  const fetchHistory = useCallback(async () => {
    setIsLoading(true);

    try {
      const response = await axios.get<HistoryResponse>(`${API_BASE_URL}/api/history/sessions`, {
        params: {
          search,
          action: selectedAction,
          recurring_only: recurringOnly,
          sort,
        },
      });

      setHistory(response.data);

      if (selectedSession) {
        const refreshed = response.data.sessions.find((session) => session.id === selectedSession.id);
        setSelectedSession(refreshed ?? null);
      }
    } catch {
      setHistory(emptyHistory);
      setSelectedSession(null);
    } finally {
      setIsLoading(false);
    }
  }, [search, selectedAction, recurringOnly, sort, selectedSession]);

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  const filters: { key: HistoryFilter; label: string }[] = [
    { key: 'all', label: 'All Sessions' },
    { key: 'monitor', label: 'Monitor' },
    { key: 'maintain', label: 'Maintain' },
    { key: 'troubleshoot', label: 'Troubleshoot' },
    { key: 'escalate', label: 'Escalate' },
    { key: 'recurring', label: 'Recurring Only' },
  ];

  const tableLayoutClass = selectedSession
    ? 'grid grid-cols-1 gap-5 2xl:grid-cols-[minmax(0,1fr)_380px]'
    : 'grid grid-cols-1';

  const tableTitleCount = useMemo(() => history.sessions.length, [history.sessions.length]);

  return (
    <>
      <TopHeader title="Diagnostic History" subtitle="All saved diagnostic sessions and their outcomes" />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="mx-auto w-full max-w-[1360px] space-y-5">
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-4">
            <MetricCard
              icon={<Activity size={20} className="text-blue-400" />}
              label="Total Sessions"
              value={history.metrics.total_sessions}
              color="border-blue-500/30"
            />

            <MetricCard
              icon={<RefreshCw size={20} className="text-orange-400" />}
              label="Recurring Issues"
              value={history.metrics.recurring_issues}
              color="border-orange-500/30"
            />

            <MetricCard
              icon={<AlertTriangle size={20} className="text-red-400" />}
              label="Escalated"
              value={history.metrics.escalated}
              color="border-red-500/30"
            />

            <MetricCard
              icon={<Calendar size={20} className="text-cyan-400" />}
              label="This Month"
              value={history.metrics.this_month}
              color="border-cyan-500/30"
            />
          </div>

          <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-4">
            <div className="flex flex-col justify-between gap-4 xl:flex-row xl:items-center">
              <div className="flex flex-wrap gap-2">
                {filters.map((filter) => (
                  <FilterButton
                    key={filter.key}
                    label={filter.label}
                    active={activeFilter === filter.key}
                    onClick={() => setActiveFilter(filter.key)}
                  />
                ))}
              </div>

              <div className="flex flex-col gap-3 sm:flex-row">
                <div className="flex min-w-[300px] items-center gap-2 rounded-xl border border-[#30363d] bg-[#1f2937] px-4 py-2">
                  <Search size={16} className="text-slate-500" />
                  <input
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                    placeholder="Search symptom or cause..."
                    className="w-full bg-transparent text-sm text-white outline-none placeholder:text-slate-500"
                  />
                </div>

                <button
                  type="button"
                  onClick={fetchHistory}
                  className="flex items-center justify-center gap-2 rounded-xl border border-[#30363d] bg-[#1f2937] px-4 py-2 text-sm font-semibold text-slate-300 transition hover:border-cyan-500/40 hover:text-cyan-400"
                >
                  <RefreshCw size={16} className={isLoading ? 'animate-spin' : ''} />
                  Refresh
                </button>
              </div>
            </div>
          </section>

          <div className={tableLayoutClass}>
            <section className="min-w-0 overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22]">
              <div className="flex items-center justify-between border-b border-[#30363d] px-5 py-4">
                <div className="flex items-center gap-2">
                  <RefreshCw size={15} className="text-cyan-400" />
                  <h3 className="font-bold uppercase tracking-wider text-white">Sessions</h3>
                  <span className="rounded-full bg-[#1f2937] px-2 py-0.5 text-xs font-bold text-slate-500">
                    {tableTitleCount}
                  </span>
                </div>

                <button
                  type="button"
                  onClick={() => setSort(sort === 'newest' ? 'oldest' : 'newest')}
                  className="flex items-center gap-2 text-xs font-semibold uppercase text-slate-500 transition hover:text-cyan-400"
                >
                  <Clock3 size={14} />
                  {sort === 'newest' ? 'Newest First' : 'Oldest First'}
                </button>
              </div>

              <div className="grid grid-cols-[130px_minmax(180px,1fr)_minmax(220px,1fr)_260px_210px] border-b border-[#30363d] bg-[#111827] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                <div>Date</div>
                <div>Symptom</div>
                <div className="text-center">Probable Cause</div>
                <div className="text-center">Action</div>
                <div className="text-center">Confidence</div>
              </div>

              {history.sessions.length === 0 ? (
                <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                  <FileText size={42} className="mb-4 text-slate-600" />
                  <h3 className="text-lg font-bold text-white">No diagnostic sessions yet</h3>
                  <p className="mt-2 max-w-md text-sm text-slate-500">
                    Saved diagnostic sessions will appear here after the New Diagnosis workflow saves results.
                  </p>
                </div>
              ) : (
                <div>
                  {history.sessions.map((session) => {
                    const actionStyle = getActionStyle(session.action_category);
                    const isSelected = selectedSession?.id === session.id;

                    return (
                      <button
                        type="button"
                        key={session.id}
                        onClick={() => setSelectedSession(session)}
                        className={`grid w-full grid-cols-[130px_minmax(180px,1fr)_minmax(220px,1fr)_260px_210px] border-l-2 border-b border-[#30363d] px-5 py-4 text-left transition hover:bg-[#1b2432] ${
                          actionStyle.row
                        } ${isSelected ? 'bg-[#1b2432]' : 'bg-[#161b22]'}`}
                      >
                        <div>
                          <p className="text-sm font-bold text-white">{session.display_date}</p>
                          <p className="mt-1 text-xs text-slate-500">
                            {session.display_time} · {session.duration}
                          </p>
                        </div>

                        <div className="flex min-w-0 items-center gap-2">
                          <p className="truncate font-bold text-white">{session.symptom}</p>

                          {session.is_recurring && (
                            <span className="rounded-full bg-[#1f2937] px-2 py-1 text-[11px] font-semibold text-slate-400">
                              Recurring
                            </span>
                          )}
                        </div>

                        <div className="flex items-center justify-center text-center text-sm text-slate-400">
                          {session.probable_cause}
                        </div>

                        <div className="flex items-center justify-center">
                          <ActionBadge action={session.action_category} />
                        </div>

                        <div className="flex items-center justify-center gap-2">
                          <ConfidenceBadge confidence={session.confidence_label} />

                          <span className="inline-flex items-center gap-1 rounded-full border border-[#30363d] bg-[#0d1117] px-2 py-1 text-[11px] font-semibold text-slate-500">
                            <Eye size={12} />
                            {isSelected ? 'Hide' : 'Details'}
                          </span>
                        </div>
                      </button>
                    );
                  })}
                </div>
              )}
            </section>

            {selectedSession && (
              <SessionDetailPanel
                session={selectedSession}
                onClose={() => setSelectedSession(null)}
              />
            )}
          </div>
        </div>
      </div>
    </>
  );
}