import { useCallback, useEffect, useMemo, useState } from 'react';
import axios from 'axios';
import { AnimatePresence, motion } from 'motion/react';
import {
  AlertCircle,
  AlertTriangle,
  BarChart3,
  ChevronDown,
  ChevronUp,
  Download,
  Eye,
  FileText,
  Info,
  RefreshCw,
  Search,
  Zap,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import { accordionReveal, buttonTap, cardFadeUp, cardTransition, hoverLift, pageFade, pageTransition, staggerContainer } from '../lib/motion';
import type { PageKey } from '../types/rigmd';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5273';

interface RecurringMetrics {
  recurring_issues: number;
  worsening_trends: number;
  action_escalated: number;
  total_occurrences: number;
}

interface PatternOccurrence {
  id: string;
  session_code: string;
  display_date: string;
  short_date: string;
  display_time: string;
  symptom: string;
  probable_cause: string;
  action_category: string;
  confidence_label: string;
  severity: string;
  frequency: string;
  duration: string;
  warning_signs: string[];
  status: string;
}

interface PatternTimelineDot {
  date: string;
  full_date: string;
  session_id: string;
}

interface RecurringPattern {
  id: string;
  pattern_key: string;
  symptom: string;
  probable_cause: string;
  occurrence_count: number;
  first_detected: string;
  latest_detected: string;
  previous_action: string;
  updated_action: string;
  status: string;
  action_escalated: boolean;
  recommended_next_step: string;
  occurrences: PatternOccurrence[];
  timeline: PatternTimelineDot[];
}

interface PatternTimelineRow {
  pattern_id: string;
  date: string;
  symptom: string;
  probable_cause: string;
  action_category: string;
  confidence_label: string;
  status: string;
}

interface RecurringResponse {
  metrics: RecurringMetrics;
  patterns: RecurringPattern[];
  timeline: PatternTimelineRow[];
  database_warning?: string;
}

const emptyRecurring: RecurringResponse = {
  metrics: {
    recurring_issues: 0,
    worsening_trends: 0,
    action_escalated: 0,
    total_occurrences: 0,
  },
  patterns: [],
  timeline: [],
};

const patternFilters = [
  'All',
  'Stable',
  'Worsening',
  'Improving',
  'Escalated',
  'Maintain',
  'Troubleshoot',
  'Escalate',
];

const DEFAULT_VISIBLE_OCCURRENCES = 5;
const OCCURRENCE_INCREMENT = 5;

function getSummaryCardTone(value: number, activeTone: 'cyan' | 'red' | 'orange') {
  if (value <= 0) {
    return {
      borderColor: 'border-slate-500/25',
      valueColor: 'text-slate-400',
      iconColor: 'text-slate-500',
    };
  }

  if (activeTone === 'red') {
    return {
      borderColor: 'border-red-400/30',
      valueColor: 'text-red-300',
      iconColor: 'text-red-300',
    };
  }

  if (activeTone === 'orange') {
    return {
      borderColor: 'border-amber-400/30',
      valueColor: 'text-amber-300',
      iconColor: 'text-amber-300',
    };
  }

  return {
    borderColor: 'border-cyan-400/30',
    valueColor: 'text-cyan-300',
    iconColor: 'text-cyan-300',
  };
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

function getStatusStyle(status: string) {
  const value = status.toLowerCase();

  if (value.includes('worsening')) {
    return 'border-red-400/35 bg-red-400/10 text-red-300';
  }

  if (value.includes('improving')) {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  if (value.includes('escalated')) {
    return 'border-red-400/40 bg-red-400/10 text-red-300';
  }

  return 'border-cyan-400/25 bg-cyan-400/[0.06] text-cyan-300';
}

function getConfidenceStyle(confidence: string) {
  const value = confidence.toLowerCase();

  if (value.includes('high')) {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  if (value.includes('moderate') || value.includes('medium')) {
    return 'border-amber-400/35 bg-amber-400/10 text-amber-300';
  }

  return 'border-red-400/30 bg-red-400/10 text-red-300';
}

function getPatternBorder(status: string) {
  const value = status.toLowerCase();

  if (value.includes('worsening')) {
    return 'border-l-red-500';
  }

  if (value.includes('improving')) {
    return 'border-l-cyan-400';
  }

  return 'border-l-slate-600';
}

function groupTimelineOccurrencesByDate(timeline: PatternTimelineDot[]) {
  const grouped = new Map<string, { label: string; count: number; sessionIds: string[] }>();

  timeline.forEach((item) => {
    const key = item.full_date || item.date;
    const existing = grouped.get(key);

    if (existing) {
      existing.count += 1;
      existing.sessionIds.push(item.session_id);
      return;
    }

    grouped.set(key, {
      label: item.date,
      count: 1,
      sessionIds: [item.session_id],
    });
  });

  return Array.from(grouped.values());
}

function getRecommendedNextStep(pattern: RecurringPattern) {
  if (pattern.recommended_next_step) {
    return pattern.recommended_next_step;
  }

  return `Continue ${pattern.updated_action.toLowerCase()} actions related to ${pattern.symptom}. Monitor if the same symptom appears again in the next diagnostic session.`;
}

function filterPatterns(patterns: RecurringPattern[], filter: string, search: string) {
  const query = search.trim().toLowerCase();

  return patterns.filter((pattern) => {
    const status = pattern.status.toLowerCase();
    const previousAction = pattern.previous_action.toLowerCase();
    const updatedAction = pattern.updated_action.toLowerCase();

    if (filter === 'Escalated' && !pattern.action_escalated && !status.includes('escalated')) {
      return false;
    }

    if (
      filter !== 'All' &&
      filter !== 'Escalated' &&
      !status.includes(filter.toLowerCase()) &&
      !previousAction.includes(filter.toLowerCase()) &&
      !updatedAction.includes(filter.toLowerCase())
    ) {
      return false;
    }

    if (!query) {
      return true;
    }

    return (
      pattern.symptom.toLowerCase().includes(query) ||
      pattern.probable_cause.toLowerCase().includes(query)
    );
  });
}

function MetricCard({
  icon,
  label,
  value,
  borderColor,
  valueColor,
}: {
  icon: React.ReactNode;
  label: string;
  value: number;
  borderColor: string;
  valueColor: string;
}) {
  return (
    <motion.section variants={cardFadeUp} transition={cardTransition} className={`flex h-full min-h-[104px] items-center rounded-2xl border bg-[var(--rigmd-card)] p-5 ${borderColor}`}>
      <div className="flex items-center gap-4">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--rigmd-bg)]">
          {icon}
        </div>

        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">{label}</p>
          <h3 className={`text-3xl font-bold ${valueColor}`}>{value}</h3>
        </div>
      </div>
    </motion.section>
  );
}

function ActionBadge({ action }: { action: string }) {
  return (
    <span className={`inline-flex items-center justify-center rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase ${getActionStyle(action)}`}>
      {action}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-3 py-1 text-xs font-bold ${getStatusStyle(status)}`}>
      {status === 'Worsening' ? <AlertTriangle size={12} /> : <RefreshCw size={12} />}
      {status}
    </span>
  );
}

function ConfidenceBadge({ confidence }: { confidence: string }) {
  return (
    <span className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase ${getConfidenceStyle(confidence)}`}>
      {confidence}
    </span>
  );
}

function PatternDots({ pattern }: { pattern: RecurringPattern }) {
  const groupedTimeline = groupTimelineOccurrencesByDate(pattern.timeline);

  return (
    <div className="mt-4 flex items-start gap-0 overflow-x-auto pb-1">
      {groupedTimeline.map((item, index) => (
        <motion.div
          key={`${item.label}-${index}`}
          variants={cardFadeUp}
          transition={{ ...cardTransition, delay: index * 0.04 }}
          className="flex min-w-[96px] flex-1 items-start"
        >
          <div className="relative flex flex-col items-center">
            <span className="h-2.5 w-2.5 rounded-full bg-cyan-400" />
            <span className="mt-2 text-xs font-semibold text-white">{item.label}</span>
            {item.count > 1 && (
              <span className="mt-0.5 text-[11px] font-medium text-slate-500">
                {item.count} sessions
              </span>
            )}
          </div>

          {index < groupedTimeline.length - 1 && <div className="mx-2 mt-1.5 h-px flex-1 bg-[var(--rigmd-border)]" />}
        </motion.div>
      ))}
    </div>
  );
}

function PatternCard({
  pattern,
  expanded,
  onToggle,
  onViewHistory,
}: {
  pattern: RecurringPattern;
  expanded: boolean;
  onToggle: () => void;
  onViewHistory?: () => void;
}) {
  const [visibleOccurrences, setVisibleOccurrences] = useState(DEFAULT_VISIBLE_OCCURRENCES);

  useEffect(() => {
    if (!expanded) {
      setVisibleOccurrences(DEFAULT_VISIBLE_OCCURRENCES);
    }
  }, [expanded, pattern.id]);

  const shownOccurrences = pattern.occurrences.slice(0, visibleOccurrences);
  const hasMoreOccurrences = shownOccurrences.length < pattern.occurrences.length;

  return (
    <motion.section
      variants={cardFadeUp}
      whileHover={hoverLift}
      transition={{ duration: 0.18 }}
      className={`overflow-hidden rounded-2xl border border-l-2 transition-colors ${
        expanded
          ? 'border-cyan-400/35 border-l-cyan-400 bg-[var(--rigmd-card-hover)]'
          : `border-[var(--rigmd-border)] bg-[var(--rigmd-card)] ${getPatternBorder(pattern.status)}`
      }`}
    >
      <motion.button type="button" onClick={onToggle} whileTap={buttonTap} className="w-full px-5 py-5 text-left transition hover:bg-[var(--rigmd-card-hover)]">
        <div className="grid grid-cols-[64px_minmax(0,1fr)_260px_170px] items-center gap-5">
          <div className="flex h-14 w-14 flex-col items-center justify-center rounded-full bg-[var(--rigmd-card-soft)]">
            <span className="text-xl font-bold text-cyan-400">{pattern.occurrence_count}</span>
            <span className="text-[11px] text-slate-500">times</span>
          </div>

          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="text-lg font-bold text-white">{pattern.symptom}</h3>
              <StatusBadge status={pattern.status} />

              {pattern.action_escalated && (
                <span className="inline-flex items-center gap-1 rounded-full border border-red-500/50 bg-red-500/10 px-3 py-1 text-xs font-bold text-red-400">
                  <AlertCircle size={12} />
                  Escalated
                </span>
              )}
            </div>

            <p className="mt-1 text-sm text-slate-400">{pattern.probable_cause}</p>

            <div className="mt-3 flex flex-wrap items-center gap-5 text-xs text-slate-500">
              <span>First: {pattern.first_detected}</span>
              <span>Latest: {pattern.latest_detected}</span>
            </div>

            <PatternDots pattern={pattern} />
          </div>

          <div className="flex items-center justify-center gap-3">
            <ActionBadge action={pattern.previous_action} />
            <span className="text-slate-500">→</span>
            <ActionBadge action={pattern.updated_action} />
          </div>

          <div className="flex justify-end">
            <span className="inline-flex min-w-[144px] items-center justify-center gap-2 whitespace-nowrap rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] px-4 py-2 text-xs font-semibold text-slate-400">
              {expanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
              {expanded ? 'Collapse Details' : 'View Details'}
            </span>
          </div>
        </div>
      </motion.button>

      <AnimatePresence initial={false}>
        {expanded && (
        <motion.div
          variants={accordionReveal}
          initial="hidden"
          animate="visible"
          exit="exit"
          transition={{ duration: 0.25 }}
          className="overflow-hidden border-t border-[var(--rigmd-border)] bg-[var(--rigmd-sidebar)]"
        >
        <div className="px-5 py-5">
          <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
            <div className="space-y-4">
              <section className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <p className="mb-2 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                  Recommended Next Step
                </p>
                <p className="text-sm leading-relaxed text-white">{getRecommendedNextStep(pattern)}</p>
              </section>

              <section className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <div className="mb-3 flex items-center justify-between">
                  <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">Occurrences</p>
                  <span className="text-xs font-semibold text-cyan-400">{pattern.occurrences.length} related sessions</span>
                </div>

                <div className="space-y-2">
                  {shownOccurrences.map((occurrence) => (
                    <div
                      key={occurrence.id}
                      className="grid grid-cols-[120px_minmax(0,1fr)_160px_160px] items-center gap-4 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] px-4 py-3"
                    >
                      <div>
                        <p className="text-sm font-bold text-white">{occurrence.display_date}</p>
                        <p className="text-xs text-slate-500">{occurrence.display_time}</p>
                      </div>

                      <div className="min-w-0">
                        <p className="truncate font-semibold text-white">{occurrence.symptom}</p>
                        <p className="truncate text-sm text-slate-500">{occurrence.probable_cause}</p>
                      </div>

                      <div className="flex justify-center">
                        <ActionBadge action={occurrence.action_category} />
                      </div>

                      <div className="flex justify-center">
                        <ConfidenceBadge confidence={occurrence.confidence_label} />
                      </div>
                    </div>
                  ))}
                </div>

                {pattern.occurrences.length > DEFAULT_VISIBLE_OCCURRENCES && (
                  <div className="mt-4 flex items-center justify-center">
                    {hasMoreOccurrences ? (
                      <motion.button
                        type="button"
                        onClick={() =>
                          setVisibleOccurrences((current) =>
                            Math.min(current + OCCURRENCE_INCREMENT, pattern.occurrences.length)
                          )
                        }
                        whileTap={buttonTap}
                        className="rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2 text-sm font-bold text-slate-300 transition hover:border-cyan-500/40 hover:text-cyan-400"
                      >
                        Show More Occurrences
                      </motion.button>
                    ) : (
                      <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                        All occurrences shown
                      </p>
                    )}
                  </div>
                )}
              </section>
            </div>

            <aside className="space-y-4">
              <section className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <p className="mb-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                  Pattern Summary
                </p>

                <div className="space-y-3 text-sm">
                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Previous Action</span>
                    <ActionBadge action={pattern.previous_action} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Updated Action</span>
                    <ActionBadge action={pattern.updated_action} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Trend Status</span>
                    <StatusBadge status={pattern.status} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Occurrences</span>
                    <span className="font-bold text-white">{pattern.occurrence_count}</span>
                  </div>
                </div>
              </section>

              <motion.button
                type="button"
                onClick={onViewHistory}
                whileTap={buttonTap}
                className="flex w-full items-center justify-center gap-2 rounded-xl border border-cyan-500/30 bg-cyan-500/10 px-4 py-3 text-sm font-bold text-cyan-400 transition hover:bg-cyan-500/20"
              >
                <Eye size={16} />
                View Related Sessions
              </motion.button>

              <motion.button
                type="button"
                whileTap={buttonTap}
                className="flex w-full items-center justify-center gap-2 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-3 text-sm font-bold text-slate-400 transition hover:border-cyan-500/40 hover:text-cyan-400"
              >
                <Download size={16} />
                Export Pattern Report
              </motion.button>
            </aside>
          </div>
        </div>
        </motion.div>
        )}
      </AnimatePresence>
    </motion.section>
  );
}

function PatternTimelineTable({ rows }: { rows: PatternTimelineRow[] }) {
  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={cardTransition}
      className="overflow-hidden rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)]"
    >
      <div className="border-b border-[var(--rigmd-border)] px-5 py-4">
        <div className="flex items-center gap-2">
          <Zap size={15} className="text-cyan-400" />
          <h3 className="font-bold uppercase tracking-wider text-white">Pattern Timeline</h3>
        </div>
        <p className="mt-1 text-sm text-slate-500">
          All diagnostic sessions contributing to recurring patterns.
        </p>
      </div>

      <div className="grid grid-cols-[150px_minmax(170px,1fr)_minmax(220px,1fr)_180px_190px_150px] border-b border-[var(--rigmd-border)] bg-[var(--rigmd-sidebar)] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
        <div>Date</div>
        <div>Symptom</div>
        <div className="text-center">Probable Cause</div>
        <div className="text-center">Action</div>
        <div className="text-center">Confidence</div>
        <div className="text-center">Trend Status</div>
      </div>

      {rows.length === 0 ? (
        <div className="flex min-h-[220px] flex-col items-center justify-center px-6 py-12 text-center">
          <FileText size={42} className="mb-4 text-slate-600" />
          <h3 className="text-lg font-bold text-white">No recurring timeline yet</h3>
          <p className="mt-2 max-w-md text-sm text-slate-500">
            Timeline entries will appear when the same symptom or probable cause appears across multiple diagnostic sessions.
          </p>
        </div>
      ) : (
        rows.map((row, index) => (
          <motion.div
            key={`${row.pattern_id}-${row.date}-${index}`}
            variants={cardFadeUp}
            transition={{ ...cardTransition, delay: index * 0.03 }}
            className="grid grid-cols-[150px_minmax(170px,1fr)_minmax(220px,1fr)_180px_190px_150px] items-center border-b border-[var(--rigmd-border)] px-5 py-4 last:border-b-0"
          >
            <div className="text-sm font-semibold text-white">{row.date}</div>
            <div className="font-bold text-white">{row.symptom}</div>
            <div className="text-center text-sm text-slate-400">{row.probable_cause}</div>
            <div className="flex justify-center">
              <ActionBadge action={row.action_category} />
            </div>
            <div className="flex justify-center">
              <ConfidenceBadge confidence={row.confidence_label} />
            </div>
            <div className="flex justify-center">
              <StatusBadge status={row.status} />
            </div>
          </motion.div>
        ))
      )}
    </motion.section>
  );
}

export default function RecurringPatternsView({
  setActivePage,
}: {
  setActivePage?: (page: PageKey) => void;
}) {
  const [data, setData] = useState<RecurringResponse>(emptyRecurring);
  const [expandedPatternId, setExpandedPatternId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [filter, setFilter] = useState('All');
  const [search, setSearch] = useState('');

  const fetchPatterns = useCallback(async () => {
    setIsLoading(true);

    try {
      const response = await axios.get<RecurringResponse>(`${API_BASE_URL}/api/recurring/patterns`);
      setData(response.data);
      setLoadError(null);
    } catch {
      setData(emptyRecurring);
      setLoadError('Recurring pattern data is not available yet. Check your backend or database connection.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPatterns();

    const interval = window.setInterval(fetchPatterns, 5000);

    return () => window.clearInterval(interval);
  }, [fetchPatterns]);

  const filteredPatterns = useMemo(
    () => filterPatterns(data.patterns, filter, search),
    [data.patterns, filter, search]
  );

  useEffect(() => {
    if (!filteredPatterns.some((pattern) => pattern.id === expandedPatternId)) {
      setExpandedPatternId(null);
    }
  }, [expandedPatternId, filteredPatterns]);

  const resetFilters = () => {
    setFilter('All');
    setSearch('');
  };

  const recurringTone = getSummaryCardTone(data.metrics.recurring_issues, 'cyan');
  const worseningTone = getSummaryCardTone(data.metrics.worsening_trends, 'red');
  const escalatedTone = getSummaryCardTone(data.metrics.action_escalated, 'orange');
  const occurrencesTone = getSummaryCardTone(data.metrics.total_occurrences, 'cyan');

  return (
    <>
      <TopHeader
        title="Recurring Patterns"
        subtitle="Symptoms and causes detected across multiple diagnostic sessions"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8"
      >
        <div className="mx-auto w-full max-w-[1360px] space-y-5">
          {(data.database_warning || loadError) && (
            <div className="rounded-xl border border-amber-400/30 bg-amber-400/10 px-4 py-3 text-sm text-amber-200">
              Recurring pattern data is not available yet. Check your backend or database connection.
            </div>
          )}

          <motion.div variants={staggerContainer} initial="hidden" animate="visible" className="grid grid-cols-1 gap-4 lg:grid-cols-4">
            <MetricCard
              icon={<RefreshCw size={20} className={recurringTone.iconColor} />}
              label="Recurring Issues"
              value={data.metrics.recurring_issues}
              borderColor={recurringTone.borderColor}
              valueColor={recurringTone.valueColor}
            />

            <MetricCard
              icon={<AlertTriangle size={20} className={worseningTone.iconColor} />}
              label="Worsening Trends"
              value={data.metrics.worsening_trends}
              borderColor={worseningTone.borderColor}
              valueColor={worseningTone.valueColor}
            />

            <MetricCard
              icon={<AlertCircle size={20} className={escalatedTone.iconColor} />}
              label="Action Escalated"
              value={data.metrics.action_escalated}
              borderColor={escalatedTone.borderColor}
              valueColor={escalatedTone.valueColor}
            />

            <MetricCard
              icon={<BarChart3 size={20} className={occurrencesTone.iconColor} />}
              label="Total Occurrences"
              value={data.metrics.total_occurrences}
              borderColor={occurrencesTone.borderColor}
              valueColor={occurrencesTone.valueColor}
            />
          </motion.div>

          <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5">
            <div className="flex gap-3">
              <Info size={18} className="mt-0.5 shrink-0 text-cyan-400" />

              <div>
                <h3 className="font-bold text-white">Pattern Escalation Advisory</h3>
                <p className="mt-1 text-sm leading-relaxed text-slate-400">
                  Recurring issues may increase the action level from{' '}
                  <span className="font-bold text-cyan-300">Monitor</span> to{' '}
                  <span className="font-bold text-amber-300">Troubleshoot</span> or{' '}
                  <span className="font-bold text-red-300">Escalate</span> when the same symptom or probable cause
                  appears across multiple diagnostic sessions.
                </p>
              </div>
            </div>
          </section>

          <section>
            <div className="mb-4 space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex flex-1 items-center gap-3">
                  <RefreshCw size={15} className={isLoading ? 'animate-spin text-cyan-400' : 'text-cyan-400'} />
                  <h3 className="text-sm font-bold uppercase tracking-[0.18em] text-white">Detected Patterns</h3>
                  <div className="h-px flex-1 bg-[var(--rigmd-border)]" />
                </div>

                <p className="ml-4 text-xs text-slate-500">
                  {filteredPatterns.length} of {data.patterns.length} patterns found
                </p>
              </div>

              {data.patterns.length > 0 && (
                <div className="flex flex-col gap-3 rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4 xl:flex-row xl:items-center xl:justify-between">
                  <div className="flex flex-wrap gap-2">
                    {patternFilters.map((item) => (
                      <motion.button
                        key={item}
                        type="button"
                        onClick={() => setFilter(item)}
                        whileTap={buttonTap}
                        className={`rounded-full border px-3.5 py-2 text-xs font-bold uppercase transition ${
                          filter === item
                            ? 'border-cyan-500/80 bg-cyan-500/10 text-cyan-300'
                            : 'border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] text-slate-500 hover:border-cyan-500/40 hover:text-cyan-400'
                        }`}
                      >
                        {item}
                      </motion.button>
                    ))}
                  </div>

                  <label className="relative block">
                    <Search
                      size={16}
                      className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-slate-500"
                    />

                    <input
                      value={search}
                      onChange={(event) => setSearch(event.target.value)}
                      placeholder="Search symptom or probable cause..."
                      className="h-10 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] pl-11 pr-4 text-sm text-white outline-none placeholder:text-slate-500 focus:border-cyan-500/50 xl:w-[340px]"
                    />
                  </label>
                </div>
              )}
            </div>

            {isLoading && data.patterns.length === 0 ? (
              <div className="space-y-4">
                {Array.from({ length: 3 }).map((_, index) => (
                  <div key={index} className="h-32 animate-pulse rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)]" />
                ))}
              </div>
            ) : data.patterns.length === 0 ? (
              <div className="flex min-h-[280px] flex-col items-center justify-center rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-6 py-12 text-center">
                <FileText size={42} className="mb-4 text-slate-600" />
                <h3 className="text-lg font-bold text-white">No recurring patterns detected yet.</h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  RigMD will identify repeated symptoms or probable causes after multiple diagnostic sessions.
                </p>
                {setActivePage && (
                  <motion.button
                    type="button"
                    onClick={() => setActivePage('diagnosticHistory')}
                    whileTap={buttonTap}
                    className="mt-5 inline-flex items-center justify-center rounded-lg border border-cyan-400/30 bg-cyan-400/10 px-4 py-2.5 text-sm font-bold text-cyan-300 transition hover:border-cyan-300/60 hover:bg-cyan-400/15"
                  >
                    View Diagnostic History
                  </motion.button>
                )}
              </div>
            ) : filteredPatterns.length === 0 ? (
              <div className="flex min-h-[260px] flex-col items-center justify-center rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-6 py-12 text-center">
                <FileText size={42} className="mb-4 text-slate-600" />
                <h3 className="text-lg font-bold text-white">No patterns match your filters.</h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  Try clearing the search or selecting All.
                </p>
                <motion.button
                  type="button"
                  onClick={resetFilters}
                  whileTap={buttonTap}
                  className="mt-5 inline-flex items-center justify-center rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2.5 text-sm font-bold text-slate-300 transition hover:border-cyan-500/40 hover:text-cyan-400"
                >
                  Reset Filters
                </motion.button>
              </div>
            ) : (
              <motion.div variants={staggerContainer} initial="hidden" animate="visible" className="space-y-4">
                {filteredPatterns.map((pattern) => (
                  <PatternCard
                    key={pattern.id}
                    pattern={pattern}
                    expanded={expandedPatternId === pattern.id}
                    onViewHistory={() => setActivePage?.('diagnosticHistory')}
                    onToggle={() =>
                      setExpandedPatternId(expandedPatternId === pattern.id ? null : pattern.id)
                    }
                  />
                ))}
              </motion.div>
            )}
          </section>

          <PatternTimelineTable rows={data.timeline} />
        </div>
      </motion.div>
    </>
  );
}
