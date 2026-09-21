import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import {
  AlertTriangle,
  ChevronDown,
  FileText,
  Info,
  Search,
  ShieldAlert,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import FilterDropdown from '../components/FilterDropdown';
import { hoverLift, buttonTap, cardFadeUp, cardTransition, pageFade, pageTransition, staggerContainer } from '../lib/motion';
import { apiGet } from '../lib/api';

interface WarningSummary {
  observed_warning_signs: number;
  total_observed_occurrences: number;
  total_reference_items: number;
}

interface WarningSignRow {
  id: string;
  warning_sign: string;
  meaning: string;
  threshold: string;
  action: string;
  category: string;
  keywords: string[];
  observed: boolean;
  observed_count: number;
}

interface WarningSignsResponse {
  summary: WarningSummary;
  categories: string[];
  warning_signs: WarningSignRow[];
  database_warning?: string;
}

const emptyWarningSigns: WarningSignsResponse = {
  summary: {
    observed_warning_signs: 0,
    total_observed_occurrences: 0,
    total_reference_items: 0,
  },
  categories: [],
  warning_signs: [],
};

function getActionStyle(action: string) {
  const value = action.toLowerCase();

  if (value.includes('escalate')) {
    return 'border-red-400/45 bg-red-400/10 text-red-300';
  }

  if (value.includes('troubleshoot')) {
    return 'border-amber-400/40 bg-amber-400/10 text-amber-300';
  }

  if (value.includes('maintain')) {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  if (value.includes('monitor')) {
    return 'border-cyan-400/35 bg-cyan-400/10 text-cyan-300';
  }

  return 'border-slate-500/35 bg-slate-500/10 text-slate-300';
}

function getCategoryStyle() {
  return 'border-slate-500/25 bg-slate-500/10 text-slate-400';
}

function getActionSeverity(action: string) {
  const value = action.toLowerCase();

  if (value.includes('escalate')) {
    return 'escalate';
  }

  if (value.includes('troubleshoot')) {
    return 'troubleshoot';
  }

  if (value.includes('maintain')) {
    return 'maintain';
  }

  if (value.includes('monitor')) {
    return 'monitor';
  }

  return 'info';
}

function getSeverityCounts(rows: WarningSignRow[]) {
  return rows.reduce(
    (counts, row) => {
      if (!row.observed) return counts;

      const severity = getActionSeverity(row.action);
      counts[severity] += 1;
      return counts;
    },
    {
      escalate: 0,
      troubleshoot: 0,
      maintain: 0,
      monitor: 0,
      info: 0,
    }
  );
}

function getAlertTone(counts: ReturnType<typeof getSeverityCounts>) {
  if (counts.escalate > 0) {
    return {
      tone: 'danger' as const,
      title: 'Critical warning signs observed in recent sessions',
      description: 'Review Escalate items first, then follow the recommended actions below.',
      iconClassName: 'text-red-300',
    };
  }

  if (counts.troubleshoot > 0) {
    return {
      tone: 'warning' as const,
      title: 'Warning signs need review',
      description: 'Troubleshoot items were observed recently. Check the recommended actions before they repeat.',
      iconClassName: 'text-amber-300',
    };
  }

  if (counts.maintain > 0 || counts.monitor > 0 || counts.info > 0) {
    return {
      tone: 'success' as const,
      title: 'Minor warning signs observed',
      description: 'Observed items are lower concern. Continue maintenance and monitor the next diagnosis.',
      iconClassName: 'text-emerald-300',
    };
  }

  return {
    tone: 'info' as const,
    title: 'No warning signs observed recently',
    description: 'This reference guide is ready. Observed warning signs will be highlighted after diagnoses are saved.',
    iconClassName: 'text-cyan-300',
  };
}

function getSeverityChipClass(severity: string) {
  if (severity === 'Escalate') {
    return 'border-red-400/35 bg-red-400/10 text-red-300';
  }

  if (severity === 'Troubleshoot') {
    return 'border-amber-400/35 bg-amber-400/10 text-amber-300';
  }

  if (severity === 'Maintain') {
    return 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300';
  }

  return 'border-cyan-400/30 bg-cyan-400/10 text-cyan-300';
}

function getObservedLabelClass(action: string) {
  const severity = getActionSeverity(action);

  if (severity === 'escalate') return 'text-red-300/85';
  if (severity === 'troubleshoot') return 'text-amber-300/85';
  if (severity === 'maintain') return 'text-emerald-300/85';

  return 'text-cyan-300/80';
}

function getPriorityScore(row: WarningSignRow) {
  const severity = getActionSeverity(row.action);
  const severityScore =
    severity === 'escalate'
      ? 0
      : severity === 'troubleshoot'
        ? 1
        : severity === 'maintain'
          ? 2
          : 3;

  return (row.observed ? 0 : 4) + severityScore;
}

function filterWarningSigns(
  rows: WarningSignRow[],
  activeCategory: string,
  search: string,
  observedOnly: boolean
) {
  const query = search.trim().toLowerCase();

  return rows.filter((row) => {
    if (activeCategory !== 'all' && row.category !== activeCategory) {
      return false;
    }

    if (observedOnly && !row.observed) {
      return false;
    }

    if (!query) {
      return true;
    }

    return (
      row.warning_sign.toLowerCase().includes(query) ||
      row.meaning.toLowerCase().includes(query) ||
      row.threshold.toLowerCase().includes(query) ||
      row.action.toLowerCase().includes(query) ||
      row.category.toLowerCase().includes(query) ||
      row.keywords.some((keyword) => keyword.toLowerCase().includes(query))
    );
  });
}

function sortWarningSignsByPriority(rows: WarningSignRow[]) {
  return [...rows].sort((a, b) => {
    const priorityDifference = getPriorityScore(a) - getPriorityScore(b);

    if (priorityDifference !== 0) {
      return priorityDifference;
    }

    return a.warning_sign.localeCompare(b.warning_sign);
  });
}

function AlertCard({
  icon,
  title,
  description,
  tone,
  children,
}: {
  icon: ReactNode;
  title: string;
  description: string;
  tone: 'danger' | 'info' | 'warning' | 'success';
  children?: ReactNode;
}) {
  const toneClass =
    tone === 'danger'
      ? 'border-red-400/30 bg-red-400/5 text-red-300'
      : tone === 'warning'
        ? 'border-amber-400/30 bg-amber-400/5 text-amber-300'
        : tone === 'success'
          ? 'border-emerald-400/25 bg-emerald-400/5 text-emerald-300'
          : 'border-cyan-500/20 bg-cyan-500/5 text-slate-400';

  return (
    <motion.section variants={cardFadeUp} transition={cardTransition} className={`rounded-2xl border px-5 py-4 ${toneClass}`}>
      <div className="flex items-start gap-3">
        <div className="mt-0.5 shrink-0">{icon}</div>

        <div>
          <h3 className={`font-bold ${tone === 'info' ? 'text-slate-300' : ''}`}>
            {title}
          </h3>
          <p className="mt-1 text-sm leading-relaxed text-slate-400">
            {description}
          </p>
          {children}
        </div>
      </div>
    </motion.section>
  );
}

function SeverityBreakdown({ counts }: { counts: ReturnType<typeof getSeverityCounts> }) {
  const chips = [
    ['Escalate', counts.escalate],
    ['Troubleshoot', counts.troubleshoot],
    ['Maintain', counts.maintain],
    ['Monitor', counts.monitor + counts.info],
  ].filter(([, count]) => Number(count) > 0);

  if (chips.length === 0) {
    return (
      <p className="mt-3 text-xs font-semibold text-slate-500">
        No observed warning signs recorded.
      </p>
    );
  }

  return (
    <div className="mt-3 flex flex-wrap gap-2">
      {chips.map(([label, count]) => (
        <span
          key={label}
          className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-bold ${getSeverityChipClass(
            String(label)
          )}`}
        >
          {label}: {count}
        </span>
      ))}
    </div>
  );
}

function ObservedToggle({
  checked,
  onChange,
}: {
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <motion.button
      type="button"
      onClick={() => onChange(!checked)}
      whileTap={buttonTap}
      className={`flex shrink-0 items-center gap-3 rounded-xl border px-4 py-2 text-sm font-semibold transition ${
        checked
          ? 'border-cyan-300/55 bg-[#12343a] text-cyan-200'
          : 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)] text-slate-400 hover:border-[#2b5261] hover:bg-[var(--rigmd-card-hover)] hover:text-cyan-300'
      }`}
    >
      <span
        className={`flex h-5 w-9 items-center rounded-full border p-0.5 transition ${
          checked
            ? 'border-cyan-500/60 bg-cyan-500/20'
            : 'border-slate-600 bg-[var(--rigmd-sidebar)]'
        }`}
      >
        <motion.span
          className={`h-3.5 w-3.5 rounded-full transition ${
            checked ? 'translate-x-4 bg-cyan-400' : 'translate-x-0 bg-slate-500'
          }`}
          animate={{ x: checked ? 16 : 0 }}
          transition={{ duration: 0.18 }}
        />
      </span>

      Observed Only
    </motion.button>
  );
}

function ActionBadge({ action }: { action: string }) {
  return (
    <span
      className={`inline-flex shrink-0 items-center justify-center whitespace-nowrap rounded-full border px-2.5 py-1 text-[11px] font-bold ${getActionStyle(
        action
      )}`}
    >
      {friendlyAction(action)}
    </span>
  );
}

function CategoryBadge({ category }: { category: string }) {
  return (
    <span
      className={`inline-flex items-center justify-center rounded-full border px-2.5 py-1 text-xs font-semibold ${getCategoryStyle()}`}
    >
      {category}
    </span>
  );
}

function friendlyAction(action: string) {
  const value = action.toLowerCase();
  if (value.includes('escalate')) return 'Get help';
  if (value.includes('troubleshoot')) return 'Try a fix';
  if (value.includes('maintain')) return 'Care tips';
  if (value.includes('monitor')) return 'Watch for now';
  return action;
}

function WarningSignCard({ row }: { row: WarningSignRow }) {
  const [expanded, setExpanded] = useState(false);
  const reducedMotion = useReducedMotion();

  return (
    <motion.button
      type="button"
      variants={cardFadeUp}
      whileHover={hoverLift}
      transition={{ duration: 0.18 }}
      aria-expanded={expanded}
      onClick={() => setExpanded(!expanded)}
      className={`row-span-2 grid h-full w-full grid-rows-subgrid gap-0 overflow-hidden rounded-xl border bg-[var(--rigmd-card)] text-left transition-colors duration-200 hover:border-cyan-400/50 hover:bg-[var(--rigmd-card-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 ${expanded ? 'border-cyan-400/50' : 'border-[var(--rigmd-border)]'}`}
    >
      <span className="flex min-h-[184px] w-full flex-col p-5">
        <span className="flex min-h-8 items-start justify-between gap-3">
          <span className="min-w-0 text-base font-bold text-white">{row.warning_sign}</span>
          <ActionBadge action={row.action} />
        </span>
        <span className="mt-3 text-sm leading-relaxed text-slate-400">{row.meaning}</span>
        {row.observed && (
          <span className={`mt-2 text-xs font-semibold ${getObservedLabelClass(row.action)}`}>
            Found in saved checks{row.observed_count > 1 ? ` (${row.observed_count} times)` : ''}
          </span>
        )}
        <span className="mt-auto flex items-center justify-between gap-3 pt-4">
          <CategoryBadge category={row.category} />
          <span className="inline-flex shrink-0 items-center gap-2 text-xs font-semibold text-cyan-300">
            {expanded ? 'Hide details' : 'View details'}
            <ChevronDown size={16} className={`transition-transform duration-300 motion-reduce:transition-none ${expanded ? 'rotate-180' : ''}`} />
          </span>
        </span>
      </span>
      <AnimatePresence initial={false}>
        {expanded && (
          <motion.span
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: reducedMotion ? 0 : 0.25, ease: 'easeOut' }}
            className="block w-full overflow-hidden"
          >
            <span className="grid gap-4 border-t border-[var(--rigmd-border)] px-5 py-4 text-sm leading-relaxed">
              <span className="grid gap-1">
                <span className="font-semibold text-slate-200">What to look for</span>
                <span className="text-slate-400">{row.threshold}</span>
              </span>
              <span className="grid gap-1">
                <span className="font-semibold text-slate-200">Next step</span>
                <span className="text-slate-400">{friendlyAction(row.action)}</span>
              </span>
            </span>
          </motion.span>
        )}
      </AnimatePresence>
    </motion.button>
  );
}

export default function WarningSignsView() {
  const [data, setData] = useState<WarningSignsResponse>(emptyWarningSigns);
  const [activeCategory, setActiveCategory] = useState('all');
  const [search, setSearch] = useState('');
  const [observedOnly, setObservedOnly] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const fetchWarningSigns = useCallback(async () => {
    setIsLoading(true);

    try {
      const response = await apiGet<WarningSignsResponse>(
        '/api/warning-signs/reference',
        {
          params: {
            category: activeCategory,
            search,
            observed_only: observedOnly,
          },
        }
      );

      setData(response.data);
      setLoadError(null);
    } catch {
      setData(emptyWarningSigns);
      setLoadError('Warning signs data is not available yet.');
    } finally {
      setIsLoading(false);
    }
  }, [activeCategory, search, observedOnly]);

  useEffect(() => {
    fetchWarningSigns();
  }, [fetchWarningSigns]);

  const categories = ['all', ...data.categories];
  const visibleWarningSigns = useMemo(
    () =>
      sortWarningSignsByPriority(
        filterWarningSigns(data.warning_signs, activeCategory, search, observedOnly)
      ),
    [activeCategory, data.warning_signs, observedOnly, search]
  );

  const observedRows = useMemo(
    () => data.warning_signs.filter((row) => row.observed),
    [data.warning_signs]
  );

  const severityCounts = useMemo(
    () => getSeverityCounts(data.warning_signs),
    [data.warning_signs]
  );

  const alertTone = getAlertTone(severityCounts);
  const hasActiveFilters =
    activeCategory !== 'all' ||
    search.trim().length > 0 ||
    observedOnly;

  return (
    <>
      <TopHeader
        title="Warning Signs"
        subtitle="Reference guide for observable Device warning indicators and recommended actions"
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
          <AlertCard
            icon={<ShieldAlert size={18} className={alertTone.iconClassName} />}
            tone={alertTone.tone}
            title={alertTone.title}
            description={alertTone.description}
          >
            <SeverityBreakdown counts={severityCounts} />
          </AlertCard>

          <details className="group rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] px-5 py-4">
            <summary className="flex cursor-pointer list-none items-center gap-2 text-sm font-semibold text-slate-300 marker:hidden">
              <Info size={16} className="text-cyan-400" />
              About warning signs
              <ChevronDown size={16} className="ml-auto shrink-0 self-center text-slate-400 transition-transform duration-300 group-open:rotate-180 motion-reduce:transition-none" />
            </summary>
            <p className="mt-3 pl-6 text-sm leading-relaxed text-slate-400">
              Warning signs suggest a possible problem. Follow the suggested step and check again if it continues.
            </p>
          </details>

          {(data.database_warning || loadError) && (
            <AlertCard
              icon={<AlertTriangle size={18} className="text-amber-300" />}
              tone="warning"
              title="Warning signs data is not available yet."
              description="Check your backend or database connection."
            />
          )}

          <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-4">
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <FilterDropdown
                label="Filter by type"
                value={activeCategory}
                onChange={setActiveCategory}
                options={categories.map((category) => ({ value: category, label: category === 'all' ? 'All warning signs' : category }))}
              />
              <div className="rigmd-search-field flex w-full items-center gap-2 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-4 py-2.5 transition-colors xl:w-[260px]">
                <Search size={16} className="shrink-0 text-slate-500" />
                <input
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search warning signs..."
                  aria-label="Search warning signs"
                  className="min-w-0 w-full bg-transparent text-sm text-white outline-none placeholder:text-slate-500"
                />
              </div>
            </div>
          </section>

          <section className="overflow-hidden rounded-2xl border border-[var(--rigmd-border)] bg-[#101821]">
            <div className="flex items-center justify-between gap-4 border-b border-[var(--rigmd-border)] px-5 py-4">
              <div className="flex items-center gap-2">
                <AlertTriangle size={15} className="text-slate-400" />
                <h3 className="font-bold uppercase tracking-wider text-white">
                  Warning Signs Reference
                </h3>
                <span className="rounded-full bg-[var(--rigmd-card-soft)] px-2 py-0.5 text-xs font-bold text-slate-500">
                  {visibleWarningSigns.length}
                </span>
              </div>

              <div className="flex items-center gap-3">
                <ObservedToggle checked={observedOnly} onChange={setObservedOnly} />

                {isLoading && (
                  <span className="text-xs font-semibold uppercase text-cyan-400">
                    Loading...
                  </span>
                )}
              </div>
            </div>

            {isLoading ? (
              <div className="grid gap-3 p-4 sm:grid-cols-2">
                {Array.from({ length: 4 }).map((_, index) => <div key={index} className="h-36 animate-pulse rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)]" />)}
              </div>
            ) : visibleWarningSigns.length === 0 ? (
              <AnimatePresence mode="wait">
                <motion.div
                  key={`${loadError ? 'error' : hasActiveFilters ? 'filtered' : 'empty'}-${observedOnly}`}
                  variants={cardFadeUp}
                  initial="hidden"
                  animate="visible"
                  exit="exit"
                  transition={cardTransition}
                  className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center"
                >
                <FileText size={42} className="mb-4 text-slate-600" />
                <h3 className="text-lg font-bold text-white">
                  {loadError
                    ? 'Warning signs data is not available yet.'
                    : observedOnly && observedRows.length === 0
                      ? 'No observed warning signs yet.'
                      : hasActiveFilters
                        ? 'No warning signs match your filters.'
                        : 'No warning signs available.'}
                </h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  {loadError
                    ? 'Check your backend or database connection.'
                    : observedOnly && observedRows.length === 0
                      ? 'Run a diagnosis to check for warning indicators.'
                      : hasActiveFilters
                        ? 'Try clearing the search or selecting All.'
                        : 'Warning signs will appear here once the reference data is loaded.'}
                </p>
                </motion.div>
              </AnimatePresence>
            ) : (
              <motion.div variants={staggerContainer} initial="hidden" animate="visible" className="grid gap-3 p-4 sm:grid-cols-2">
                {visibleWarningSigns.map((row) => <WarningSignCard key={row.id} row={row} />)}
              </motion.div>
            )}
          </section>
        </div>
      </motion.div>
    </>
  );
}
