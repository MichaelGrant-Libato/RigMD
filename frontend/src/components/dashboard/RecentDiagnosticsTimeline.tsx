import { motion } from 'motion/react';
import { ChevronRight, Clock3 } from 'lucide-react';
import type { DashboardSummary, PageKey } from '../../types/rigmd';

const actionColors: Record<string, { dot: string; text: string }> = {
  monitor:      { dot: 'bg-blue-400',    text: 'text-blue-300'   },
  maintain:     { dot: 'bg-emerald-400', text: 'text-emerald-300' },
  troubleshoot: { dot: 'bg-amber-400',   text: 'text-amber-300'  },
  escalate:     { dot: 'bg-red-400',     text: 'text-red-300'    },
};

interface Props {
  dashboard: DashboardSummary;
  onViewSession: (sessionId: string) => void;
  setActivePage: (page: PageKey) => void;
}

export default function RecentDiagnosticsTimeline({ dashboard, onViewSession, setActivePage }: Props) {
  // Gather recent sessions from available data
  const sessions: Array<{
    id: string;
    symptom: string;
    action: string;
    date: string | null;
    displayDate: string | null;
  }> = [];

  if (dashboard.last_saved_session) {
    sessions.push({
      id: dashboard.last_saved_session.session_id,
      symptom: dashboard.last_saved_session.symptom_type || 'Device check',
      action: dashboard.last_saved_session.action_category || 'monitor',
      date: dashboard.last_saved_session.created_at,
      displayDate: dashboard.last_saved_session.display_date,
    });
  }
  if (dashboard.last_diagnosis && dashboard.last_diagnosis.session_id !== sessions[0]?.id) {
    sessions.push({
      id: dashboard.last_diagnosis.session_id,
      symptom: dashboard.last_diagnosis.symptom_type || 'Device check',
      action: dashboard.last_diagnosis.action_category || 'monitor',
      date: dashboard.last_diagnosis.created_at,
      displayDate: dashboard.last_diagnosis.display_date,
    });
  }
  if (dashboard.current_action_status && !sessions.find(s => s.id === dashboard.current_action_status!.session_id)) {
    sessions.push({
      id: dashboard.current_action_status.session_id,
      symptom: dashboard.current_action_status.symptom_type || 'Device check',
      action: dashboard.current_action_status.action_category || 'monitor',
      date: dashboard.current_action_status.created_at,
      displayDate: dashboard.current_action_status.display_date,
    });
  }

  const isEmpty = sessions.length === 0;

  function formatDate(iso: string | null, display: string | null): string {
    if (iso) {
      const d = new Date(iso);
      if (!Number.isNaN(d.getTime())) {
        return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
      }
    }
    return display || 'Recent';
  }

  return (
    <motion.section
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, delay: 0.2 }}
      className="rigmd-card-surface flex flex-col rounded-xl border p-5"
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <Clock3 size={16} className="text-cyan-300" />
          <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Recent Diagnostics</span>
        </div>
        {dashboard.totals.total_sessions > 0 && (
          <button
            type="button"
            onClick={() => setActivePage('diagnosticHistory')}
            className="flex items-center gap-1 text-[11px] font-semibold text-cyan-300 hover:text-cyan-200 transition-colors"
          >
            View all <ChevronRight size={12} />
          </button>
        )}
      </div>

      {isEmpty ? (
        <div className="mt-6 flex flex-1 items-center justify-center">
          <p className="text-sm text-slate-500">No diagnostics yet. Run your first check to see results here.</p>
        </div>
      ) : (
        <div className="mt-4 space-y-0">
          {sessions.map((s, i) => {
            const ac = actionColors[s.action.toLowerCase()] ?? actionColors.monitor;
            return (
              <motion.button
                key={s.id}
                type="button"
                onClick={() => onViewSession(s.id)}
                initial={{ opacity: 0, x: -8 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ duration: 0.25, delay: 0.05 * i }}
                className="group relative flex w-full items-start gap-3 rounded-lg px-2 py-3 text-left transition hover:bg-white/[0.03]"
              >
                {/* Timeline line */}
                {i < sessions.length - 1 && (
                  <div className="absolute left-[14px] top-[28px] bottom-0 w-px bg-slate-700/60" />
                )}
                {/* Dot */}
                <div className={`relative z-10 mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${ac.dot}`} />
                {/* Content */}
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-white group-hover:text-cyan-100 transition-colors">
                    {s.symptom}
                  </p>
                  <div className="mt-0.5 flex items-center gap-2">
                    <span className={`text-[11px] font-semibold capitalize ${ac.text}`}>{s.action}</span>
                    <span className="text-[11px] text-slate-500">·</span>
                    <span className="text-[11px] text-slate-500">{formatDate(s.date, s.displayDate)}</span>
                  </div>
                </div>
                <ChevronRight size={14} className="mt-1 shrink-0 text-slate-600 group-hover:text-slate-400 transition-colors" />
              </motion.button>
            );
          })}
        </div>
      )}
    </motion.section>
  );
}
