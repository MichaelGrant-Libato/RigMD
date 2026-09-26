import { useEffect, useState } from 'react';
import {
  Activity,
  AlertTriangle,
  Bot,
  CheckCircle2,
  Cpu,
  History,
  Play,
  RefreshCw,
  Settings,
  ShieldAlert,
  ShieldCheck,
  Sparkles,
  Terminal,
  Wrench,
} from 'lucide-react';
import TopHeader from '../components/TopHeader';
import type { DashboardSummary, HardwareStats, PageKey } from '../types/rigmd';
import {
  getAgentSettings,
  getBackendErrorMessage,
  runAutonomyExecution,
  runAutonomousDoctor,
  startRemediationStream,
  stopRemediationStream,
  type AgentSettingsResponse,
  type AutonomousDoctorResponse,
  type AutonomyResult,
  type ReActTraceStep,
} from '../services/autonomyService';

interface AiAgentDoctorViewProps {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
  onViewSession: (sessionId: string) => void;
}

export default function AiAgentDoctorView({
  stats,
  dashboard,
  setActivePage,
  onViewSession,
}: AiAgentDoctorViewProps) {
  const [settings, setSettings] = useState<AgentSettingsResponse | null>(null);
  const [userPrompt, setUserPrompt] = useState('');
  const [autoExecuteSafeFixes, setAutoExecuteSafeFixes] = useState(false);
  const [isRunningDoctor, setIsRunningDoctor] = useState(false);
  const [isExecutingFix, setIsExecutingFix] = useState(false);
  const [liveProgressLogs, setLiveProgressLogs] = useState<string[]>([]);
  const [liveReActSteps, setLiveReActSteps] = useState<ReActTraceStep[]>([]);
  const [doctorResult, setDoctorResult] = useState<AutonomousDoctorResponse | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    getAgentSettings()
      .then((res) => {
        if (!active) return;
        setSettings(res);
        setAutoExecuteSafeFixes(Boolean(res.autoExecuteSafeTier1));
      })
      .catch(() => {
        // Ignore non-fatal settings fetch failure
      });

    return () => {
      active = false;
      void stopRemediationStream();
    };
  }, []);

  const handleRunAutonomousDoctor = async () => {
    setErrorMsg(null);
    setIsRunningDoctor(true);
    setDoctorResult(null);
    setLiveProgressLogs([
      '[AUTO-DOCTOR] Initializing whole-system ReAct diagnostic & memory loop...',
    ]);
    setLiveReActSteps([]);

    try {
      await startRemediationStream(
        (msg) => {
          setLiveProgressLogs((prev) =>
            prev[prev.length - 1] === msg ? prev : [...prev, msg],
          );
        },
        (step) => {
          setLiveReActSteps((prev) => {
            const exists = prev.some(
              (s) => s.stepIndex === step.stepIndex && s.title === step.title,
            );
            return exists ? prev : [...prev, step];
          });
        },
      );

      const response = await runAutonomousDoctor({
        userPrompt: userPrompt.trim() || undefined,
        autoExecuteSafeFixes,
      });

      setDoctorResult(response);
      if (response.orchestration?.reasoningSteps?.length) {
        setLiveReActSteps(response.orchestration.reasoningSteps);
      }
    } catch (err) {
      setErrorMsg(getBackendErrorMessage(err));
    } finally {
      setIsRunningDoctor(false);
      await stopRemediationStream();
    }
  };

  const handleApproveAndExecute = async () => {
    if (!doctorResult) return;

    const plannedAction =
      doctorResult.orchestration?.plan?.plannedActions?.[0];
    const toolName =
      doctorResult.orchestration?.proposedTool?.toolName ?? plannedAction?.id;
    const toolArgs =
      doctorResult.orchestration?.proposedTool?.argumentsJson ??
      plannedAction?.toolArgumentsJson;

    setErrorMsg(null);
    setIsExecutingFix(true);

    try {
      await startRemediationStream(
        (msg) => {
          setLiveProgressLogs((prev) =>
            prev[prev.length - 1] === msg ? prev : [...prev, msg],
          );
        },
        (step) => {
          setLiveReActSteps((prev) => [...prev, step]);
        },
      );

      const execResult: AutonomyResult = await runAutonomyExecution({
        sessionId: doctorResult.sessionId,
        diagnosedCategory: doctorResult.diagnosedCategory,
        userConsentProvided: true,
        toolName,
        toolArgumentsJson: toolArgs,
      });

      setDoctorResult({
        ...doctorResult,
        autoExecuted: true,
        orchestration: {
          ...doctorResult.orchestration,
          ...execResult,
          reasoningSteps:
            execResult.reasoningSteps && execResult.reasoningSteps.length > 0
              ? execResult.reasoningSteps
              : doctorResult.orchestration.reasoningSteps,
        },
      });
    } catch (err) {
      setErrorMsg(getBackendErrorMessage(err));
    } finally {
      setIsExecutingFix(false);
      await stopRemediationStream();
    }
  };

  const orchestration = doctorResult?.orchestration;
  const primaryPlannedAction = orchestration?.plan?.plannedActions?.[0];
  const dryRunPreview =
    orchestration?.dryRunPreview ?? orchestration?.proposedTool?.dryRunPreview;
  const executionReport = orchestration?.execution;
  const verificationReport = orchestration?.verificationReport;
  const displayedSteps =
    orchestration?.reasoningSteps && orchestration.reasoningSteps.length > 0
      ? orchestration.reasoningSteps
      : liveReActSteps;

  return (
    <div className="flex flex-col gap-6 pb-10">
      <TopHeader
        title="AI Agent (Autonomous PC Doctor)"
        subtitle="Whole-system Windows diagnostic agent with full hardware access, SQLite historical memory, and verified OS remediation"
      />

      {/* Whole-System 3-Pillar Telemetry & Memory Context Bar */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        <div className="rigmd-card-surface border border-white/10 p-4">
          <div className="mb-2 flex items-center justify-between">
            <span className="text-xs font-bold uppercase tracking-wider text-cyan-300">
              1. Live Device Telemetry
            </span>
            <Cpu size={16} className="text-cyan-300" />
          </div>
          <p className="truncate text-sm font-semibold text-white">
            {stats?.device_name || 'Local Windows PC'} ({stats?.device_type || 'Desktop'})
          </p>
          <div className="mt-2 flex flex-wrap gap-2 text-xs text-slate-300">
            <span className="border border-white/10 bg-black/25 px-2 py-0.5">
              CPU: {stats?.cpu?.usage_percent ?? 0}%
            </span>
            <span className="border border-white/10 bg-black/25 px-2 py-0.5">
              RAM: {stats?.ram?.usage_percent ?? 0}% ({stats?.ram?.used_gb ?? 0}/
              {stats?.ram?.total_gb ?? 0} GB)
            </span>
            <span className="border border-white/10 bg-black/25 px-2 py-0.5">
              Disk: {stats?.disk?.usage_percent ?? 0}%
            </span>
          </div>
        </div>

        <div className="rigmd-card-surface border border-white/10 p-4">
          <div className="mb-2 flex items-center justify-between">
            <span className="text-xs font-bold uppercase tracking-wider text-emerald-300">
              2. Past Checks Memory
            </span>
            <History size={16} className="text-emerald-300" />
          </div>
          <p className="text-sm font-semibold text-white">
            {dashboard.totals?.total_sessions ?? 0} Saved Diagnostic Sessions
          </p>
          <p className="mt-1 truncate text-xs text-slate-400">
            Latest:{' '}
            {dashboard.last_diagnosis
              ? `${dashboard.last_diagnosis.diagnosed_category} (${dashboard.last_diagnosis.action_category})`
              : 'No prior check recorded yet'}
          </p>
        </div>

        <div className="rigmd-card-surface border border-white/10 p-4">
          <div className="mb-2 flex items-center justify-between">
            <span className="text-xs font-bold uppercase tracking-wider text-amber-300">
              3. Repeated Problems & Alerts
            </span>
            <Activity size={16} className="text-amber-300" />
          </div>
          <p className="text-sm font-semibold text-white">
            {dashboard.recurring_issues_count ?? 0} Recurring Patterns ·{' '}
            {dashboard.warning_signs_active_count ?? 0} Active Alerts
          </p>
          <p className="mt-1 text-xs text-slate-400">
            Escalated sessions: {dashboard.totals?.escalated_count ?? 0}
          </p>
        </div>
      </div>

      {/* Autonomous Doctor Command Center Card */}
      <div className="rigmd-card-surface border border-cyan-400/25 p-6">
        <div className="flex flex-col justify-between gap-4 border-b border-white/10 pb-4 lg:flex-row lg:items-center">
          <div className="flex items-start gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center border border-cyan-400/35 bg-cyan-400/10 text-cyan-200">
              <Bot size={24} />
            </div>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="text-lg font-bold text-white">
                  Whole-System Autonomous Doctor
                </h2>
                <span className="border border-cyan-400/35 bg-cyan-400/10 px-2 py-0.5 text-[11px] font-semibold text-cyan-200">
                  {settings?.activeEngine ||
                    'Local Deterministic ReAct Engine + Gemini Cascade'}
                </span>
                <span className="border border-white/15 bg-white/5 px-2 py-0.5 text-[11px] text-slate-300">
                  {settings?.registeredToolCount ?? 18} OS Tools Armed
                </span>
              </div>
              <p className="mt-1 text-xs text-slate-300">
                Unlike single-component checks, the Autonomous Doctor inspects your full hardware profile, queries your SQLite Past Checks and Repeated Problems, runs live Windows OS diagnostic probes, and synthesizes a verified repair plan.
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => setActivePage('settings')}
            className="inline-flex shrink-0 items-center gap-2 border border-white/15 bg-white/5 px-3 py-2 text-xs font-semibold text-slate-200 transition hover:border-cyan-400/40 hover:text-white"
          >
            <Settings size={14} />
            AI Engine Settings
          </button>
        </div>

        <div className="mt-5 grid grid-cols-1 gap-4 lg:grid-cols-12">
          <div className="lg:col-span-8">
            <label
              htmlFor="auto-doctor-prompt"
              className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-300"
            >
              Optional Symptom or Context Note (Leave blank for full autonomous inspection)
            </label>
            <input
              id="auto-doctor-prompt"
              type="text"
              value={userPrompt}
              onChange={(e) => setUserPrompt(e.target.value)}
              placeholder="e.g., PC feels sluggish when Chrome and VS Code are open, or frequent stutters after boot..."
              disabled={isRunningDoctor || isExecutingFix}
              className="w-full border border-white/15 bg-black/35 px-3.5 py-2.5 text-sm text-white placeholder-slate-500 focus:border-cyan-400 focus:outline-none"
            />
          </div>

          <div className="flex flex-col justify-end lg:col-span-4">
            <label className="flex cursor-pointer items-center gap-2.5 border border-white/10 bg-black/25 px-3.5 py-2.5 text-xs text-slate-200">
              <input
                type="checkbox"
                checked={autoExecuteSafeFixes}
                onChange={(e) => setAutoExecuteSafeFixes(e.target.checked)}
                disabled={isRunningDoctor || isExecutingFix}
                className="h-4 w-4 accent-cyan-400"
              />
              <span>
                <strong className="text-cyan-200">Safe Tier-1 Auto-Pilot:</strong>{' '}
                Auto-run non-destructive cleanups (Temp/Cache/DNS)
              </span>
            </label>
          </div>
        </div>

        <div className="mt-5 flex flex-wrap items-center justify-between gap-4">
          <div className="text-xs text-slate-400">
            Tier-2 system changes (killing user apps, resetting network adapters, restarting services) always pause for your explicit confirmation.
          </div>

          <button
            type="button"
            onClick={handleRunAutonomousDoctor}
            disabled={isRunningDoctor || isExecutingFix}
            className="inline-flex items-center gap-2.5 border border-cyan-400/50 bg-cyan-500/20 px-5 py-2.5 text-sm font-bold text-cyan-100 shadow-lg transition hover:bg-cyan-500/30 disabled:opacity-50"
          >
            {isRunningDoctor ? (
              <>
                <RefreshCw size={16} className="animate-spin" />
                Running Whole-System ReAct Loop...
              </>
            ) : (
              <>
                <Sparkles size={16} />
                Run Autonomous PC Doctor
              </>
            )}
          </button>
        </div>

        {errorMsg && (
          <div className="mt-4 flex items-start gap-2.5 border border-red-500/40 bg-red-950/30 p-3.5 text-xs text-red-200">
            <AlertTriangle size={16} className="mt-0.5 shrink-0 text-red-400" />
            <div>{errorMsg}</div>
          </div>
        )}
      </div>

      {/* Live ReAct Reasoning & Tool Observation Stream */}
      {(isRunningDoctor ||
        liveProgressLogs.length > 0 ||
        displayedSteps.length > 0) && (
        <div className="rigmd-card-surface border border-white/10 p-6">
          <div className="mb-4 flex items-center justify-between border-b border-white/10 pb-3">
            <div className="flex items-center gap-2">
              <Terminal size={18} className="text-cyan-300" />
              <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                Multi-Turn ReAct Reasoning & Tool Telemetry Trace
              </h3>
            </div>
            {orchestration?.engineMode && (
              <span className="border border-cyan-400/30 bg-cyan-400/10 px-2.5 py-0.5 text-xs font-semibold text-cyan-200">
                Engine: {orchestration.engineMode}
              </span>
            )}
          </div>

          {liveProgressLogs.length > 0 && (
            <div className="mb-4 max-h-36 overflow-y-auto border border-white/10 bg-black/40 p-3 font-mono text-xs text-cyan-200">
              {liveProgressLogs.map((log, idx) => (
                <div key={idx} className="py-0.5">
                  {log}
                </div>
              ))}
            </div>
          )}

          {displayedSteps.length > 0 && (
            <div className="space-y-3">
              {displayedSteps.map((step, idx) => (
                <div
                  key={`${step.stepIndex}-${idx}`}
                  className="border border-white/10 bg-black/25 p-3.5"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span className="border border-cyan-400/30 bg-cyan-400/10 px-2 py-0.5 text-[11px] font-bold uppercase text-cyan-200">
                        Step {step.stepIndex}: {step.stepType}
                      </span>
                      <span className="text-xs font-semibold text-white">
                        {step.title}
                      </span>
                    </div>
                    {step.toolName && (
                      <span className="font-mono text-[11px] text-emerald-300">
                        Tool: {step.toolName}
                        {typeof step.durationMs === 'number'
                          ? ` (${step.durationMs}ms)`
                          : ''}
                      </span>
                    )}
                  </div>
                  <p className="mt-2 whitespace-pre-wrap text-xs text-slate-300">
                    {step.content}
                  </p>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Synthesis, Dry-Run Impact Preview & Execution / Verification Proof */}
      {doctorResult && (
        <div className="rigmd-card-surface border border-emerald-400/25 p-6">
          <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 pb-4">
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="border border-emerald-400/35 bg-emerald-400/10 px-2.5 py-0.5 text-xs font-bold uppercase text-emerald-200">
                  Diagnosis: {doctorResult.diagnosedCategory}
                </span>
                <span className="border border-cyan-400/35 bg-cyan-400/10 px-2.5 py-0.5 text-xs font-semibold text-cyan-200">
                  Action Tier: {doctorResult.actionCategory}
                </span>
                <span className="border border-white/15 bg-white/5 px-2.5 py-0.5 text-xs text-slate-300">
                  Confidence: {doctorResult.confidenceLabel}
                </span>
              </div>
              <h3 className="mt-2 text-base font-bold text-white">
                Autonomous Root-Cause Synthesis
              </h3>
            </div>

            <button
              type="button"
              onClick={() => onViewSession(doctorResult.sessionId)}
              className="border border-white/15 bg-white/5 px-3 py-1.5 text-xs font-semibold text-cyan-200 transition hover:border-cyan-400/40 hover:text-white"
            >
              Open Full Session Record →
            </button>
          </div>

          <p className="mt-4 whitespace-pre-wrap text-sm leading-relaxed text-slate-200">
            {orchestration?.rootCauseAnalysis || doctorResult.aiExplanation}
          </p>

          {/* Dry-Run Preview & Action Approval */}
          {primaryPlannedAction && (
            <div className="mt-6 border border-cyan-400/25 bg-black/30 p-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <Wrench size={16} className="text-cyan-300" />
                  <h4 className="text-sm font-bold text-white">
                    Recommended OS Remediation Tool: {primaryPlannedAction.name}
                  </h4>
                </div>
                <span className="border border-amber-400/30 bg-amber-400/10 px-2 py-0.5 text-xs font-semibold text-amber-200">
                  Safety Tier: {primaryPlannedAction.riskLevel || primaryPlannedAction.safetyTier || 'Tier 1'}
                </span>
              </div>

              <p className="mt-2 text-xs text-slate-300">
                {dryRunPreview?.whatWillHappen || primaryPlannedAction.description}
              </p>

              {dryRunPreview && (
                <div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-300">
                  <span className="border border-white/10 bg-black/40 px-2.5 py-1">
                    Requires Admin: {dryRunPreview.requiresAdmin ? 'Yes' : 'No'}
                  </span>
                  <span className="border border-white/10 bg-black/40 px-2.5 py-1">
                    Process Elevated: {dryRunPreview.isRunningAsAdmin ? 'Yes' : 'Standard User'}
                  </span>
                  {dryRunPreview.affectedItemsCount > 0 && (
                    <span className="border border-white/10 bg-black/40 px-2.5 py-1">
                      Affected Items: {dryRunPreview.affectedItemsCount}
                    </span>
                  )}
                </div>
              )}

              {dryRunPreview?.warnings && dryRunPreview.warnings.length > 0 && (
                <div className="mt-3 space-y-1 border border-amber-400/30 bg-amber-950/20 p-3 text-xs text-amber-200">
                  {dryRunPreview.warnings.map((w, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <ShieldAlert size={14} className="shrink-0 text-amber-400" />
                      <span>{w}</span>
                    </div>
                  ))}
                </div>
              )}

              {!executionReport && (
                <div className="mt-4 flex items-center justify-end gap-3">
                  <button
                    type="button"
                    onClick={handleApproveAndExecute}
                    disabled={isExecutingFix}
                    className="inline-flex items-center gap-2 border border-emerald-400/50 bg-emerald-500/20 px-4 py-2 text-xs font-bold text-emerald-100 transition hover:bg-emerald-500/30 disabled:opacity-50"
                  >
                    {isExecutingFix ? (
                      <>
                        <RefreshCw size={14} className="animate-spin" />
                        Executing & Verifying...
                      </>
                    ) : (
                      <>
                        <Play size={14} />
                        Approve & Execute Remediation
                      </>
                    )}
                  </button>
                </div>
              )}
            </div>
          )}

          {/* Execution & Before/After Verification Proof */}
          {executionReport && (
            <div className="mt-6 border border-emerald-400/30 bg-emerald-950/15 p-4">
              <div className="flex items-center gap-2">
                {executionReport.success ? (
                  <CheckCircle2 size={18} className="text-emerald-400" />
                ) : (
                  <AlertTriangle size={18} className="text-amber-400" />
                )}
                <h4 className="text-sm font-bold text-white">
                  {doctorResult.autoExecuted
                    ? 'Autonomous Remediation & Verification Complete'
                    : 'Remediation Execution Result'}
                </h4>
              </div>

              <p className="mt-2 text-xs text-slate-200">
                {executionReport.summary}
              </p>

              {verificationReport && (
                <div className="mt-3 border border-white/10 bg-black/30 p-3">
                  <div className="flex items-center gap-2 text-xs font-bold text-cyan-200">
                    <ShieldCheck size={15} />
                    <span>
                      Post-Execution Verification ({verificationReport.verificationToolName}):{' '}
                      {String(verificationReport.status)}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-slate-300">
                    {verificationReport.summary}
                  </p>
                </div>
              )}

              {executionReport.proof && executionReport.proof.length > 0 && (
                <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-2">
                  {executionReport.proof.map((item, idx) => (
                    <div
                      key={idx}
                      className="border border-white/10 bg-black/35 p-3 text-xs"
                    >
                      <div className="flex items-center justify-between font-semibold text-white">
                        <span>{item.label}</span>
                        <span className="text-emerald-300">{item.status}</span>
                      </div>
                      {item.meaning && (
                        <p className="mt-1 text-slate-400">{item.meaning}</p>
                      )}
                      {(item.before || item.after) && (
                        <div className="mt-2 flex flex-wrap gap-2 font-mono text-[11px] text-slate-300">
                          {item.before && <span>Before: {item.before}</span>}
                          {item.after && <span>→ After: {item.after}</span>}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
