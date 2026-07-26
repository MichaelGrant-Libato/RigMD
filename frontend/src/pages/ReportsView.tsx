import { useCallback, useEffect, useState } from 'react';
import {
  AlertTriangle,
  CheckSquare,
  ChevronDown,
  ClipboardList,
  Copy,
  Download,
  FileText,
  Printer,
  RefreshCw,
  ShieldAlert,
} from 'lucide-react';
import TopHeader from '../components/TopHeader';
import { API_BASE_URL, fetchWithClient } from '../services/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

interface SessionOption {
  session_id: string;
  session_code: string;
  display_date: string;
  display_time: string;
  symptom_type: string;
  action_category: string;
  confidence_label: string;
  diagnosed_category: string;
  affected_activity?: string;
  frequency?: string;
  severity?: string;
  duration?: string;
  recent_changes?: string;
  warning_signs?: string[];
  recommendations?: Array<{ recommended_action: string; warning_sign?: string }>;
  ai_explanation?: string;
}

interface ProfileSnapshot {
  cpu_model?: string;
  ram_capacity?: string;
  storage_type?: string;
  storage_capacity?: string;
  os_version?: string;
  gpu_driver?: string;
  chipset_driver?: string;
  system_age?: string;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getActionStyle(action: string) {
  const v = action.toLowerCase();
  if (v.includes('maintain')) return 'border-emerald-500/60 bg-emerald-500/10 text-emerald-400';
  if (v.includes('troubleshoot')) return 'border-orange-500/60 bg-orange-500/10 text-orange-400';
  if (v.includes('escalate')) return 'border-red-500/60 bg-red-500/10 text-red-400';
  return 'border-blue-500/60 bg-blue-500/10 text-blue-400';
}

function getConfidenceStyle(confidence: string) {
  const v = confidence.toLowerCase();
  if (v.includes('high')) return 'border-emerald-500/60 bg-emerald-500/10 text-emerald-400';
  if (v.includes('moderate') || v.includes('medium'))
    return 'border-orange-500/60 bg-orange-500/10 text-orange-400';
  return 'border-slate-600 bg-slate-700/30 text-slate-400';
}

function normalizeAction(action: string): string {
  const v = action.toLowerCase();
  if (v.includes('maintain')) return 'MAINTAIN';
  if (v.includes('troubleshoot')) return 'TROUBLESHOOT';
  if (v.includes('escalate')) return 'ESCALATE';
  return 'MONITOR';
}

function normalizeConfidence(confidence: string): string {
  const v = confidence.toLowerCase();
  if (v.includes('high')) return 'HIGH';
  if (v.includes('moderate') || v.includes('medium')) return 'MODERATE';
  return 'LOW';
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function SectionHeader({ icon: Icon, label }: { icon: React.ElementType; label: string }) {
  return (
    <div className="mb-3 flex items-center gap-2 text-[11px] font-bold uppercase tracking-widest text-slate-500">
      <Icon size={13} className="text-cyan-500/70" />
      {label}
    </div>
  );
}

function InfoCell({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-[#253041] bg-[#131c28] px-4 py-3">
      <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-slate-500">{label}</p>
      <p className="truncate text-sm font-medium text-white">{value || '—'}</p>
    </div>
  );
}

function ActionBadge({ label }: { label: string }) {
  return (
    <span
      className={`rounded-md border px-3 py-1 text-xs font-bold uppercase tracking-wider ${getActionStyle(label)}`}
    >
      {normalizeAction(label)}
    </span>
  );
}

function ConfidenceBadge({ label }: { label: string }) {
  return (
    <span
      className={`rounded-md border px-3 py-1 text-xs font-bold uppercase tracking-wider ${getConfidenceStyle(label)}`}
    >
      {normalizeConfidence(label)}
    </span>
  );
}

// ─── Session Selector ─────────────────────────────────────────────────────────

interface SessionSelectorProps {
  sessions: SessionOption[];
  selectedId: string | null;
  onSelect: (id: string) => void;
}

function SessionSelector({ sessions, selectedId, onSelect }: SessionSelectorProps) {
  const [open, setOpen] = useState(false);
  const selected = sessions.find((s) => s.session_id === selectedId);

  const label = selected
    ? `${selected.session_code} — ${selected.symptom_type} (${selected.display_date} — ${selected.display_time})`
    : 'Select Session';

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((p) => !p)}
        className="flex items-center gap-2 rounded-xl border border-[#253041] bg-[#1a2332] px-4 py-2.5 text-sm text-white transition hover:border-cyan-500/50 hover:bg-[#1f2c3e]"
      >
        <FileText size={15} className="shrink-0 text-cyan-400" />
        <span className="max-w-[380px] truncate">{label}</span>
        <ChevronDown
          size={15}
          className={`ml-1 shrink-0 text-slate-400 transition-transform ${open ? 'rotate-180' : ''}`}
        />
      </button>

      {open && (
        <div className="absolute left-0 top-full z-50 mt-2 w-max max-w-[540px] rounded-xl border border-[#253041] bg-[#131c28] shadow-xl">
          {sessions.length === 0 ? (
            <p className="px-4 py-3 text-sm text-slate-500">No sessions available</p>
          ) : (
            sessions.map((s) => (
              <button
                key={s.session_id}
                type="button"
                onClick={() => {
                  onSelect(s.session_id);
                  setOpen(false);
                }}
                className={`flex w-full items-center gap-3 px-4 py-3 text-left text-sm transition hover:bg-[#1a2332] ${
                  s.session_id === selectedId ? 'text-cyan-400' : 'text-slate-200'
                }`}
              >
                <span className="shrink-0 font-mono text-xs text-slate-500">{s.session_code}</span>
                <span className="truncate">{s.symptom_type}</span>
                <span className="ml-auto shrink-0 text-xs text-slate-500">
                  {s.display_date} — {s.display_time}
                </span>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  );
}

// ─── Report Body ──────────────────────────────────────────────────────────────

interface ReportBodyProps {
  session: SessionOption;
  profile: ProfileSnapshot | null;
}

function ReportBody({ session, profile }: ReportBodyProps) {
  const recSteps: string[] = session.recommendations
    ? session.recommendations.map((r) => r.recommended_action).filter(Boolean)
    : [];

  const warningSigns: string[] = Array.isArray(session.warning_signs) ? session.warning_signs : [];

  const handleCopy = () => {
    const text = buildPlainText(session, profile, recSteps, warningSigns);
    navigator.clipboard.writeText(text).catch(() => {});
  };

  const handlePrint = () => window.print();

  return (
    <div className="space-y-5" id="report-printable">
      {/* Report Title Bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[#253041] bg-[#131c28] px-5 py-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-lg border border-cyan-500/30 bg-cyan-500/10 text-cyan-400">
            <FileText size={18} />
          </div>
          <div>
            <p className="font-bold text-white">RigMD Diagnostic Report</p>
            <p className="text-xs text-slate-500">
              Session {session.session_code} &nbsp;·&nbsp; {session.display_date} — {session.display_time}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handleCopy}
            className="flex items-center gap-1.5 rounded-lg border border-[#253041] bg-[#1a2332] px-3 py-1.5 text-xs text-slate-300 transition hover:border-cyan-500/40 hover:text-white"
          >
            <Copy size={13} />
            Copy
          </button>
          <button
            type="button"
            onClick={handlePrint}
            className="flex items-center gap-1.5 rounded-lg border border-[#253041] bg-[#1a2332] px-3 py-1.5 text-xs text-slate-300 transition hover:border-cyan-500/40 hover:text-white"
          >
            <Printer size={13} />
            Print
          </button>
          <button
            type="button"
            onClick={handlePrint}
            className="flex items-center gap-1.5 rounded-lg border border-cyan-500/30 bg-cyan-500/10 px-3 py-1.5 text-xs font-semibold text-cyan-400 transition hover:bg-cyan-500/20"
          >
            <Download size={13} />
            Export PDF
          </button>
        </div>
      </div>

      {/* ── Result Summary ── */}
      <div className="rounded-xl border border-[#253041] bg-[#0f1923] p-5">
        <SectionHeader icon={ClipboardList} label="Result Summary" />
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <InfoCell label="Reported Symptom" value={session.symptom_type} />
          <InfoCell label="Probable Cause" value={session.diagnosed_category} />
          <div className="rounded-xl border border-[#253041] bg-[#131c28] px-4 py-3">
            <p className="mb-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Action Category</p>
            <ActionBadge label={session.action_category} />
          </div>
          <div className="rounded-xl border border-[#253041] bg-[#131c28] px-4 py-3">
            <p className="mb-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Confidence</p>
            <ConfidenceBadge label={session.confidence_label} />
          </div>
        </div>
      </div>

      {/* ── System Profile Snapshot ── */}
      {profile && (
        <div className="rounded-xl border border-[#253041] bg-[#0f1923] p-5">
          <SectionHeader icon={RefreshCw} label="System Profile Snapshot" />
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <InfoCell label="CPU" value={profile.cpu_model ?? '—'} />
            <InfoCell label="RAM" value={profile.ram_capacity ?? '—'} />
            <InfoCell
              label="Storage"
              value={
                profile.storage_type && profile.storage_capacity
                  ? `${profile.storage_type} — ${profile.storage_capacity}`
                  : profile.storage_type ?? profile.storage_capacity ?? '—'
              }
            />
            <InfoCell label="OS" value={profile.os_version ?? '—'} />
            <InfoCell label="GPU" value={profile.gpu_driver ?? '—'} />
            <InfoCell label="GPU Driver" value={profile.gpu_driver ?? '—'} />
            <InfoCell label="Chipset Driver" value={profile.chipset_driver ?? '—'} />
            <InfoCell label="System Age" value={profile.system_age ?? '—'} />
          </div>
        </div>
      )}

      {/* ── Symptom Answers ── */}
      <div className="rounded-xl border border-[#253041] bg-[#0f1923] p-5">
        <SectionHeader icon={CheckSquare} label="Symptom Answers" />
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
          <InfoCell label="Symptom Type" value={session.symptom_type} />
          <InfoCell label="Affected Activity" value={session.affected_activity ?? '—'} />
          <InfoCell label="Frequency" value={session.frequency ?? '—'} />
          <InfoCell label="Severity" value={session.severity ?? '—'} />
          <InfoCell label="Duration" value={session.duration ?? '—'} />
          <InfoCell label="Recent Changes" value={session.recent_changes ?? '—'} />
        </div>
      </div>

      {/* ── Warning Signs Observed ── */}
      {warningSigns.length > 0 && (
        <div className="rounded-xl border border-[#253041] bg-[#0f1923] p-5">
          <SectionHeader icon={ShieldAlert} label="Warning Signs Observed" />
          <div className="space-y-2">
            {warningSigns.map((sign, i) => (
              <div
                key={i}
                className="flex items-center gap-3 rounded-lg border border-[#253041] bg-[#131c28] px-4 py-3"
              >
                <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-orange-400" />
                <span className="text-sm text-slate-200">{sign}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Recommended Next Steps ── */}
      {recSteps.length > 0 && (
        <div className="rounded-xl border border-[#253041] bg-[#0f1923] p-5">
          <SectionHeader icon={AlertTriangle} label="Recommended Next Steps" />
          <div className="space-y-2">
            {recSteps.map((step, i) => (
              <div
                key={i}
                className="flex items-start gap-3 rounded-lg border border-[#253041] bg-[#131c28] px-4 py-3"
              >
                <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border border-cyan-500/30 bg-cyan-500/10 text-[11px] font-bold text-cyan-400">
                  {i + 1}
                </span>
                <span className="text-sm leading-relaxed text-slate-200">{step}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* AI Explanation */}
      {session.ai_explanation && (
        <div className="rounded-xl border border-[#253041] bg-[#0f1923] p-5">
          <SectionHeader icon={FileText} label="AI Diagnostic Explanation" />
          <p className="text-sm leading-relaxed text-slate-300">{session.ai_explanation}</p>
        </div>
      )}

      {/* Footer disclaimer */}
      <div className="border-t border-[#253041] pt-4 text-center text-xs text-slate-600">
        This report contains probable diagnostic advisory only. It does not replace professional hardware inspection.
        Generated by RigMD · Session {session.session_code} · {session.display_date} — {session.display_time}
      </div>
    </div>
  );
}

// ─── Plain Text Copy Helper ───────────────────────────────────────────────────

function buildPlainText(
  session: SessionOption,
  profile: ProfileSnapshot | null,
  recSteps: string[],
  warningSigns: string[]
): string {
  const lines: string[] = [
    `RigMD Diagnostic Report`,
    `Session: ${session.session_code} | ${session.display_date} — ${session.display_time}`,
    ``,
    `=== RESULT SUMMARY ===`,
    `Symptom: ${session.symptom_type}`,
    `Probable Cause: ${session.diagnosed_category}`,
    `Action: ${normalizeAction(session.action_category)}`,
    `Confidence: ${normalizeConfidence(session.confidence_label)}`,
  ];

  if (profile) {
    lines.push(``, `=== SYSTEM PROFILE ===`);
    if (profile.cpu_model) lines.push(`CPU: ${profile.cpu_model}`);
    if (profile.ram_capacity) lines.push(`RAM: ${profile.ram_capacity}`);
    if (profile.storage_type) lines.push(`Storage: ${profile.storage_type} — ${profile.storage_capacity}`);
    if (profile.os_version) lines.push(`OS: ${profile.os_version}`);
    if (profile.gpu_driver) lines.push(`GPU Driver: ${profile.gpu_driver}`);
    if (profile.chipset_driver) lines.push(`Chipset: ${profile.chipset_driver}`);
    if (profile.system_age) lines.push(`System Age: ${profile.system_age}`);
  }

  lines.push(``, `=== SYMPTOM ANSWERS ===`);
  lines.push(`Affected Activity: ${session.affected_activity ?? '—'}`);
  lines.push(`Frequency: ${session.frequency ?? '—'}`);
  lines.push(`Severity: ${session.severity ?? '—'}`);
  lines.push(`Duration: ${session.duration ?? '—'}`);
  lines.push(`Recent Changes: ${session.recent_changes ?? '—'}`);

  if (warningSigns.length > 0) {
    lines.push(``, `=== WARNING SIGNS ===`);
    warningSigns.forEach((s) => lines.push(`• ${s}`));
  }

  if (recSteps.length > 0) {
    lines.push(``, `=== RECOMMENDED NEXT STEPS ===`);
    recSteps.forEach((s, i) => lines.push(`${i + 1}. ${s}`));
  }

  return lines.join('\n');
}

// ─── Empty State ──────────────────────────────────────────────────────────────

function EmptyState({ message }: { message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl border border-[#253041] bg-[#131c28] text-slate-600">
        <FileText size={28} />
      </div>
      <p className="text-base font-semibold text-slate-400">{message}</p>
      <p className="mt-1 text-sm text-slate-600">Complete a diagnostic session first to view its report here.</p>
    </div>
  );
}

// ─── Main View ────────────────────────────────────────────────────────────────

export default function ReportsView() {
  const [sessions, setSessions] = useState<SessionOption[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [profile, setProfile] = useState<ProfileSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Fetch all sessions
  const fetchSessions = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetchWithClient(`${API_BASE_URL}/api/diagnosis/sessions?sort=newest`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      const items: SessionOption[] = data.sessions ?? [];
      setSessions(items);
      if (items.length > 0 && !selectedId) {
        setSelectedId(items[0].session_id);
      }
    } catch (err) {
      setError('Could not load sessions. Ensure the backend is running.');
    } finally {
      setLoading(false);
    }
  }, [selectedId]);

  // Fetch profile snapshot for selected session
  useEffect(() => {
    if (!selectedId) return;
    const fetchDetail = async () => {
      try {
        const res = await fetchWithClient(`${API_BASE_URL}/api/diagnosis/sessions/${selectedId}`);
        if (!res.ok) return;
        const data = await res.json();
        const sess = data.session;
        if (sess?.profile_id) {
          const pRes = await fetchWithClient(`${API_BASE_URL}/api/profiles/${sess.profile_id}`);
          if (pRes.ok) {
            setProfile(await pRes.json());
          } else {
            setProfile(null);
          }
        } else {
          setProfile(null);
        }
      } catch {
        setProfile(null);
      }
    };
    fetchDetail();
  }, [selectedId]);

  useEffect(() => {
    fetchSessions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selectedSession = sessions.find((s) => s.session_id === selectedId) ?? null;

  return (
    <>
      <TopHeader
        title="Reports"
        subtitle="Technician-ready diagnostic reports for your sessions"
      />

      <div className="custom-scrollbar flex-1 overflow-y-auto p-6">
        {/* ── Top Controls ── */}
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
          <SessionSelector
            sessions={sessions}
            selectedId={selectedId}
            onSelect={setSelectedId}
          />

          {selectedSession && (
            <div className="flex items-center gap-2">
              <ActionBadge label={selectedSession.action_category} />
              <ConfidenceBadge label={selectedSession.confidence_label} />
            </div>
          )}
        </div>

        {/* ── Content ── */}
        {loading ? (
          <div className="flex items-center justify-center py-24">
            <RefreshCw size={22} className="animate-spin text-cyan-400" />
            <span className="ml-3 text-sm text-slate-400">Loading sessions…</span>
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <AlertTriangle size={28} className="mb-3 text-orange-400" />
            <p className="text-sm text-slate-400">{error}</p>
          </div>
        ) : sessions.length === 0 ? (
          <EmptyState message="No diagnostic sessions found" />
        ) : !selectedSession ? (
          <EmptyState message="Select a session above to view its report" />
        ) : (
          <ReportBody session={selectedSession} profile={profile} />
        )}
      </div>
    </>
  );
}
