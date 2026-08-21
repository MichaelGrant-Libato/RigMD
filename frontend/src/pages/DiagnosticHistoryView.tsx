import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { motion } from 'motion/react';

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
import { buttonTap, cardFadeUp, cardTransition, pageFade, pageTransition, staggerContainer } from '../lib/motion';
import { apiFetch } from '../lib/api';
import type { SessionSummary } from '../types/rigmd';

const filters = [
  'All Sessions',
  'Monitor',
  'Maintain',
  'Troubleshoot',
  'Escalate',
  'Recurring Only',
];

const DEFAULT_VISIBLE_SESSIONS = 10;
const SHOW_MORE_INCREMENT = 5;

interface Props {
  onViewSession?: (sessionId: string) => void;
  selectedSessionId?: string | null;
  onStartNewDiagnosis?: () => void;
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

  return (
    date.getMonth() === now.getMonth() &&
    date.getFullYear() === now.getFullYear()
  );
}

function getActionStyle(action: string) {
  const value = action.toLowerCase();

  if (value.includes('monitor')) {
    return 'border-cyan-400/35 bg-cyan-400/10 text-cyan-300';
  }

  if (value.includes('maintain')) {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  if (value.includes('troubleshoot')) {
    return 'border-amber-400/35 bg-amber-400/10 text-amber-300';
  }

  if (value.includes('escalate')) {
    return 'border-red-400/35 bg-red-400/10 text-red-300';
  }

  return 'border-slate-500/35 bg-slate-500/10 text-slate-300';
}

function getConfidenceStyle(confidence: string) {
  const value = confidence.toLowerCase();

  if (value.includes('high')) {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  if (
    value.includes('moderate') ||
    value.includes('medium')
  ) {
    return 'border-amber-400/35 bg-amber-400/10 text-amber-300';
  }

  return 'border-slate-500/35 bg-slate-500/10 text-slate-300';
}

function getResolutionLabel(status?: string) {
  if (status === 'resolved') return 'Resolved';
  if (status === 'still_active') return 'Still Active';
  if (status === 'needs_recheck') return 'Needs Recheck';

  return 'Open';
}

function getResolutionStyle(status?: string) {
  if (status === 'resolved') {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  if (status === 'still_active') {
    return 'border-red-400/35 bg-red-400/10 text-red-300';
  }

  if (status === 'needs_recheck') {
    return 'border-amber-400/35 bg-amber-400/10 text-amber-300';
  }

  return 'border-cyan-400/35 bg-cyan-400/10 text-cyan-300';
}

function formatSessionDate(session: SessionSummary) {
  if (session.display_date) {
    return session.display_date;
  }

  if (!session.created_at) {
    return 'No date';
  }

  const date = new Date(session.created_at);

  if (Number.isNaN(date.getTime())) {
    return 'No date';
  }

  return date.toLocaleDateString();
}

function formatSessionTime(createdAt?: string | null) {
  if (!createdAt) {
    return '';
  }

  const date = new Date(createdAt);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  return date.toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  });
}

function MetricCard({
  icon,
  label,
  value,
  borderColor,
}: {
  icon: ReactNode;
  label: string;
  value: number;
  borderColor: string;
}) {
  return (
    <motion.section
      variants={cardFadeUp}
      transition={cardTransition}
      className={`flex h-full min-h-[104px] items-center rounded-2xl border bg-[var(--rigmd-card)] px-5 py-6 ${borderColor}`}
    >
      <div className="flex items-center gap-4">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)]">
          {icon}
        </div>

        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.28em] text-slate-500">
            {label}
          </p>

          <h3 className="text-3xl font-bold leading-none text-white">
            {value}
          </h3>
        </div>
      </div>
    </motion.section>
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
    <motion.button
      type="button"
      onClick={onClick}
      whileTap={buttonTap}
      className={`rounded-full border px-4 py-2 text-xs font-bold uppercase transition ${
        active
          ? 'border-cyan-300/55 bg-[#12343a] text-cyan-200'
          : 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)] text-slate-500 hover:border-[#2b5261] hover:bg-[var(--rigmd-card-hover)] hover:text-cyan-300'
      }`}
    >
      {label}
    </motion.button>
  );
}

function SessionRow({
  session,
  onViewSession,
  selected,
}: {
  session: SessionSummary;
  onViewSession?: (sessionId: string) => void;
  selected?: boolean;
}) {
  const action = normalizeAction(
    session.action_category || ''
  );

  const sessionDate = formatSessionDate(session);
  const sessionTime = formatSessionTime(
    session.created_at
  );

  const canOpen =
    Boolean(session.session_id) &&
    Boolean(onViewSession);

  const openSession = () => {
    if (session.session_id) {
      onViewSession?.(session.session_id);
    }
  };

  return (
    <motion.div
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      whileHover={canOpen ? { y: -1 } : undefined}
      transition={{ duration: 0.2 }}
      onClick={canOpen ? openSession : undefined}
      className={`grid grid-cols-[130px_minmax(190px,1fr)_minmax(280px,1.35fr)_140px_140px_170px_120px] items-center border-b border-l-2 border-b-[var(--rigmd-border)] px-5 py-4 transition-colors last:border-b-0 ${
        canOpen ? 'cursor-pointer' : ''
      } ${
        selected
          ? 'border-l-cyan-300 bg-[#12343a]'
          : 'border-l-transparent hover:border-l-cyan-400/50 hover:bg-[var(--rigmd-card-hover)]'
      }`}
    >
      <div>
        <p className="text-sm font-bold text-white">
          {sessionDate}
        </p>

        <p className="text-xs text-slate-500">
          {sessionTime}
        </p>
      </div>

      <div className="min-w-0">
        <p className="truncate font-semibold text-white">
          {session.symptom_type || 'Unknown symptom'}
        </p>

        {session.is_recurring && (
          <span className="mt-1 inline-flex rounded-full border border-amber-400/25 bg-amber-400/[0.06] px-1.5 py-0.5 text-[10px] font-bold uppercase text-amber-300">
            Recurring
          </span>
        )}
      </div>

      <p className="mx-auto max-w-[360px] whitespace-normal text-center text-sm leading-relaxed text-slate-400">
        {session.diagnosed_category || 'Unknown'}
      </p>

      <div className="flex justify-center">
        <span
          className={`rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase ${getActionStyle(
            action
          )}`}
        >
          {action}
        </span>
      </div>

      <div className="flex justify-center">
        <span
          className={`rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase ${getConfidenceStyle(
            session.confidence_label || ''
          )}`}
        >
          {session.confidence_label || 'Low'}
        </span>
      </div>

      <div className="flex justify-center">
        <span
          className={`rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase ${getResolutionStyle(
            session.resolution_status
          )}`}
        >
          {getResolutionLabel(
            session.resolution_status
          )}
        </span>
      </div>

      <div className="flex justify-center">
        <motion.button
          type="button"
          disabled={!canOpen}
          onClick={(event) => {
            event.stopPropagation();
            openSession();
          }}
          whileTap={buttonTap}
          className="inline-flex items-center justify-center rounded-lg border border-cyan-400/30 bg-cyan-400/10 px-3 py-2 text-[11px] font-bold uppercase text-cyan-300 transition hover:border-cyan-300/60 hover:bg-cyan-400/15 disabled:cursor-not-allowed disabled:opacity-50"
          title="Open this diagnosis"
        >
          View
        </motion.button>
      </div>
    </motion.div>
  );
}

function TableRowSkeleton() {
  return (
    <div className="grid grid-cols-[130px_minmax(190px,1fr)_minmax(280px,1.35fr)_140px_140px_170px_120px] items-center border-b border-[var(--rigmd-border)] px-5 py-4 last:border-b-0">
      {Array.from({ length: 7 }).map((_, index) => (
        <div key={index} className="flex justify-center">
          <div className="h-4 w-24 animate-pulse rounded bg-[var(--rigmd-card-soft)]" />
        </div>
      ))}
    </div>
  );
}

export default function DiagnosticHistoryView({
  onViewSession,
  selectedSessionId,
  onStartNewDiagnosis,
}: Props) {
  const [sessions, setSessions] = useState<
    SessionSummary[]
  >([]);

  const [loading, setLoading] =
    useState(true);

  const [error, setError] =
    useState<string | null>(null);

  const [filter, setFilter] =
    useState('All Sessions');

  const [search, setSearch] =
    useState('');

  const [visibleCount, setVisibleCount] =
    useState(DEFAULT_VISIBLE_SESSIONS);

  const fetchSessions =
    useCallback(async () => {
      setLoading(true);
      setError(null);

      try {
        const response = await apiFetch(
          '/api/diagnosis/sessions'
        );

        if (!response.ok) {
          throw new Error(
            `Server returned status ${response.status}`
          );
        }

        const data = await response.json();

        if (Array.isArray(data)) {
          setSessions(data as SessionSummary[]);
        } else if (Array.isArray(data?.sessions)) {
          setSessions(data.sessions as SessionSummary[]);
        } else {
          console.warn(
            'Unexpected diagnostic sessions response:',
            data
          );

          setSessions([]);
        }
      } catch (error) {
        console.error(
          'Failed to load diagnostic sessions:',
          error
        );

        setError(
          'Could not load session history.'
        );

        setSessions([]);
      } finally {
        setLoading(false);
      }
    }, []);

  useEffect(() => {
    fetchSessions();
  }, [fetchSessions]);

  useEffect(() => {
    setVisibleCount(
      DEFAULT_VISIBLE_SESSIONS
    );
  }, [
    filter,
    search,
  ]);

  const filteredSessions =
    useMemo(() => {
      const query =
        search.trim().toLowerCase();

      return sessions
        .filter((session) => {
          const action =
            normalizeAction(
              session.action_category || ''
            );

          if (
            filter === 'Recurring Only' &&
            !session.is_recurring
          ) {
            return false;
          }

          if (
            filter !== 'All Sessions' &&
            filter !== 'Recurring Only' &&
            action !== filter
          ) {
            return false;
          }

          if (!query) {
            return true;
          }

          return (
            session.symptom_type
              ?.toLowerCase()
              .includes(query) ||
            session.diagnosed_category
              ?.toLowerCase()
              .includes(query)
          );
        })
        .sort(
          (a, b) =>
            new Date(
              b.created_at ?? 0
            ).getTime() -
            new Date(
              a.created_at ?? 0
            ).getTime()
        );
    }, [
      filter,
      search,
      sessions,
    ]);

  const visibleSessions =
    useMemo(
      () =>
        filteredSessions.slice(
          0,
          visibleCount
        ),
      [
        filteredSessions,
        visibleCount,
      ]
    );

  const totalSessions =
    sessions.length;

  const hasMoreSessions =
    visibleSessions.length <
    filteredSessions.length;

  const hasActiveFilters =
    filter !== 'All Sessions' ||
    search.trim().length > 0;

  const resetFilters = () => {
    setFilter('All Sessions');
    setSearch('');
  };

  const recurringCount =
    sessions.filter(
      (session) =>
        session.is_recurring
    ).length;

  const escalatedCount =
    sessions.filter(
      (session) =>
        normalizeAction(
          session.action_category || ''
        ) === 'Escalate'
    ).length;

  const thisMonthCount =
    sessions.filter(
      (session) =>
        isCurrentMonth(
          session.created_at
        )
    ).length;

  return (
    <>
      <TopHeader
        title="Diagnostic History"
        subtitle="All saved diagnostic sessions and their outcomes"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8"
      >
        <div className="mx-auto w-full max-w-[1512px] space-y-5">
          <motion.div
            variants={staggerContainer}
            initial="hidden"
            animate="visible"
            className="grid grid-cols-1 gap-4 lg:grid-cols-4"
          >
            <MetricCard
              icon={
                <Zap
                  size={20}
                  className="text-blue-400"
                />
              }
              label="Total Sessions"
              value={totalSessions}
              borderColor="border-blue-500/30"
            />

            <MetricCard
              icon={
                <RefreshCw
                  size={20}
                  className="text-orange-400"
                />
              }
              label="Recurring Issues"
              value={recurringCount}
              borderColor="border-orange-500/30"
            />

            <MetricCard
              icon={
                <AlertTriangle
                  size={20}
                  className="text-red-400"
                />
              }
              label="Escalated"
              value={escalatedCount}
              borderColor="border-red-500/30"
            />

            <MetricCard
              icon={
                <CalendarDays
                  size={20}
                  className="text-cyan-400"
                />
              }
              label="This Month"
              value={thisMonthCount}
              borderColor="border-cyan-500/30"
            />
          </motion.div>

          <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-4">
            <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
              <div className="flex flex-wrap gap-2">
                {filters.map(
                  (item) => (
                    <FilterButton
                      key={item}
                      label={item}
                      active={
                        filter === item
                      }
                      onClick={() =>
                        setFilter(item)
                      }
                    />
                  )
                )}
              </div>

              <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                <label className="relative block">
                  <Search
                    size={16}
                    className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-slate-500"
                  />

                  <input
                    value={search}
                    onChange={(event) =>
                      setSearch(
                        event.target.value
                      )
                    }
                    placeholder="Search symptom or cause..."
                    className="h-10 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] pl-11 pr-4 text-sm text-white outline-none placeholder:text-slate-500 focus:border-cyan-500/50 sm:w-[300px]"
                  />
                </label>

                <motion.button
                  type="button"
                  onClick={fetchSessions}
                  disabled={loading}
                  whileTap={buttonTap}
                  className="inline-flex h-10 items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 text-sm font-bold text-white transition hover:border-cyan-500/40 hover:text-cyan-400 disabled:cursor-wait disabled:opacity-70"
                >
                  <RefreshCw
                    size={16}
                    className={
                      loading
                        ? 'animate-spin text-cyan-400'
                        : 'text-slate-400'
                    }
                  />

                  Refresh
                </motion.button>
              </div>
            </div>
          </section>

          <section className="overflow-hidden rounded-2xl border border-[var(--rigmd-border)] bg-[#101821]">
            <div className="flex items-center justify-between border-b border-[var(--rigmd-border)] px-5 py-4">
              <div className="flex items-center gap-3">
                <RefreshCw
                  size={15}
                  className={
                    loading
                      ? 'animate-spin text-cyan-400'
                      : 'text-cyan-400'
                  }
                />

                <h3 className="font-bold uppercase tracking-wider text-white">
                  Sessions
                </h3>

                <span className="rounded-full bg-[var(--rigmd-card-soft)] px-2 py-0.5 text-xs font-bold text-slate-400">
                  {filteredSessions.length}
                </span>
              </div>

              <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-500">
                <Clock3 size={14} />
                Newest First
              </div>
            </div>

            <div className="overflow-x-auto">
              <div className="min-w-[1380px]">
                <div className="grid grid-cols-[130px_minmax(190px,1fr)_minmax(280px,1.35fr)_140px_140px_170px_120px] border-b border-[var(--rigmd-border)] bg-[#0f1824] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.24em] text-slate-500">
                  <div>
                    Date
                  </div>

                  <div>
                    Symptom
                  </div>

                  <div className="text-center">
                    Probable Cause
                  </div>

                  <div className="text-center">
                    Action
                  </div>

                  <div className="text-center">
                    Confidence
                  </div>

                  <div className="text-center">
                    Follow-up Status
                  </div>

                  <div className="text-center">
                    Details
                  </div>
                </div>

                {loading ? (
                  <div>
                    {Array.from({ length: 6 }).map((_, index) => (
                      <TableRowSkeleton key={index} />
                    ))}
                  </div>
                ) : error ? (
                  <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                    <AlertTriangle
                      size={42}
                      className="mb-4 text-red-400"
                    />

                    <h3 className="text-lg font-bold text-white">
                      Diagnostic history is unavailable
                    </h3>

                    <p className="mt-2 max-w-md text-sm leading-relaxed text-slate-500">
                      Check that the backend API is running, then refresh this view.
                    </p>

                    <motion.button
                      type="button"
                      onClick={fetchSessions}
                      whileTap={buttonTap}
                      className="mt-4 inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2 text-sm font-bold text-white transition hover:border-cyan-500/40 hover:text-cyan-400"
                    >
                      <RefreshCw
                        size={15}
                      />
                      Try Again
                    </motion.button>
                  </div>
                ) : filteredSessions.length ===
                  0 ? (
                  <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                    <FileText
                      size={42}
                      className="mb-4 text-slate-600"
                    />

                    <h3 className="text-lg font-bold text-white">
                      {totalSessions === 0 && !hasActiveFilters
                        ? 'No diagnostic sessions yet'
                        : 'No sessions match your filters'}
                    </h3>

                    <p className="mt-2 max-w-md text-sm leading-relaxed text-slate-500">
                      {totalSessions === 0 && !hasActiveFilters
                        ? 'Complete a guided diagnosis to start building your history.'
                        : 'Try clearing the search or selecting All Sessions.'}
                    </p>

                    {totalSessions === 0 && !hasActiveFilters ? (
                      onStartNewDiagnosis && (
                        <motion.button
                          type="button"
                          onClick={onStartNewDiagnosis}
                          whileTap={buttonTap}
                          className="mt-5 inline-flex items-center justify-center rounded-lg bg-[#1fb6c9] px-4 py-2.5 text-sm font-bold text-[#041014] transition hover:bg-[#38c7d7]"
                        >
                          Start New Diagnosis
                        </motion.button>
                      )
                    ) : (
                      <motion.button
                        type="button"
                        onClick={resetFilters}
                        whileTap={buttonTap}
                        className="mt-5 inline-flex items-center justify-center rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2.5 text-sm font-bold text-white transition hover:border-cyan-500/40 hover:text-cyan-400"
                      >
                        Reset Filters
                      </motion.button>
                    )}
                  </div>
                ) : (
                  <motion.div variants={staggerContainer} initial="hidden" animate="visible">
                    {visibleSessions.map(
                    (session) => (
                      <SessionRow
                        key={
                          session.session_id
                        }
                        session={
                          session
                        }
                        onViewSession={
                          onViewSession
                        }
                        selected={
                          selectedSessionId ===
                          session.session_id
                        }
                      />
                    )
                    )}

                    {filteredSessions.length >
                      DEFAULT_VISIBLE_SESSIONS && (
                      <div className="flex items-center justify-center border-t border-[var(--rigmd-border)] px-5 py-4">
                        {hasMoreSessions ? (
                          <motion.button
                            type="button"
                            onClick={() =>
                              setVisibleCount(
                                (current) =>
                                  Math.min(
                                    current +
                                      SHOW_MORE_INCREMENT,
                                    filteredSessions.length
                                  )
                              )
                            }
                            whileTap={buttonTap}
                            className="inline-flex items-center justify-center rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2.5 text-sm font-bold text-slate-200 transition hover:border-cyan-500/40 hover:text-cyan-400"
                          >
                            Show More Sessions
                          </motion.button>
                        ) : (
                          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                            All sessions loaded
                          </p>
                        )}
                      </div>
                    )}
                  </motion.div>
                )}
              </div>
            </div>
          </section>
        </div>
      </motion.div>
    </>
  );
}
