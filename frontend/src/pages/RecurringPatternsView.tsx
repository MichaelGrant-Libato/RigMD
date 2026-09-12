import { useCallback, useEffect, useMemo, useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import {
  AlertCircle,
  AlertTriangle,
  BarChart3,
  ChevronDown,
  ChevronUp,
  Eye,
  FileText,
  Info,
  RefreshCw,
  Search,
  Trash2,
  Zap,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import DeleteConfirmationDialog from '../components/DeleteConfirmationDialog';
import { accordionReveal, buttonTap, cardFadeUp, cardTransition, hoverLift, pageFade, pageTransition, staggerContainer } from '../lib/motion';
import { apiDelete, apiGet } from '../lib/api';
import type { PageKey } from '../types/rigmd';

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

function getFilterLabel(filter: string) {
  const labels: Record<string, string> = {
    All: 'All',
    Stable: 'No change',
    Worsening: 'Getting worse',
    Improving: 'Getting better',
    Escalated: 'Needs help',
    Maintain: 'Care tips',
    Troubleshoot: 'Try a fix',
    Escalate: 'Get help',
  };

  return labels[filter] ?? filter;
}

function getFriendlyAction(action: string) {
  const value = action.toLowerCase();

  if (value.includes('monitor')) {
    return 'Watch for now';
  }

  if (value.includes('maintain')) {
    return 'Do simple care';
  }

  if (value.includes('troubleshoot')) {
    return 'Try a safe fix';
  }

  if (value.includes('escalate')) {
    return 'Get help';
  }

  return action || 'Not sure yet';
}

function getFriendlyStatus(status: string) {
  const value = status.toLowerCase();

  if (value.includes('worsening')) {
    return 'Getting worse';
  }

  if (value.includes('improving')) {
    return 'Getting better';
  }

  if (value.includes('escalated')) {
    return 'Needs help';
  }

  return 'Not changing';
}

function getFriendlyConfidence(confidence: string) {
  const value = confidence.toLowerCase();

  if (value.includes('high')) {
    return 'Strong match';
  }

  if (value.includes('moderate') || value.includes('medium')) {
    return 'Possible match';
  }

  return 'Needs more checks';
}

function getPatternMeaning(pattern: RecurringPattern) {
  if (pattern.status.toLowerCase().includes('worsening')) {
    return 'This has shown up more than once and may need attention soon.';
  }

  if (pattern.action_escalated) {
    return 'RigMD thinks this repeated problem may need outside help.';
  }

  if (pattern.updated_action.toLowerCase().includes('monitor')) {
    return 'This has repeated, but RigMD does not recommend changing anything yet.';
  }

  return 'This repeated enough times that RigMD recommends a next step.';
}

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

  if (pattern.updated_action.toLowerCase().includes('monitor')) {
    return 'No fix is recommended right now. Keep using the computer normally and run another check if this keeps happening.';
  }

  return `Recommended next step: ${getFriendlyAction(pattern.updated_action).toLowerCase()} for ${pattern.probable_cause || pattern.symptom}.`;
}

function isNoIssueResult(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase() === 'no active issue detected';
}

function isActionablePattern(pattern: RecurringPattern) {
  return !isNoIssueResult(pattern.probable_cause) && !isNoIssueResult(pattern.symptom);
}

function isActionableTimelineRow(row: PatternTimelineRow) {
  return !isNoIssueResult(row.probable_cause) && !isNoIssueResult(row.symptom);
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
        <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)]">
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
    <span className={`inline-flex items-center justify-center rounded-full border px-2.5 py-1 text-[11px] font-bold ${getActionStyle(action)}`}>
      {getFriendlyAction(action)}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-3 py-1 text-xs font-bold ${getStatusStyle(status)}`}>
      {status === 'Worsening' ? <AlertTriangle size={12} /> : <RefreshCw size={12} />}
      {getFriendlyStatus(status)}
    </span>
  );
}

function ConfidenceBadge({ confidence }: { confidence: string }) {
  return (
    <span className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-bold ${getConfidenceStyle(confidence)}`}>
      {getFriendlyConfidence(confidence)}
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
                {item.count} checks
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
  onDeletePattern,
  deleting,
}: {
  pattern: RecurringPattern;
  expanded: boolean;
  onToggle: () => void;
  onViewHistory?: () => void;
  onDeletePattern?: () => void;
  deleting?: boolean;
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
          ? 'border-cyan-300/55 border-l-cyan-300 bg-[#12343a]'
          : `border-[var(--rigmd-border)] bg-[var(--rigmd-card)] ${getPatternBorder(pattern.status)}`
      }`}
    >
      <motion.button type="button" onClick={onToggle} whileTap={buttonTap} className="w-full px-5 py-5 text-left transition hover:bg-[var(--rigmd-card-hover)]">
        <div className="grid grid-cols-[64px_minmax(0,1fr)_260px_170px] items-center gap-5">
          <div className="flex h-14 w-14 flex-col items-center justify-center rounded-full bg-[var(--rigmd-card-soft)]">
            <span className="text-xl font-bold text-cyan-400">{pattern.occurrence_count}</span>
            <span className="text-[11px] text-slate-500">checks</span>
          </div>

          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="text-lg font-bold text-white">This keeps showing up</h3>
              <StatusBadge status={pattern.status} />

              {pattern.action_escalated && (
                <span className="inline-flex items-center gap-1 rounded-full border border-red-500/50 bg-red-500/10 px-3 py-1 text-xs font-bold text-red-400">
                  <AlertCircle size={12} />
                  Needs help
                </span>
              )}
            </div>

            <p className="mt-1 text-sm font-semibold text-slate-200">{pattern.probable_cause}</p>
            <p className="mt-1 text-sm text-slate-500">{getPatternMeaning(pattern)}</p>

            <div className="mt-3 flex flex-wrap items-center gap-5 text-xs text-slate-500">
              <span>First noticed: {pattern.first_detected}</span>
              <span>Last seen: {pattern.latest_detected}</span>
            </div>

            <PatternDots pattern={pattern} />
          </div>

          <div className="flex items-center justify-center gap-3">
            <ActionBadge action={pattern.updated_action} />
          </div>

          <div className="flex justify-end">
            <span className="inline-flex min-w-[144px] items-center justify-center gap-2 whitespace-nowrap rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2 text-xs font-semibold text-slate-400">
              {expanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
              {expanded ? 'Hide details' : 'Why repeated?'}
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
          className="overflow-hidden border-t border-[var(--rigmd-border)] bg-[#0f1824]"
        >
        <div className="px-5 py-5">
          <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
            <div className="space-y-4">
              <section className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                <p className="mb-2 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                  What to do next
                </p>
                <p className="text-sm leading-relaxed text-white">{getRecommendedNextStep(pattern)}</p>
              </section>

              <section className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                <div className="mb-3 flex items-center justify-between">
                  <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">Checks included</p>
                  <span className="text-xs font-semibold text-cyan-400">{pattern.occurrences.length} related checks</span>
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
                        Show more checks
                      </motion.button>
                    ) : (
                      <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                        All checks shown
                      </p>
                    )}
                  </div>
                )}
              </section>
            </div>

            <aside className="space-y-4">
              <section className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                <p className="mb-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                  Simple summary
                </p>

                <div className="space-y-3 text-sm">
                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Before</span>
                    <ActionBadge action={pattern.previous_action} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Now</span>
                    <ActionBadge action={pattern.updated_action} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Trend</span>
                    <StatusBadge status={pattern.status} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Repeated checks</span>
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
                Open past checks
              </motion.button>

              <section className="rounded-xl border border-red-400/20 bg-red-400/[0.06] p-4">
                <p className="text-sm font-bold text-red-200">Remove this repeated problem?</p>
                <p className="mt-1 text-sm leading-relaxed text-slate-400">
                  This deletes the saved checks that created this repeated problem. It also removes them from Past Checks.
                </p>

                <motion.button
                  type="button"
                  onClick={onDeletePattern}
                  disabled={deleting}
                  whileTap={buttonTap}
                  className="mt-4 flex w-full items-center justify-center gap-2 rounded-xl border border-red-400/35 bg-red-400/10 px-4 py-3 text-sm font-bold text-red-200 transition hover:bg-red-400/15 disabled:cursor-wait disabled:opacity-60"
                >
                  <Trash2 size={16} />
                  {deleting ? 'Deleting...' : 'Delete related checks'}
                </motion.button>
              </section>
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
          <h3 className="font-bold uppercase tracking-wider text-white">Checks that make up these repeats</h3>
        </div>
        <p className="mt-1 text-sm text-slate-500">
          These are the past checks RigMD used to spot repeated problems.
        </p>
      </div>

      <div className="grid grid-cols-[150px_minmax(170px,1fr)_minmax(220px,1fr)_180px_190px_150px] border-b border-[var(--rigmd-border)] bg-[#0f1824] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
        <div>Date</div>
        <div>What happened</div>
        <div className="text-center">What RigMD found</div>
        <div className="text-center">Advice</div>
        <div className="text-center">Match</div>
        <div className="text-center">Trend</div>
      </div>

      {rows.length === 0 ? (
        <div className="flex min-h-[220px] flex-col items-center justify-center px-6 py-12 text-center">
          <FileText size={42} className="mb-4 text-slate-600" />
          <h3 className="text-lg font-bold text-white">No repeated-check timeline yet</h3>
          <p className="mt-2 max-w-md text-sm text-slate-500">
            This will appear after RigMD sees the same kind of issue in more than one check.
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
  const [deletingPatternId, setDeletingPatternId] = useState<string | null>(null);
  const [patternPendingDelete, setPatternPendingDelete] =
    useState<RecurringPattern | null>(null);

  const fetchPatterns = useCallback(async () => {
    setIsLoading(true);

    try {
      const response = await apiGet<RecurringResponse>('/api/recurring/patterns');
      setData(response.data);
      setLoadError(null);
    } catch {
      setData(emptyRecurring);
      setLoadError('Repeated problem data is not available right now. Try again after RigMD finishes syncing.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPatterns();

    const interval = window.setInterval(fetchPatterns, 5000);

    return () => window.clearInterval(interval);
  }, [fetchPatterns]);

  const visiblePatterns = useMemo(
    () => data.patterns.filter(isActionablePattern),
    [data.patterns]
  );

  const visiblePatternIds = useMemo(
    () => new Set(visiblePatterns.map((pattern) => pattern.id)),
    [visiblePatterns]
  );

  const visibleTimeline = useMemo(
    () => data.timeline.filter((row) => visiblePatternIds.has(row.pattern_id) && isActionableTimelineRow(row)),
    [data.timeline, visiblePatternIds]
  );

  const visibleMetrics = useMemo(
    () => ({
      recurring_issues: visiblePatterns.length,
      worsening_trends: visiblePatterns.filter((pattern) => pattern.status.toLowerCase().includes('worsening')).length,
      action_escalated: visiblePatterns.filter((pattern) => pattern.action_escalated).length,
      total_occurrences: visiblePatterns.reduce((total, pattern) => total + pattern.occurrence_count, 0),
    }),
    [visiblePatterns]
  );

  const filteredPatterns = useMemo(
    () => filterPatterns(visiblePatterns, filter, search),
    [visiblePatterns, filter, search]
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

  const deletePatternSessions = async (pattern: RecurringPattern) => {
    if (deletingPatternId) {
      return;
    }

    const sessionIds = Array.from(
      new Set(pattern.occurrences.map((occurrence) => occurrence.id).filter(Boolean))
    );

    if (sessionIds.length === 0) {
      setLoadError('RigMD could not find the saved checks for this repeated problem.');
      return;
    }

    setDeletingPatternId(pattern.id);
    setLoadError(null);

    try {
      await Promise.all(
        sessionIds.map((sessionId) =>
          apiDelete(`/api/diagnosis/sessions/${sessionId}`)
        )
      );

      await fetchPatterns();
      setPatternPendingDelete(null);
    } catch {
      setLoadError('Could not delete this repeated problem. Please try again.');
      await fetchPatterns();
    } finally {
      setDeletingPatternId(null);
    }
  };

  const recurringTone = getSummaryCardTone(visibleMetrics.recurring_issues, 'cyan');
  const worseningTone = getSummaryCardTone(visibleMetrics.worsening_trends, 'red');
  const escalatedTone = getSummaryCardTone(visibleMetrics.action_escalated, 'orange');
  const occurrencesTone = getSummaryCardTone(visibleMetrics.total_occurrences, 'cyan');

  return (
    <>
      <TopHeader
        title="Repeated Problems"
        subtitle="See problems that have shown up more than once"
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
              {loadError || 'Repeated problem data is not available right now. Try again after RigMD finishes syncing.'}
            </div>
          )}

          <motion.div variants={staggerContainer} initial="hidden" animate="visible" className="grid grid-cols-1 gap-4 lg:grid-cols-4">
            <MetricCard
              icon={<RefreshCw size={20} className={recurringTone.iconColor} />}
              label="Things Repeated"
              value={visibleMetrics.recurring_issues}
              borderColor={recurringTone.borderColor}
              valueColor={recurringTone.valueColor}
            />

            <MetricCard
              icon={<AlertTriangle size={20} className={worseningTone.iconColor} />}
              label="Getting Worse"
              value={visibleMetrics.worsening_trends}
              borderColor={worseningTone.borderColor}
              valueColor={worseningTone.valueColor}
            />

            <MetricCard
              icon={<AlertCircle size={20} className={escalatedTone.iconColor} />}
              label="Needs Help"
              value={visibleMetrics.action_escalated}
              borderColor={escalatedTone.borderColor}
              valueColor={escalatedTone.valueColor}
            />

            <MetricCard
              icon={<BarChart3 size={20} className={occurrencesTone.iconColor} />}
              label="Total Matches"
              value={visibleMetrics.total_occurrences}
              borderColor={occurrencesTone.borderColor}
              valueColor={occurrencesTone.valueColor}
            />
          </motion.div>

          <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5">
            <div className="flex gap-3">
              <Info size={18} className="mt-0.5 shrink-0 text-cyan-400" />

              <div>
                <h3 className="font-bold text-white">What this page means</h3>
                <p className="mt-1 text-sm leading-relaxed text-slate-400">
                  RigMD groups checks that keep showing the same kind of problem. If something is only being watched,
                  no fix is needed yet. If it keeps getting worse, RigMD will make the next step clearer.
                </p>
              </div>
            </div>
          </section>

          <section>
            <div className="mb-4 space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex flex-1 items-center gap-3">
                  <RefreshCw size={15} className={isLoading ? 'animate-spin text-cyan-400' : 'text-cyan-400'} />
                  <h3 className="text-sm font-bold uppercase tracking-[0.18em] text-white">Problems seen more than once</h3>
                  <div className="h-px flex-1 bg-[var(--rigmd-border)]" />
                </div>

                <p className="ml-4 text-xs text-slate-500">
                  {filteredPatterns.length} of {visiblePatterns.length} shown
                </p>
              </div>

              {visiblePatterns.length > 0 && (
                <div className="flex flex-col gap-3 rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-4 xl:flex-row xl:items-center xl:justify-between">
                  <div className="flex flex-wrap gap-2">
                    {patternFilters.map((item) => (
                      <motion.button
                        key={item}
                        type="button"
                        onClick={() => setFilter(item)}
                        whileTap={buttonTap}
                        className={`rounded-full border px-3.5 py-2 text-xs font-bold uppercase transition ${
                          filter === item
                            ? 'border-cyan-300/55 bg-[#12343a] text-cyan-200'
                            : 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)] text-slate-500 hover:border-[#2b5261] hover:bg-[var(--rigmd-card-hover)] hover:text-cyan-300'
                        }`}
                      >
                        {getFilterLabel(item)}
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
                      placeholder="Search what RigMD found..."
                      className="h-10 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] pl-11 pr-4 text-sm text-white outline-none placeholder:text-slate-500 focus:border-cyan-500/50 xl:w-[340px]"
                    />
                  </label>
                </div>
              )}
            </div>

            {isLoading && visiblePatterns.length === 0 ? (
              <div className="space-y-4">
                {Array.from({ length: 3 }).map((_, index) => (
                  <div key={index} className="h-32 animate-pulse rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)]" />
                ))}
              </div>
            ) : visiblePatterns.length === 0 ? (
              <div className="flex min-h-[280px] flex-col items-center justify-center rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-6 py-12 text-center">
                <FileText size={42} className="mb-4 text-slate-600" />
                <h3 className="text-lg font-bold text-white">No repeated problems that need attention yet.</h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  Healthy results are saved in Past Checks, but this page only shows repeated issues that may matter.
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
                <h3 className="text-lg font-bold text-white">Nothing matches that filter.</h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  Try clearing the search or showing all repeated problems.
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
                    onDeletePattern={() => setPatternPendingDelete(pattern)}
                    deleting={deletingPatternId === pattern.id}
                    onToggle={() =>
                      setExpandedPatternId(expandedPatternId === pattern.id ? null : pattern.id)
                    }
                  />
                ))}
              </motion.div>
            )}
          </section>

          <PatternTimelineTable rows={visibleTimeline} />
        </div>
      </motion.div>

      <DeleteConfirmationDialog
        open={patternPendingDelete != null}
        title="Delete this repeated problem?"
        description="This removes the saved checks that created this repeated problem. It will also remove those checks from Past Checks."
        confirmLabel="Delete related checks"
        isWorking={deletingPatternId === patternPendingDelete?.id}
        onCancel={() => {
          if (!deletingPatternId) {
            setPatternPendingDelete(null);
          }
        }}
        onConfirm={() => {
          if (patternPendingDelete) {
            void deletePatternSessions(patternPendingDelete);
          }
        }}
        details={
          patternPendingDelete ? (
            <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
              <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                Repeated problem
              </p>
              <div className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
                <div>
                  <p className="text-slate-500">What RigMD found</p>
                  <p className="font-bold text-white">{patternPendingDelete.probable_cause}</p>
                </div>
                <div>
                  <p className="text-slate-500">Saved checks affected</p>
                  <p className="font-bold text-white">
                    {patternPendingDelete.occurrences.length} check
                    {patternPendingDelete.occurrences.length === 1 ? '' : 's'}
                  </p>
                </div>
              </div>
            </div>
          ) : null
        }
      />
    </>
  );
}
