import { useCallback, useEffect, useState } from 'react';
import axios from 'axios';
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
  Zap,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';

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

function getActionStyle(action: string) {
  const value = action.toLowerCase();

  if (value.includes('monitor')) {
    return 'border-blue-500/70 bg-blue-500/10 text-blue-400';
  }

  if (value.includes('maintain')) {
    return 'border-emerald-500/70 bg-emerald-500/10 text-emerald-400';
  }

  if (value.includes('troubleshoot')) {
    return 'border-orange-500/70 bg-orange-500/10 text-orange-400';
  }

  if (value.includes('escalate')) {
    return 'border-red-500/70 bg-red-500/10 text-red-400';
  }

  return 'border-cyan-500/70 bg-cyan-500/10 text-cyan-400';
}

function getStatusStyle(status: string) {
  const value = status.toLowerCase();

  if (value.includes('worsening')) {
    return 'border-red-500/60 bg-red-500/10 text-red-400';
  }

  if (value.includes('improving')) {
    return 'border-cyan-500/60 bg-cyan-500/10 text-cyan-400';
  }

  return 'border-slate-500/40 bg-slate-500/10 text-slate-400';
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
    <section className={`rounded-2xl border bg-[#161b22] p-5 ${borderColor}`}>
      <div className="flex items-center gap-4">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[#0d1117]">
          {icon}
        </div>

        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">{label}</p>
          <h3 className={`text-3xl font-bold ${valueColor}`}>{value}</h3>
        </div>
      </div>
    </section>
  );
}

function ActionBadge({ action }: { action: string }) {
  return (
    <span className={`inline-flex items-center justify-center rounded border px-3 py-1 text-xs font-bold uppercase ${getActionStyle(action)}`}>
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
    <span className={`inline-flex rounded border px-3 py-1 text-xs font-bold uppercase ${getConfidenceStyle(confidence)}`}>
      {confidence}
    </span>
  );
}

function PatternDots({ pattern }: { pattern: RecurringPattern }) {
  return (
    <div className="mt-4 flex items-center gap-0">
      {pattern.timeline.map((item, index) => (
        <div key={`${item.session_id}-${index}`} className="flex flex-1 items-center">
          <div className="relative flex flex-col items-center">
            <span className="h-2.5 w-2.5 rounded-full bg-cyan-400 shadow-[0_0_12px_rgba(34,211,238,0.8)]" />
            <span className="mt-2 text-xs font-semibold text-white">{item.date}</span>
          </div>

          {index < pattern.timeline.length - 1 && <div className="mx-2 h-px flex-1 bg-[#30363d]" />}
        </div>
      ))}
    </div>
  );
}

function PatternCard({
  pattern,
  expanded,
  onToggle,
}: {
  pattern: RecurringPattern;
  expanded: boolean;
  onToggle: () => void;
}) {
  return (
    <section
      className={`overflow-hidden rounded-2xl border border-[#30363d] border-l-2 bg-[#161b22] ${getPatternBorder(
        pattern.status
      )}`}
    >
      <button type="button" onClick={onToggle} className="w-full px-5 py-5 text-left transition hover:bg-[#1b2432]">
        <div className="grid grid-cols-[64px_minmax(0,1fr)_260px_130px] items-center gap-5">
          <div className="flex h-14 w-14 flex-col items-center justify-center rounded-full bg-[#1f2937]">
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
            <span className="inline-flex items-center gap-2 rounded-lg border border-[#30363d] bg-[#0d1117] px-4 py-2 text-xs font-semibold text-slate-400">
              {expanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
              {expanded ? 'Collapse' : 'Expand'}
            </span>
          </div>
        </div>
      </button>

      {expanded && (
        <div className="border-t border-[#30363d] bg-[#111827] px-5 py-5">
          <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
            <div className="space-y-4">
              <section className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
                <p className="mb-2 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
                  Recommended Next Step
                </p>
                <p className="text-sm leading-relaxed text-white">{pattern.recommended_next_step}</p>
              </section>

              <section className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
                <div className="mb-3 flex items-center justify-between">
                  <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">Occurrences</p>
                  <span className="text-xs font-semibold text-cyan-400">{pattern.occurrences.length} related sessions</span>
                </div>

                <div className="space-y-2">
                  {pattern.occurrences.map((occurrence) => (
                    <div
                      key={occurrence.id}
                      className="grid grid-cols-[120px_minmax(0,1fr)_160px_160px] items-center gap-4 rounded-lg border border-[#26303a] bg-[#161b22] px-4 py-3"
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
              </section>
            </div>

            <aside className="space-y-4">
              <section className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
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
                    <span className="text-slate-500">Status</span>
                    <StatusBadge status={pattern.status} />
                  </div>

                  <div className="flex items-center justify-between">
                    <span className="text-slate-500">Occurrences</span>
                    <span className="font-bold text-white">{pattern.occurrence_count}</span>
                  </div>
                </div>
              </section>

              <button
                type="button"
                className="flex w-full items-center justify-center gap-2 rounded-xl border border-cyan-500/30 bg-cyan-500/10 px-4 py-3 text-sm font-bold text-cyan-400 transition hover:bg-cyan-500/20"
              >
                <Eye size={16} />
                View Related Sessions
              </button>

              <button
                type="button"
                className="flex w-full items-center justify-center gap-2 rounded-xl border border-[#30363d] bg-[#1f2937] px-4 py-3 text-sm font-bold text-slate-400 transition hover:border-cyan-500/40 hover:text-cyan-400"
              >
                <Download size={16} />
                Export Pattern Report
              </button>
            </aside>
          </div>
        </div>
      )}
    </section>
  );
}

function PatternTimelineTable({ rows }: { rows: PatternTimelineRow[] }) {
  return (
    <section className="overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22]">
      <div className="flex items-center gap-2 border-b border-[#30363d] px-5 py-4">
        <Zap size={15} className="text-cyan-400" />
        <h3 className="font-bold uppercase tracking-wider text-white">Pattern Timeline</h3>
      </div>

      <div className="grid grid-cols-[150px_minmax(170px,1fr)_minmax(220px,1fr)_180px_190px_150px] border-b border-[#30363d] bg-[#111827] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
        <div>Date</div>
        <div>Symptom</div>
        <div className="text-center">Probable Cause</div>
        <div className="text-center">Action</div>
        <div className="text-center">Confidence</div>
        <div className="text-center">Status</div>
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
          <div
            key={`${row.pattern_id}-${row.date}-${index}`}
            className="grid grid-cols-[150px_minmax(170px,1fr)_minmax(220px,1fr)_180px_190px_150px] items-center border-b border-[#30363d] px-5 py-4 last:border-b-0"
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
          </div>
        ))
      )}
    </section>
  );
}

export default function RecurringPatternsView() {
  const [data, setData] = useState<RecurringResponse>(emptyRecurring);
  const [expandedPatternId, setExpandedPatternId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const fetchPatterns = useCallback(async () => {
    setIsLoading(true);

    try {
      const response = await axios.get<RecurringResponse>(`${API_BASE_URL}/api/recurring/patterns`);
      setData(response.data);
    } catch {
      setData(emptyRecurring);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPatterns();

    const interval = window.setInterval(fetchPatterns, 5000);

    return () => window.clearInterval(interval);
  }, [fetchPatterns]);

  return (
    <>
      <TopHeader
        title="Recurring Patterns"
        subtitle="Symptoms and causes detected across multiple diagnostic sessions"
      />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="mx-auto w-full max-w-[1360px] space-y-5">
          {data.database_warning && (
            <div className="rounded-xl border border-orange-500/30 bg-orange-500/10 px-4 py-3 text-sm text-orange-300">
              Recurring pattern data is not available yet. Check your Supabase connection and sessions table.
            </div>
          )}

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-4">
            <MetricCard
              icon={<RefreshCw size={20} className="text-cyan-400" />}
              label="Recurring Issues"
              value={data.metrics.recurring_issues}
              borderColor="border-cyan-500/30"
              valueColor="text-cyan-400"
            />

            <MetricCard
              icon={<AlertTriangle size={20} className="text-red-400" />}
              label="Worsening Trends"
              value={data.metrics.worsening_trends}
              borderColor="border-red-500/30"
              valueColor="text-red-400"
            />

            <MetricCard
              icon={<AlertCircle size={20} className="text-orange-400" />}
              label="Action Escalated"
              value={data.metrics.action_escalated}
              borderColor="border-orange-500/30"
              valueColor="text-orange-400"
            />

            <MetricCard
              icon={<BarChart3 size={20} className="text-cyan-400" />}
              label="Total Occurrences"
              value={data.metrics.total_occurrences}
              borderColor="border-cyan-500/30"
              valueColor="text-cyan-400"
            />
          </div>

          <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
            <div className="flex gap-3">
              <Info size={18} className="mt-0.5 shrink-0 text-cyan-400" />

              <div>
                <h3 className="font-bold text-white">Pattern Escalation Advisory</h3>
                <p className="mt-1 text-sm leading-relaxed text-slate-400">
                  Recurring issues may increase the action level from Monitor to{' '}
                  <span className="font-bold text-orange-400">Troubleshoot</span> or{' '}
                  <span className="font-bold text-red-400">Escalate</span> when the same symptom or probable cause
                  appears across multiple diagnostic sessions.
                </p>
              </div>
            </div>
          </section>

          <section>
            <div className="mb-4 flex items-center justify-between">
              <div className="flex flex-1 items-center gap-3">
                <RefreshCw size={15} className={isLoading ? 'animate-spin text-cyan-400' : 'text-cyan-400'} />
                <h3 className="text-sm font-bold uppercase tracking-[0.18em] text-white">Detected Patterns</h3>
                <div className="h-px flex-1 bg-[#30363d]" />
              </div>

              <p className="ml-4 text-xs text-slate-500">{data.patterns.length} patterns found</p>
            </div>

            {data.patterns.length === 0 ? (
              <div className="flex min-h-[280px] flex-col items-center justify-center rounded-2xl border border-[#30363d] bg-[#161b22] px-6 py-12 text-center">
                <FileText size={42} className="mb-4 text-slate-600" />
                <h3 className="text-lg font-bold text-white">No recurring patterns yet</h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  Recurring patterns will appear after the New Diagnosis workflow saves multiple sessions with repeated symptoms or causes.
                </p>
              </div>
            ) : (
              <div className="space-y-4">
                {data.patterns.map((pattern) => (
                  <PatternCard
                    key={pattern.id}
                    pattern={pattern}
                    expanded={expandedPatternId === pattern.id}
                    onToggle={() =>
                      setExpandedPatternId(expandedPatternId === pattern.id ? null : pattern.id)
                    }
                  />
                ))}
              </div>
            )}
          </section>

          <PatternTimelineTable rows={data.timeline} />
        </div>
      </div>
    </>
  );
}