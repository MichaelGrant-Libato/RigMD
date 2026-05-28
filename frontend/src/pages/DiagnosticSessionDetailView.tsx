import { useCallback, useEffect, useState } from 'react';
import { ArrowLeft, CheckCircle2, RefreshCw, ShieldCheck } from 'lucide-react';
import TopHeader from '../components/TopHeader';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8000';

interface SessionDetail {
  session_id: string;
  display_date?: string;
  display_time?: string;
  symptom_type: string;
  affected_activity?: string;
  frequency?: string;
  severity?: string;
  diagnosed_category: string;
  action_category: string;
  confidence_label: string;
  ai_explanation?: string;
  recommended_next_step?: string;
  resolution_status?: string;
  resolution_checked_at?: string | null;
  resolution_summary?: string;
  resolution_proof?: Array<{ label: string; value: string; status: string; meaning: string }>;
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;
}

interface ActionResult {
  success: boolean;
  summary: string;
  cleared?: string;
  deleted_items?: number;
  skipped_errors?: number;
}

interface Props {
  sessionId: string;
  onBack: () => void;
}

function getResolutionLabel(status?: string) {
  if (status === 'resolved') return 'Resolved';
  if (status === 'still_active') return 'Still Active';
  if (status === 'needs_recheck') return 'Needs Recheck';
  return 'Open';
}

function getResolutionStyle(status?: string) {
  if (status === 'resolved') return 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300';
  if (status === 'still_active') return 'border-red-500/40 bg-red-500/10 text-red-300';
  if (status === 'needs_recheck') return 'border-orange-500/40 bg-orange-500/10 text-orange-300';
  return 'border-cyan-500/40 bg-cyan-500/10 text-cyan-300';
}

function getActionButtonLabel(actionId: string) {
  if (actionId === 'clear_user_temp_files') return 'Run Cleanup';
  if (actionId === 'chkdsk_readonly') return 'Run Scan';
  if (actionId.startsWith('open_')) return 'Open Tool';
  if (actionId === 'show_gpu_reset_shortcut') return 'Show Shortcut';
  return 'Run Action';
}

export default function DiagnosticSessionDetailView({ sessionId, onBack }: Props) {
  const [session, setSession] = useState<SessionDetail | null>(null);
  const [actions, setActions] = useState<RemediationAction[]>([]);
  const [actionResults, setActionResults] = useState<Record<string, ActionResult>>({});
  const [loading, setLoading] = useState(true);
  const [checking, setChecking] = useState(false);
  const [runningActionId, setRunningActionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const fetchSession = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${API_BASE_URL}/api/diagnosis/sessions/${sessionId}`);
      if (!response.ok) throw new Error(`Server returned status ${response.status}`);

      const data = await response.json();
      const loadedSession = data.session ?? null;

      setSession(loadedSession);

      if (loadedSession?.diagnosed_category) {
        const actionsResponse = await fetch(
          `${API_BASE_URL}/api/remediation/actions?category=${encodeURIComponent(loadedSession.diagnosed_category)}`
        );

        const actionsData = await actionsResponse.json();
        setActions(Array.isArray(actionsData?.actions) ? actionsData.actions : []);
      }
    } catch {
      setError('Could not open this diagnostic session.');
      setSession(null);
      setActions([]);
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  const runAction = async (action: RemediationAction) => {
    setRunningActionId(action.id);
    setError(null);

    try {
      const response = await fetch(`${API_BASE_URL}/api/remediation/execute`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action_id: action.id }),
      });

      const result = await response.json();

      setActionResults((prev) => ({
        ...prev,
        [action.id]: result,
      }));

      if (result?.success) {
        const statusResponse = await fetch(`${API_BASE_URL}/api/diagnosis/${sessionId}/needs-recheck`, {
          method: 'POST',
        });

        const statusData = await statusResponse.json();

        setSession((prev) =>
          prev
            ? {
                ...prev,
                resolution_status: statusData.resolution_status ?? 'needs_recheck',
                resolution_summary:
                  statusData.last_action_summary ||
                  'A safe action was performed. Run a follow-up check to see if the issue improved.',
              }
            : prev
        );
      }
    } catch {
      setActionResults((prev) => ({
        ...prev,
        [action.id]: { success: false, summary: 'Could not complete this safe action.' },
      }));
    } finally {
      setRunningActionId(null);
    }
  };

  const checkResolution = async () => {
    setChecking(true);
    setError(null);

    try {
      const response = await fetch(`${API_BASE_URL}/api/diagnosis/${sessionId}/check-resolution`, {
        method: 'POST',
      });

      if (!response.ok) throw new Error(`Server returned status ${response.status}`);

      const data = await response.json();

      setSession((prev) =>
        prev
          ? {
              ...prev,
              resolution_status: data.resolution_status,
              resolution_checked_at: data.resolution_checked_at,
              resolution_summary: data.resolution_summary,
              resolution_proof: data.resolution_proof,
            }
          : prev
      );
    } catch {
      setError('Could not check whether this issue is fixed yet.');
    } finally {
      setChecking(false);
    }
  };

  useEffect(() => {
    fetchSession();
  }, [fetchSession]);

  return (
    <>
      <TopHeader title="Diagnosis Detail" subtitle="Resolve this saved session and verify the result" />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="mx-auto w-full max-w-[1100px] space-y-5">
          <button
            type="button"
            onClick={onBack}
            className="inline-flex items-center gap-2 rounded-lg border border-[#30363d] bg-[#161b22] px-4 py-2 text-sm font-bold text-cyan-400 hover:border-cyan-500/40"
          >
            <ArrowLeft size={16} />
            Back to History
          </button>

          {loading ? (
            <div className="rounded-2xl border border-[#30363d] bg-[#161b22] px-6 py-16 text-center">
              <RefreshCw size={34} className="mx-auto mb-4 animate-spin text-cyan-400" />
              <h3 className="text-lg font-bold text-white">Opening diagnostic session</h3>
            </div>
          ) : error ? (
            <div className="rounded-2xl border border-red-500/20 bg-red-500/10 p-5 text-red-300">{error}</div>
          ) : session ? (
            <>
              <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6">
                <div className="flex flex-col gap-4 border-b border-[#30363d] pb-5 md:flex-row md:items-start md:justify-between">
                  <div>
                    <div className="text-xs font-bold uppercase tracking-[0.22em] text-slate-500">
                      {session.display_date} {session.display_time}
                    </div>
                    <h2 className="mt-2 text-2xl font-bold text-white">{session.diagnosed_category}</h2>
                    <p className="mt-2 text-sm text-slate-400">{session.symptom_type}</p>
                  </div>

                  <div className="flex flex-wrap gap-2">
                    <span className="rounded border border-emerald-500/40 bg-emerald-500/10 px-3 py-1 text-xs font-bold uppercase text-emerald-400">
                      {session.action_category}
                    </span>
                    <span className="rounded border border-cyan-500/40 bg-cyan-500/10 px-3 py-1 text-xs font-bold uppercase text-cyan-400">
                      {session.confidence_label}
                    </span>
                    <span className={`rounded border px-3 py-1 text-xs font-bold uppercase ${getResolutionStyle(session.resolution_status)}`}>
                      {getResolutionLabel(session.resolution_status)}
                    </span>
                  </div>
                </div>

                <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
                  <div className="rounded-xl border border-[#253041] bg-[#0d1117] p-4">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Activity</p>
                    <p className="mt-1 text-sm font-semibold text-white">{session.affected_activity || 'Not recorded'}</p>
                  </div>
                  <div className="rounded-xl border border-[#253041] bg-[#0d1117] p-4">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Frequency</p>
                    <p className="mt-1 text-sm font-semibold text-white">{session.frequency || 'Not recorded'}</p>
                  </div>
                  <div className="rounded-xl border border-[#253041] bg-[#0d1117] p-4">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Severity</p>
                    <p className="mt-1 text-sm font-semibold text-white">{session.severity || 'Not recorded'}</p>
                  </div>
                </div>

                <div className="mt-5 rounded-xl border border-[#253041] bg-[#0d1117] p-4">
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Explanation</h3>
                  <p className="mt-2 text-sm leading-relaxed text-slate-300">
                    {session.ai_explanation || 'No explanation saved for this session.'}
                  </p>
                </div>
              </section>

              <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6">
                <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                  <div>
                    <h3 className="text-sm font-bold uppercase tracking-wider text-white">Resolution Status</h3>
                    <p className="mt-2 text-sm leading-relaxed text-slate-400">
                      {session.resolution_summary || 'Use a safe action if needed, then check whether the live issue is fixed.'}
                    </p>
                  </div>

                  <button
                    type="button"
                    onClick={checkResolution}
                    disabled={checking}
                    className="inline-flex items-center justify-center gap-2 rounded-lg border border-cyan-500/40 bg-cyan-500/5 px-4 py-2 text-sm font-bold text-cyan-400 hover:bg-cyan-500/10 disabled:opacity-50"
                  >
                    <RefreshCw size={16} className={checking ? 'animate-spin' : ''} />
                    {checking ? 'Checking...' : 'Check if Fixed'}
                  </button>
                </div>

                {session.resolution_proof && session.resolution_proof.length > 0 && (
                  <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
                    {session.resolution_proof.map((item) => (
                      <div key={item.label} className="rounded-xl border border-[#253041] bg-[#0d1117] p-4">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{item.label}</p>
                        <p className="mt-1 text-sm font-bold text-white">{item.value}</p>
                        <p className="mt-2 text-xs leading-relaxed text-slate-500">{item.meaning}</p>
                      </div>
                    ))}
                  </div>
                )}
              </section>

              <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6">
                <div className="flex items-start gap-3">
                  <ShieldCheck size={20} className="mt-0.5 text-cyan-400" />
                  <div>
                    <h3 className="text-sm font-bold uppercase tracking-wider text-white">Safe Actions Available</h3>
                    <p className="mt-2 text-sm leading-relaxed text-slate-400">
                      Run one safe action, then use Check if Fixed to prove whether the issue is resolved.
                    </p>
                  </div>
                </div>

                <div className="mt-5 space-y-3">
                  {actions.length === 0 ? (
                    <div className="rounded-xl border border-[#253041] bg-[#0d1117] p-4 text-sm text-slate-500">
                      No safe action is available for this saved diagnosis.
                    </div>
                  ) : (
                    actions.map((action) => {
                      const result = actionResults[action.id];
                      const running = runningActionId === action.id;

                      return (
                        <div key={action.id} className="rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4">
                          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                            <div>
                              <h4 className="text-sm font-bold text-cyan-400">{action.label}</h4>
                              <p className="mt-1 text-xs leading-relaxed text-slate-400">{action.description}</p>
                              <p className="mt-1 text-[11px] text-slate-500">{action.risk}</p>
                            </div>

                            {!result && (
                              <button
                                type="button"
                                onClick={() => runAction(action)}
                                disabled={runningActionId !== null}
                                className="rounded-lg border border-cyan-500/40 bg-cyan-500/10 px-4 py-2 text-xs font-bold text-cyan-300 hover:bg-cyan-500/15 disabled:opacity-50"
                              >
                                {running ? 'Running...' : getActionButtonLabel(action.id)}
                              </button>
                            )}
                          </div>

                          {result && (
                            <div className={`mt-3 rounded-lg border p-3 text-xs ${
                              result.success
                                ? 'border-emerald-500/20 bg-emerald-500/5 text-emerald-300'
                                : 'border-red-500/20 bg-red-500/5 text-red-300'
                            }`}>
                              <p className="font-semibold">{result.summary}</p>
                              {result.cleared && <p className="mt-1">Cleared: {result.cleared}</p>}
                            </div>
                          )}
                        </div>
                      );
                    })
                  )}
                </div>
              </section>
            </>
          ) : (
            <div className="rounded-2xl border border-[#30363d] bg-[#161b22] px-6 py-16 text-center">
              <CheckCircle2 size={40} className="mx-auto mb-4 text-slate-600" />
              <h3 className="text-lg font-bold text-white">Session not found</h3>
            </div>
          )}
        </div>
      </div>
    </>
  );
}