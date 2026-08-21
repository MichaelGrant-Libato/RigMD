import { useCallback, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { ArrowLeft, CheckCircle2, RefreshCw, ShieldCheck } from 'lucide-react';
import TopHeader from '../components/TopHeader';
import { buttonTap, cardFadeUp, cardTransition, drawerSlideRight, pageFade, pageTransition, staggerContainer } from '../lib/motion';
import { apiFetch } from '../lib/api';

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

interface ExecutionProof {
  label: string;
  status: string;
  meaning: string;
  after?: string;
}

interface ExecutionResult {
  success: boolean;
  summary: string;
  proof?: ExecutionProof[];
}

interface SafetyEvaluation {
  isApproved: boolean;
  rejectionReason?: string;
  warnings?: string[];
}

interface RemediationActionDef {
  id: string;
  name: string;
}

interface RemediationPlan {
  plannedActions: RemediationActionDef[];
  strategyReasoning: string;
}

interface AutonomyResult {
  plan?: RemediationPlan;
  safety?: SafetyEvaluation;
  execution?: ExecutionResult;
  verification?: string | null;
  trace?: string;
}

interface ActionResult {
  success: boolean;
  summary: string;
  cleared?: string;
  deleted_items?: number;
  skipped_errors?: number;
  autonomy?: AutonomyResult;
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
  const [dryRunResult, setDryRunResult] = useState<AutonomyResult | null>(null);
  const [dryRunLoading, setDryRunLoading] = useState(false);

  const fetchSession = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await apiFetch(`/api/diagnosis/sessions/${sessionId}`);
      if (!response.ok) throw new Error(`Server returned status ${response.status}`);

      const data = await response.json();
      const loadedSession = data?.session ?? (data?.session_id ? data : null);

      setSession(loadedSession);

      if (loadedSession?.diagnosed_category) {
        const actionsResponse = await apiFetch(
          `/api/remediation/actions?category=${encodeURIComponent(loadedSession.diagnosed_category)}`
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
      const response = await apiFetch(`/api/autonomy/execute`, {
        method: 'POST',
        body: JSON.stringify({
          diagnosedCategory: session?.diagnosed_category ?? '',
          userConsentProvided: true,
        }),
      });

      const data: AutonomyResult = await response.json();

      setActionResults((prev) => ({
        ...prev,
        [action.id]: {
          success: data.execution?.success ?? false,
          summary: data.execution?.summary ?? (data.safety?.isApproved === false
            ? `Safety check blocked: ${data.safety.rejectionReason || 'Action not approved.'}`
            : 'Execution completed.'),
          autonomy: data,
        },
      }));

      if (data.execution?.success) {
        const statusResponse = await apiFetch(`/api/diagnosis/${sessionId}/needs-recheck`, {
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

  const handleDryRun = async () => {
    if (!session?.diagnosed_category) return;
    setDryRunLoading(true);
    setDryRunResult(null);
    try {
      const response = await apiFetch(`/api/autonomy/dry-run`, {
        method: 'POST',
        body: JSON.stringify({ diagnosedCategory: session.diagnosed_category }),
      });
      const data: AutonomyResult = await response.json();
      setDryRunResult(data);
    } catch {
      setDryRunResult({ trace: 'Could not connect to the backend for simulation.' });
    } finally {
      setDryRunLoading(false);
    }
  };

  const checkResolution = async () => {
    setChecking(true);
    setError(null);

    try {
      const response = await apiFetch(`/api/diagnosis/${sessionId}/check-resolution`, {
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

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8"
      >
        <div className="mx-auto w-full max-w-[1100px] space-y-5">
          <motion.button
            type="button"
            onClick={onBack}
            whileTap={buttonTap}
            className="inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-4 py-2 text-sm font-bold text-cyan-400 hover:border-cyan-500/40"
          >
            <ArrowLeft size={16} />
            Back to History
          </motion.button>

          {loading ? (
            <div className="space-y-4 rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6">
              {Array.from({ length: 5 }).map((_, index) => (
                <div key={index} className="h-12 animate-pulse rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)]" />
              ))}
            </div>
          ) : error ? (
            <div className="rounded-2xl border border-red-500/20 bg-red-500/10 p-5 text-red-300">{error}</div>
          ) : session ? (
            <>
              <motion.section variants={drawerSlideRight} initial="hidden" animate="visible" exit="exit" transition={pageTransition} className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6">
                <div className="flex flex-col gap-4 border-b border-[var(--rigmd-border)] pb-5 md:flex-row md:items-start md:justify-between">
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
                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Activity</p>
                    <p className="mt-1 text-sm font-semibold text-white">{session.affected_activity || 'Not recorded'}</p>
                  </div>
                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Frequency</p>
                    <p className="mt-1 text-sm font-semibold text-white">{session.frequency || 'Not recorded'}</p>
                  </div>
                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Severity</p>
                    <p className="mt-1 text-sm font-semibold text-white">{session.severity || 'Not recorded'}</p>
                  </div>
                </div>

                <div className="mt-5 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Explanation</h3>
                  <p className="mt-2 text-sm leading-relaxed text-slate-300">
                    {session.ai_explanation || 'No explanation saved for this session.'}
                  </p>
                </div>
              </motion.section>

              <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={cardTransition} className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6">
                <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                  <div>
                    <h3 className="text-sm font-bold uppercase tracking-wider text-white">Resolution Status</h3>
                    <p className="mt-2 text-sm leading-relaxed text-slate-400">
                      {session.resolution_summary || 'Use a safe action if needed, then check whether the live issue is fixed.'}
                    </p>
                  </div>

                  <motion.button
                    type="button"
                    onClick={checkResolution}
                    disabled={checking}
                    whileTap={buttonTap}
                    className="inline-flex items-center justify-center gap-2 rounded-lg border border-cyan-500/40 bg-cyan-500/5 px-4 py-2 text-sm font-bold text-cyan-400 hover:bg-cyan-500/10 disabled:opacity-50"
                  >
                    <RefreshCw size={16} className={checking ? 'animate-spin' : ''} />
                    {checking ? 'Checking...' : 'Check if Fixed'}
                  </motion.button>
                </div>

                {session.resolution_proof && session.resolution_proof.length > 0 && (
                  <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
                    {session.resolution_proof.map((item) => (
                      <div key={item.label} className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{item.label}</p>
                        <p className="mt-1 text-sm font-bold text-white">{item.value}</p>
                        <p className="mt-2 text-xs leading-relaxed text-slate-500">{item.meaning}</p>
                      </div>
                    ))}
                  </div>
                )}
              </motion.section>

              <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={cardTransition} className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6">
                <div className="flex items-start gap-3">
                  <ShieldCheck size={20} className="mt-0.5 text-cyan-400" />
                  <div className="flex-1">
                    <h3 className="text-sm font-bold uppercase tracking-wider text-white">Autonomous Safe Actions</h3>
                    <p className="mt-2 text-sm leading-relaxed text-slate-400">
                      RigMD will plan and apply the safest fix autonomously. Use <strong className="text-cyan-400">Simulate</strong> to preview the plan without making any changes.
                    </p>
                  </div>
                  <motion.button
                    type="button"
                    onClick={handleDryRun}
                    disabled={dryRunLoading || runningActionId !== null}
                    whileTap={buttonTap}
                    className="inline-flex items-center gap-2 rounded-lg border border-cyan-800/50 bg-transparent px-4 py-2 text-xs font-bold text-cyan-400 hover:bg-cyan-500/5 disabled:opacity-50"
                  >
                    {dryRunLoading ? 'Simulating...' : '🔍 Simulate'}
                  </motion.button>
                </div>

                {dryRunResult && (
                  <div className="mt-4 rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4 text-xs text-slate-300">
                    <p className="mb-2 font-bold text-cyan-400">🔍 Simulation Result (No changes made)</p>
                    {dryRunResult.plan?.plannedActions && dryRunResult.plan.plannedActions.length > 0 && (
                      <p className="mb-1"><strong>Planned Actions: </strong>{dryRunResult.plan.plannedActions.map(a => a.name).join(' → ')}</p>
                    )}
                    {dryRunResult.safety && (
                      <p className="mb-1">
                        <strong>Safety: </strong>
                        <span className={dryRunResult.safety.isApproved ? 'text-emerald-400' : 'text-red-400'}>
                          {dryRunResult.safety.isApproved ? 'Approved ✓' : `Blocked — ${dryRunResult.safety.rejectionReason || 'Not approved'}`}
                        </span>
                        {dryRunResult.safety.warnings && dryRunResult.safety.warnings.length > 0 && (
                          <span className="ml-2 text-yellow-400">⚠ {dryRunResult.safety.warnings.join(', ')}</span>
                        )}
                      </p>
                    )}
                    {dryRunResult.plan?.strategyReasoning && (
                      <p className="text-slate-500"><strong>Reasoning: </strong>{dryRunResult.plan.strategyReasoning}</p>
                    )}
                    {dryRunResult.trace && !dryRunResult.plan && (
                      <p className="text-slate-500">{dryRunResult.trace}</p>
                    )}
                  </div>
                )}

                <div className="mt-5 space-y-3">
                  {actions.length === 0 ? (
                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4 text-sm text-slate-500">
                      No safe action is available for this saved diagnosis.
                    </div>
                  ) : (
                    <motion.div variants={staggerContainer} initial="hidden" animate="visible" className="space-y-3">
                    {actions.map((action) => {
                      const result = actionResults[action.id];
                      const running = runningActionId === action.id;

                      return (
                        <motion.div key={action.id} variants={cardFadeUp} transition={cardTransition} className="rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4">
                          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                            <div>
                              <h4 className="text-sm font-bold text-cyan-400">{action.label}</h4>
                              <p className="mt-1 text-xs leading-relaxed text-slate-400">{action.description}</p>
                              <p className="mt-1 text-[11px] text-slate-500">{action.risk}</p>
                            </div>

                          {!result && (
                              <motion.button
                                type="button"
                                onClick={() => runAction(action)}
                                disabled={runningActionId !== null}
                                whileTap={buttonTap}
                                className="rounded-lg border border-cyan-500/40 bg-cyan-500/10 px-4 py-2 text-xs font-bold text-cyan-300 hover:bg-cyan-500/15 disabled:opacity-50"
                              >
                                {running ? 'Running...' : 'Run Autonomous Fix'}
                              </motion.button>
                            )}
                          </div>

                          {result && (
                            <div className={`mt-3 rounded-lg border p-3 text-xs ${
                              result.success
                                ? 'border-emerald-500/20 bg-emerald-500/5 text-emerald-300'
                                : 'border-red-500/20 bg-red-500/5 text-red-300'
                            }`}>
                              <p className="font-semibold">{result.summary}</p>
                              {result.autonomy?.execution?.proof && result.autonomy.execution.proof.length > 0 && (
                                <div className="mt-2 space-y-1">
                                  {result.autonomy.execution.proof.map((p, i) => (
                                    <p key={i} className="text-slate-400">
                                      {p.label}: <span className="text-slate-300">{p.status}</span>{p.after ? ` → ${p.after}` : ''}
                                    </p>
                                  ))}
                                </div>
                              )}
                              {result.autonomy?.verification && (
                                <p className="mt-1 text-cyan-400">Verification: {result.autonomy.verification}</p>
                              )}
                              {result.cleared && <p className="mt-1">Cleared: {result.cleared}</p>}
                            </div>
                          )}
                        </motion.div>
                      );
                    })
                    }
                    </motion.div>
                  )}
                </div>
              </motion.section>
            </>
          ) : (
            <div className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-6 py-16 text-center">
              <CheckCircle2 size={40} className="mx-auto mb-4 text-slate-600" />
              <h3 className="text-lg font-bold text-white">Session not found</h3>
            </div>
          )}
        </div>
      </motion.div>
    </>
  );
}
