import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  AlertTriangle,
  CalendarDays,
  Clock3,
  FileText,
  RefreshCw,
  Search,
  Zap,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import type { SessionSummary } from '../types/rigmd';

import { API_BASE_URL, fetchWithClient } from '../services/apiClient';

const filters = ['All Sessions', 'Monitor', 'Maintain', 'Troubleshoot', 'Escalate', 'Recurring Only'];

interface Props {
  onViewSession?: (sessionId: string) => void;
}

function normalizeAction(action: string): string {
  const value = action.toLowerCase();
  if (value.includes('escalate')) return 'Escalate';
  if (value.includes('troubleshoot')) return 'Troubleshoot';
  if (value.includes('maintain')) return 'Maintain';
  return 'Monitor';
}

function isCurrentMonth(value: string | null) {
  if (!value) return false;
  const date = new Date(value);
  const now = new Date();
  return date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
}

function getActionStyle(action: string) {
  const value = action.toLowerCase();
  if (value.includes('monitor')) return 'border-blue-500/60 bg-blue-500/10 text-blue-400';
  if (value.includes('maintain')) return 'border-emerald-500/60 bg-emerald-500/10 text-emerald-400';
  if (value.includes('troubleshoot')) return 'border-orange-500/60 bg-orange-500/10 text-orange-400';
  if (value.includes('escalate')) return 'border-red-500/60 bg-red-500/10 text-red-400';
  return 'border-cyan-500/60 bg-cyan-500/10 text-cyan-400';
}

function getConfidenceStyle(confidence: string) {
  const value = confidence.toLowerCase();
  if (value.includes('high')) return 'text-emerald-400';
  if (value.includes('moderate') || value.includes('medium')) return 'text-orange-400';
  return 'text-slate-400';
}

function getResolutionLabel(status?: string) {
  if (status === 'resolved') return 'Resolved';
  if (status === 'still_active') return 'Still Active';
  if (status === 'needs_recheck') return 'Needs Recheck';
  return 'Open';
}

function getResolutionStyle(status?: string) {
  if (status === 'resolved') return 'border-emerald-500/50 bg-emerald-500/10 text-emerald-400 hover:bg-emerald-500/15';
  if (status === 'still_active') return 'border-red-500/50 bg-red-500/10 text-red-300 hover:bg-red-500/15';
  if (status === 'needs_recheck') return 'border-orange-500/50 bg-orange-500/10 text-orange-300 hover:bg-orange-500/15';
  return 'border-cyan-500/50 bg-cyan-500/10 text-cyan-400 hover:bg-cyan-500/15';
}

function MetricCard({ icon, label, value, borderColor }: { icon: ReactNode; label: string; value: number; borderColor: string }) {
  return (
    <section className={`rounded-2xl border bg-[#161b22] px-5 py-6 ${borderColor}`}>
      <div className="flex items-center gap-4">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[#0d1117]">{icon}</div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.28em] text-slate-500">{label}</p>
          <h3 className="text-3xl font-bold leading-none text-white">{value}</h3>
        </div>
      </div>
    </section>
  );
}

function FilterButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-full border px-4 py-2 text-xs font-bold uppercase transition ${
        active
          ? 'border-cyan-500/80 bg-cyan-500/10 text-cyan-400'
          : 'border-[#30363d] bg-[#0d1117] text-slate-500 hover:border-cyan-500/40 hover:text-cyan-400'
      }`}
    >
      {label}
    </button>
  );
}

function SessionRow({ session, onViewSession }: { session: SessionSummary; onViewSession?: (sessionId: string) => void }) {
  const action = normalizeAction(session.action_category || '');

  return (
    <div className="grid grid-cols-[130px_minmax(190px,1fr)_minmax(260px,1.25fr)_140px_150px_170px] items-center border-b border-[#30363d] px-5 py-4 last:border-b-0">
      <div>
        <p className="text-sm font-bold text-white">{session.display_date ?? 'No date'}</p>
        <p className="text-xs text-slate-500">
          {session.created_at ? new Date(session.created_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : ''}
        </p>
      </div>

      <div className="min-w-0">
        <p className="truncate font-semibold text-white">{session.symptom_type}</p>
        {session.is_recurring && (
          <span className="mt-1 inline-flex rounded-full border border-orange-500/40 bg-orange-500/10 px-2 py-0.5 text-[11px] font-bold text-orange-400">
            Recurring
          </span>
        )}
      </div>

      <p className="truncate text-center text-sm text-slate-400">{session.diagnosed_category}</p>

      <div className="flex justify-center">
        <span className={`rounded border px-3 py-1 text-xs font-bold uppercase ${getActionStyle(action)}`}>{action}</span>
      </div>

      <div className="flex justify-center">
        <span className={`text-xs font-bold uppercase ${getConfidenceStyle(session.confidence_label || '')}`}>
          {session.confidence_label || 'Low'}
        </span>
      </div>

      <div className="flex justify-center">
        <button
          type="button"
          disabled={!session.session_id || !onViewSession}
          onClick={() => onViewSession?.(session.session_id)}
          className={`rounded border px-3 py-2 text-[11px] font-bold uppercase transition disabled:cursor-not-allowed disabled:opacity-50 ${getResolutionStyle(session.resolution_status)}`}
          title="Open this diagnosis to resolve it"
        >
          {getResolutionLabel(session.resolution_status)}
        </button>
      </div>
    </div>
  );
}

export default function DiagnosticHistoryView({ onViewSession }: Props) {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('All Sessions');
  const [search, setSearch] = useState('');

  const fetchSessions = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetchWithClient(`${API_BASE_URL}/api/diagnosis/sessions`);
      if (!response.ok) throw new Error(`Server returned status ${response.status}`);

      const data = await response.json();
      setSessions(Array.isArray(data?.sessions) ? data.sessions : []);
    } catch {
      setError('Could not load session history.');
      setSessions([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchSessions();
  }, [fetchSessions]);

  const filteredSessions = useMemo(() => {
    const query = search.trim().toLowerCase();

    return sessions
      .filter((session) => {
        const action = normalizeAction(session.action_category || '');

        if (filter === 'Recurring Only' && !session.is_recurring) return false;
        if (filter !== 'All Sessions' && filter !== 'Recurring Only' && action !== filter) return false;
        if (!query) return true;

        return (
          session.symptom_type?.toLowerCase().includes(query) ||
          session.diagnosed_category?.toLowerCase().includes(query)
        );
      })
      .sort((a, b) => new Date(b.created_at ?? 0).getTime() - new Date(a.created_at ?? 0).getTime());
  }, [filter, search, sessions]);

  const totalSessions = sessions.length;
  const recurringCount = sessions.filter((session) => session.is_recurring).length;
  const escalatedCount = sessions.filter((session) => normalizeAction(session.action_category || '') === 'Escalate').length;
  const thisMonthCount = sessions.filter((session) => isCurrentMonth(session.created_at)).length;

  return (
    <>
      <TopHeader title="Diagnostic History" subtitle="All saved diagnostic sessions and their outcomes" />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="mx-auto w-full max-w-[1512px] space-y-5">
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-4">
            <MetricCard icon={<Zap size={20} className="text-blue-400" />} label="Total Sessions" value={totalSessions} borderColor="border-blue-500/30" />
            <MetricCard icon={<RefreshCw size={20} className="text-orange-400" />} label="Recurring Issues" value={recurringCount} borderColor="border-orange-500/30" />
            <MetricCard icon={<AlertTriangle size={20} className="text-red-400" />} label="Escalated" value={escalatedCount} borderColor="border-red-500/30" />
            <MetricCard icon={<CalendarDays size={20} className="text-cyan-400" />} label="This Month" value={thisMonthCount} borderColor="border-cyan-500/30" />
          </div>

          <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-4">
            <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
              <div className="flex flex-wrap gap-2">
                {filters.map((item) => (
                  <FilterButton key={item} label={item} active={filter === item} onClick={() => setFilter(item)} />
                ))}
              </div>

              <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                <label className="relative block">
                  <Search size={16} className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-slate-500" />
                  <input
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                    placeholder="Search symptom or cause..."
                    className="h-10 w-full rounded-lg border border-[#30363d] bg-[#1f2937] pl-11 pr-4 text-sm text-white outline-none placeholder:text-slate-500 focus:border-cyan-500/50 sm:w-[300px]"
                  />
                </label>

                <button
                  type="button"
                  onClick={fetchSessions}
                  className="inline-flex h-10 items-center justify-center gap-2 rounded-lg border border-[#30363d] bg-[#1f2937] px-4 text-sm font-bold text-white transition hover:border-cyan-500/40 hover:text-cyan-400"
                >
                  <RefreshCw size={16} className={loading ? 'animate-spin text-cyan-400' : 'text-slate-400'} />
                  Refresh
                </button>
              </div>
            </div>
          </section>

          <section className="overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22]">
            <div className="flex items-center justify-between border-b border-[#30363d] px-5 py-4">
              <div className="flex items-center gap-3">
                <RefreshCw size={15} className={loading ? 'animate-spin text-cyan-400' : 'text-cyan-400'} />
                <h3 className="font-bold uppercase tracking-wider text-white">Sessions</h3>
                <span className="rounded-full bg-[#20304a] px-2 py-0.5 text-xs font-bold text-slate-400">
                  {filteredSessions.length}
                </span>
              </div>

              <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-500">
                <Clock3 size={14} />
                Newest First
              </div>
            </div>

            <div className="overflow-x-auto">
              <div className="min-w-[1260px]">
                <div className="grid grid-cols-[130px_minmax(190px,1fr)_minmax(260px,1.25fr)_140px_150px_170px] border-b border-[#30363d] bg-[#111827] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.24em] text-slate-500">
                  <div>Date</div>
                  <div>Symptom</div>
                  <div className="text-center">Probable Cause</div>
                  <div className="text-center">Action</div>
                  <div className="text-center">Confidence</div>
                  <div className="text-center">Status</div>
                </div>

                {loading ? (
                  <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                    <RefreshCw size={38} className="mb-4 animate-spin text-slate-600" />
                    <h3 className="text-lg font-bold text-white">Loading diagnostic sessions</h3>
                  </div>
                ) : error ? (
                  <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                    <AlertTriangle size={42} className="mb-4 text-red-400" />
                    <h3 className="text-lg font-bold text-white">{error}</h3>
                  </div>
                ) : filteredSessions.length === 0 ? (
                  <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                    <FileText size={42} className="mb-4 text-slate-600" />
                    <h3 className="text-lg font-bold text-white">No diagnostic sessions yet</h3>
                    <p className="mt-2 max-w-md text-sm leading-relaxed text-slate-500">
                      Saved diagnostic sessions will appear here after the New Diagnosis workflow saves results.
                    </p>
                  </div>
                ) : (
                  filteredSessions.map((session) => (
                    <SessionRow key={session.session_id} session={session} onViewSession={onViewSession} />
                  ))
                )}
              </div>
            </div>
          </section>
        </div>
      </div>
    </>
  );
}