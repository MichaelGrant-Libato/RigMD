import { motion } from 'motion/react';
import { AlertTriangle, CheckCircle2, ChevronRight, RefreshCw } from 'lucide-react';
import type { DashboardSummary, PageKey } from '../../types/rigmd';

interface Props {
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
}

export default function AlertsSummaryCard({ dashboard, setActivePage }: Props) {
  const hasWarnings = dashboard.warning_signs_active_count > 0;
  const hasPatterns = dashboard.recurring_issues_count > 0;
  const unavailable = Boolean(dashboard.database_warning);

  if (unavailable) {
    return (
      <motion.section
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.3, delay: 0.25 }}
        className="rigmd-card-surface flex items-center gap-3 rounded-xl border border-amber-400/20 p-5"
      >
        <AlertTriangle size={18} className="shrink-0 text-amber-300" />
        <p className="text-sm text-slate-300">Check history is unavailable. Try again shortly.</p>
      </motion.section>
    );
  }

  return (
    <motion.section
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, delay: 0.25 }}
      className="rigmd-card-surface flex flex-col gap-3 rounded-xl border p-5"
    >
      <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Alerts & Patterns</span>

      {/* Warning signs row */}
      <button
        type="button"
        onClick={() => hasWarnings && setActivePage('warningSigns')}
        disabled={!hasWarnings}
        className={`group flex w-full items-center justify-between gap-3 rounded-lg px-3 py-2.5 text-left transition
          ${hasWarnings
            ? 'bg-amber-400/[0.04] border border-amber-400/20 hover:bg-amber-400/[0.07] cursor-pointer'
            : 'bg-white/[0.015] border border-transparent cursor-default'
          }`}
      >
        <div className="flex items-center gap-3 min-w-0">
          {hasWarnings ? (
            <AlertTriangle size={16} className="shrink-0 text-amber-300" />
          ) : (
            <CheckCircle2 size={16} className="shrink-0 text-emerald-400" />
          )}
          <div className="min-w-0">
            <p className="text-sm font-medium text-white">Warning Signs</p>
            <p className="truncate text-[11px] text-slate-400">
              {hasWarnings
                ? `${dashboard.warning_signs_active_count} recorded alert${dashboard.warning_signs_active_count !== 1 ? 's' : ''}`
                : 'No warnings detected'}
            </p>
          </div>
        </div>
        {hasWarnings && (
          <ChevronRight size={14} className="shrink-0 text-amber-300/60 group-hover:text-amber-200 transition-colors" />
        )}
      </button>

      {/* Recurring patterns row */}
      <button
        type="button"
        onClick={() => hasPatterns && setActivePage('recurringPatterns')}
        disabled={!hasPatterns}
        className={`group flex w-full items-center justify-between gap-3 rounded-lg px-3 py-2.5 text-left transition
          ${hasPatterns
            ? 'bg-cyan-400/[0.04] border border-cyan-400/20 hover:bg-cyan-400/[0.07] cursor-pointer'
            : 'bg-white/[0.015] border border-transparent cursor-default'
          }`}
      >
        <div className="flex items-center gap-3 min-w-0">
          <RefreshCw size={16} className={hasPatterns ? 'shrink-0 text-cyan-300' : 'shrink-0 text-slate-500'} />
          <div className="min-w-0">
            <p className="text-sm font-medium text-white">Repeated Checks</p>
            <p className="truncate text-[11px] text-slate-400">
              {hasPatterns
                ? `${dashboard.recurring_issues_count} pattern${dashboard.recurring_issues_count !== 1 ? 's' : ''} found`
                : 'None found yet'}
            </p>
          </div>
        </div>
        {hasPatterns && (
          <ChevronRight size={14} className="shrink-0 text-cyan-300/60 group-hover:text-cyan-200 transition-colors" />
        )}
      </button>
    </motion.section>
  );
}
