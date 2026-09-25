import { useState } from 'react';
import { motion } from 'motion/react';
import {
  Activity,
  AlertTriangle,
  Bot,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Cpu,
  Info,
  Loader2,
  Play,
  ShieldCheck,
  SlidersHorizontal,
  Terminal,
  Wrench,
  X,
  XCircle,
} from 'lucide-react';

import {
  buttonTap,
  cardFadeUp,
  cardTransition,
} from '../lib/motion';

import {
  type AutonomyExecution,
  type AutonomyResult,
  type MemoryAppCandidate,
  type ReActTraceStep,
  type ToolDryRunPreview,
  closeSelectedApp,
  getBackendErrorMessage,
  getMemoryAppCandidates,
  previewAgentTool,
  runAutonomyExecution,
  runAutonomyPreview,
  startRemediationStream,
  stopRemediationStream,
} from '../services/autonomyService';

interface AutonomyRemediationPanelProps {
  sessionId: string;
  diagnosedCategory: string;
  onExecutionComplete?: (result: AutonomyResult) => void;
}

const ATTEMPT_STATE_LABELS: Record<number, string> = {
  0: 'Planned',
  1: 'Safety rejected',
  2: 'Needs approval',
  3: 'Running',
  4: 'Failed',
  5: 'Checking result',
  6: 'Fixed',
  7: 'Not fixed',
  8: 'Needs help',
  9: 'Could not confirm',
  10: 'Undo pending',
  11: 'Undone',
  12: 'Undo failed',
  13: 'Trying another option',
  14: 'Changed approach',
  15: 'Completed',
};

const VERIFICATION_STATUS_LABELS: Record<number, string> = {
  0: 'Fixed',
  1: 'Not fixed',
  2: 'Needs help',
  3: 'Could not confirm',
};

const REMEDIATION_TOOL_OPTIONS = [
  { id: 'clear_temp_files', label: 'Clear Temporary Files (Tier 1)' },
  { id: 'flush_dns_cache', label: 'Flush Windows DNS Resolver Cache (Tier 1)' },
  { id: 'restart_windows_explorer', label: 'Restart Windows Explorer Shell (Tier 1)' },
  { id: 'clear_browser_cache', label: 'Clear Web Browser Disk Caches (Tier 2)' },
  { id: 'clear_windows_update_cache', label: 'Clear Windows Update Download Cache (Tier 2)' },
  { id: 'run_system_file_checker', label: 'Run Windows System File Checker / SFC (Tier 2)' },
  { id: 'close_selected_app', label: 'Terminate Selected High-Memory Apps (Tier 2)' },
];

function getLatestAttempt(result?: AutonomyResult | null) {
  return result?.attempts?.[result.attempts.length - 1] ?? null;
}

function getAttemptStateLabel(state?: string | number) {
  if (typeof state === 'number') {
    return ATTEMPT_STATE_LABELS[state] ?? String(state);
  }

  return state ?? '';
}

function getVerificationLabel(verification?: string | number) {
  if (typeof verification === 'number') {
    return VERIFICATION_STATUS_LABELS[verification] ?? String(verification);
  }

  return verification ?? '';
}

function getState(result?: AutonomyResult | null) {
  if (!result) {
    return 'info';
  }

  const attempt = getLatestAttempt(result);
  const attemptState = getAttemptStateLabel(attempt?.state);
  const verificationState = getVerificationLabel(result.verification);
  const state = (attemptState || verificationState).toLowerCase();
  const safety = result.safety;

  if (
    state.includes('approval') ||
    state.includes('consent') ||
    (safety?.requiresUserConfirmation === true && safety?.isApproved === false)
  ) {
    return 'consent';
  }

  if (state.includes('reject') || safety?.isApproved === false) {
    return 'rejected';
  }

  if (state.includes('not fixed') || state.includes('unresolved')) {
    return 'unresolved';
  }

  if (
    state.includes('failed') ||
    state.includes('needs help') ||
    result.execution?.success === false
  ) {
    return 'failed';
  }

  if (
    state.includes('fixed') ||
    state.includes('completed') ||
    result.execution?.success === true
  ) {
    return 'success';
  }

  return 'info';
}

function getStatusStyle(state: string) {
  if (state === 'success') {
    return {
      icon: CheckCircle2,
      title: 'Verified Complete',
      className: 'border-emerald-400/30 bg-emerald-400/10 text-emerald-200',
    };
  }

  if (state === 'failed' || state === 'rejected') {
    return {
      icon: XCircle,
      title: 'Could not complete',
      className: 'border-red-400/30 bg-red-400/10 text-red-200',
    };
  }

  if (state === 'consent' || state === 'unresolved') {
    return {
      icon: AlertTriangle,
      title: 'Needs your attention',
      className: 'border-amber-400/30 bg-amber-400/10 text-amber-200',
    };
  }

  return {
    icon: Info,
    title: 'Ready',
    className: 'border-cyan-400/30 bg-cyan-400/10 text-cyan-200',
  };
}

function getPrimaryAction(result?: AutonomyResult | null) {
  return result?.plan?.plannedActions?.[0] ?? null;
}

function formatMemory(memoryMb: number) {
  if (memoryMb >= 1024) {
    return `${(memoryMb / 1024).toFixed(1)} GB`;
  }

  return `${Math.round(memoryMb)} MB`;
}

function formatBytes(bytes?: number) {
  if (!bytes || bytes <= 0) {
    return '0 B';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kb = bytes / 1024;
  if (kb < 1024) {
    return `${kb.toFixed(1)} KB`;
  }
  const mb = kb / 1024;
  if (mb < 1024) {
    return `${mb.toFixed(1)} MB`;
  }
  return `${(mb / 1024).toFixed(2)} GB`;
}

function getStepBadgeStyle(stepType: string) {
  const normalized = stepType.toLowerCase();
  if (normalized.includes('thought')) {
    return 'border-purple-400/40 bg-purple-400/15 text-purple-200';
  }
  if (normalized.includes('toolcall')) {
    return 'border-cyan-400/40 bg-cyan-400/15 text-cyan-200';
  }
  if (normalized.includes('observation')) {
    return 'border-emerald-400/40 bg-emerald-400/15 text-emerald-200';
  }
  if (normalized.includes('dryrun')) {
    return 'border-amber-400/40 bg-amber-400/15 text-amber-200';
  }
  if (normalized.includes('execution')) {
    return 'border-blue-400/40 bg-blue-400/15 text-blue-200';
  }
  if (normalized.includes('verification')) {
    return 'border-teal-400/40 bg-teal-400/15 text-teal-200';
  }
  return 'border-slate-400/40 bg-slate-400/15 text-slate-200';
}

function ReActTimeline({ steps }: { steps: ReActTraceStep[] }) {
  const [expandedSteps, setExpandedSteps] = useState<Record<number, boolean>>({});

  if (!steps || steps.length === 0) {
    return null;
  }

  const toggleExpand = (idx: number) => {
    setExpandedSteps((prev) => ({ ...prev, [idx]: !prev[idx] }));
  };

  return (
    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[#0b1118] p-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Bot size={15} className="text-cyan-300" />
          <h4 className="text-xs font-bold uppercase tracking-wider text-cyan-200">
            Live ReAct Reasoning &amp; Tool Trace ({steps.length} steps)
          </h4>
        </div>
      </div>

      <div className="mt-3 space-y-2.5">
        {steps.map((step, index) => {
          const key = step.stepIndex || index + 1;
          const isExpanded = Boolean(expandedSteps[key]);
          const hasRawJson =
            Boolean(step.observationJson) &&
            step.observationJson !== '{}' &&
            step.observationJson !== 'null';

          return (
            <div
              key={`${key}-${step.stepType}-${step.title}`}
              className="rounded-lg border border-white/10 bg-black/25 p-3 text-xs"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span
                    className={`rounded-md border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider ${getStepBadgeStyle(
                      step.stepType,
                    )}`}
                  >
                    {step.stepType}
                  </span>

                  <span className="font-semibold text-white">
                    {step.title}
                  </span>

                  {step.toolName && (
                    <code className="rounded bg-white/5 px-1.5 py-0.5 font-mono text-[11px] text-cyan-300">
                      {step.toolName}()
                    </code>
                  )}
                </div>

                {typeof step.durationMs === 'number' && step.durationMs > 0 && (
                  <span className="text-[10px] font-mono text-slate-400">
                    {step.durationMs} ms
                  </span>
                )}
              </div>

              <p className="mt-1.5 leading-relaxed text-slate-300">
                {step.content}
              </p>

              {hasRawJson && (
                <div className="mt-2">
                  <button
                    type="button"
                    onClick={() => toggleExpand(key)}
                    className="inline-flex items-center gap-1 text-[11px] font-semibold text-cyan-300 hover:text-cyan-200"
                  >
                    {isExpanded ? (
                      <>
                        <ChevronUp size={12} /> Hide Raw Telemetry JSON
                      </>
                    ) : (
                      <>
                        <ChevronDown size={12} /> View Raw Telemetry JSON
                      </>
                    )}
                  </button>

                  {isExpanded && (
                    <pre className="mt-2 max-h-44 overflow-auto rounded border border-white/10 bg-black/50 p-2.5 font-mono text-[10px] leading-relaxed text-emerald-200">
                      {(() => {
                        try {
                          return JSON.stringify(
                            JSON.parse(step.observationJson!),
                            null,
                            2,
                          );
                        } catch {
                          return step.observationJson;
                        }
                      })()}
                    </pre>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function ProofList({ execution }: { execution?: AutonomyExecution }) {
  if (!execution?.proof?.length) {
    return null;
  }

  return (
    <div className="mt-4 grid grid-cols-1 gap-2 md:grid-cols-2">
      {execution.proof.map((item, index) => (
        <div
          key={`${item.label}-${index}`}
          className="rounded-lg border border-current/15 bg-black/10 p-3"
        >
          <p className="text-[10px] font-bold uppercase tracking-wider opacity-70">
            {item.label || 'Checked'}
          </p>

          {item.before && item.after ? (
            <p className="mt-1 text-xs font-semibold text-white">
              {item.before} &rarr; {item.after}
            </p>
          ) : (
            <p className="mt-1 text-xs font-semibold text-white">
              {item.after || item.status || item.before || 'Completed'}
            </p>
          )}

          {item.meaning && (
            <p className="mt-1 text-[11px] leading-relaxed opacity-80">
              {item.meaning}
            </p>
          )}
        </div>
      ))}
    </div>
  );
}

function ResultCard({ result }: { result: AutonomyResult }) {
  const state = getState(result);
  const style = getStatusStyle(state);
  const Icon = style.icon;
  const attempt = getLatestAttempt(result);
  const summary =
    attempt?.notes ||
    result.execution?.summary ||
    result.safety?.rejectionReason ||
    'RigMD finished checking this action.';

  return (
    <div className="space-y-3">
      <div className={`rounded-xl border p-4 text-sm ${style.className}`}>
        <div className="flex items-start gap-3">
          <Icon size={18} className="mt-0.5 shrink-0" />

          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-xs font-bold uppercase tracking-wider">
                {style.title}
              </p>

              {result.engineMode && (
                <span className="rounded-full border border-current/30 px-2 py-0.5 font-mono text-[10px]">
                  {result.engineMode}
                </span>
              )}
            </div>

            <p className="mt-2 leading-relaxed">
              {summary}
            </p>

            {result.verificationReport?.summary && (
              <div className="mt-3 rounded-lg border border-current/20 bg-black/20 p-2.5 text-xs">
                <p className="font-bold uppercase tracking-wider opacity-80">
                  Post-Action Telemetry Verification ({result.verificationReport.verificationToolName})
                </p>
                <p className="mt-1 opacity-90">
                  {result.verificationReport.summary}
                </p>
              </div>
            )}

            <span className="mt-3 inline-flex rounded-full border border-current/30 px-2.5 py-1 text-[10px] font-bold uppercase">
              {getAttemptStateLabel(attempt?.state) ||
                getVerificationLabel(result.verification) ||
                'Completed'}
            </span>
          </div>
        </div>

        <ProofList execution={result.execution || attempt?.execution} />
      </div>

      {result.reasoningSteps && result.reasoningSteps.length > 0 && (
        <ReActTimeline steps={result.reasoningSteps} />
      )}
    </div>
  );
}

export default function AutonomyRemediationPanel({
  sessionId,
  diagnosedCategory,
  onExecutionComplete,
}: AutonomyRemediationPanelProps) {
  const [previewResult, setPreviewResult] =
    useState<AutonomyResult | null>(null);

  const [executionResult, setExecutionResult] =
    useState<AutonomyResult | null>(null);

  const [liveSteps, setLiveSteps] =
    useState<ReActTraceStep[]>([]);

  const [liveProgressLine, setLiveProgressLine] =
    useState<string | null>(null);

  const [activeToolId, setActiveToolId] =
    useState<string>('clear_temp_files');

  const [customDryRun, setCustomDryRun] =
    useState<ToolDryRunPreview | null>(null);

  const [previewError, setPreviewError] =
    useState<string | null>(null);

  const [executeError, setExecuteError] =
    useState<string | null>(null);

  const [isPreviewLoading, setIsPreviewLoading] =
    useState(false);

  const [isExecuteLoading, setIsExecuteLoading] =
    useState(false);

  const [reviewOpen, setReviewOpen] =
    useState(false);

  const [userConsentProvided, setUserConsentProvided] =
    useState(false);

  const [memoryApps, setMemoryApps] =
    useState<MemoryAppCandidate[]>([]);

  const [selectedProcessNames, setSelectedProcessNames] =
    useState<string[]>([]);

  const requestActive = isPreviewLoading || isExecuteLoading;

  const isCloseSelectedAppMode = activeToolId === 'close_selected_app';

  const primaryAction = getPrimaryAction(previewResult);

  const effectiveDryRun: ToolDryRunPreview | undefined =
    customDryRun ??
    previewResult?.dryRunPreview ??
    previewResult?.proposedTool?.dryRunPreview;

  const dynamicTitle = isCloseSelectedAppMode
    ? 'Close selected memory-heavy apps'
    : effectiveDryRun?.displayName ||
      previewResult?.proposedTool?.displayName ||
      primaryAction?.name ||
      'Review proposed remediation';

  const dynamicDescription = isCloseSelectedAppMode
    ? 'Select which running applications RigMD should close to reduce live memory pressure.'
    : effectiveDryRun?.whatWillHappen ||
      primaryAction?.description ||
      previewResult?.plan?.strategyReasoning ||
      'Review the proposed remediation parameters before execution.';

  const dynamicWarnings =
    effectiveDryRun?.warnings && effectiveDryRun.warnings.length > 0
      ? effectiveDryRun.warnings
      : previewResult?.safety?.warnings && previewResult.safety.warnings.length > 0
        ? previewResult.safety.warnings
        : ['Explicit user confirmation is required before executing system changes.'];

  const canProceed =
    Boolean(previewResult) &&
    userConsentProvided &&
    !requestActive &&
    (effectiveDryRun?.canExecute ?? true) &&
    (!isCloseSelectedAppMode || selectedProcessNames.length > 0);

  const selectedMemoryApps =
    memoryApps.filter((app) => selectedProcessNames.includes(app.name));

  const selectedAppNames =
    selectedMemoryApps.map((app) => app.displayName || app.name);

  const handleToolSelectionChange = async (nextToolId: string) => {
    setActiveToolId(nextToolId);
    setUserConsentProvided(false);

    if (nextToolId === 'close_selected_app') {
      setCustomDryRun(null);
      if (memoryApps.length === 0) {
        try {
          const apps = await getMemoryAppCandidates();
          setMemoryApps(apps);
        } catch {
          // Ignore error if process list fails
        }
      }
      return;
    }

    try {
      const preview = await previewAgentTool(nextToolId, {});
      setCustomDryRun(preview);
    } catch {
      setCustomDryRun(null);
    }
  };

  const openReview = async () => {
    if (requestActive || !sessionId || !diagnosedCategory) {
      return;
    }

    setIsPreviewLoading(true);
    setPreviewError(null);
    setExecuteError(null);
    setExecutionResult(null);
    setUserConsentProvided(false);
    setSelectedProcessNames([]);
    setCustomDryRun(null);
    setLiveSteps([]);
    setLiveProgressLine('Starting ReAct diagnostic tool investigation...');

    await startRemediationStream(
      (msg) => setLiveProgressLine(msg),
      (step) =>
        setLiveSteps((prev) => {
          if (prev.some((s) => s.stepIndex === step.stepIndex && s.title === step.title)) {
            return prev;
          }
          return [...prev, step];
        }),
    );

    try {
      const [result, apps] = await Promise.all([
        runAutonomyPreview({
          sessionId,
          diagnosedCategory,
        }),
        getMemoryAppCandidates().catch(() => [] as MemoryAppCandidate[]),
      ]);

      setMemoryApps(apps);
      setPreviewResult(result);

      const recommendedTool =
        result.proposedTool?.toolName ||
        result.plan?.plannedActions?.[0]?.id ||
        (diagnosedCategory.toLowerCase().includes('memory') && apps.length > 0
          ? 'close_selected_app'
          : 'clear_temp_files');

      setActiveToolId(recommendedTool);
      setReviewOpen(true);
    } catch (error) {
      setPreviewError(getBackendErrorMessage(error));
    } finally {
      setIsPreviewLoading(false);
      setLiveProgressLine(null);
      await stopRemediationStream();
    }
  };

  const runExecution = async () => {
    if (!canProceed || !sessionId || !diagnosedCategory) {
      return;
    }

    setIsExecuteLoading(true);
    setExecuteError(null);
    setLiveSteps([]);

    await startRemediationStream(
      (msg) => setLiveProgressLine(msg),
      (step) =>
        setLiveSteps((prev) => [...prev, step]),
    );

    try {
      const result = isCloseSelectedAppMode
        ? await closeSelectedApp({
            processNames: selectedProcessNames,
            confirmed: true,
          })
        : await runAutonomyExecution({
            sessionId,
            diagnosedCategory,
            userConsentProvided: true,
            toolName: activeToolId,
            toolArgumentsJson:
              activeToolId === previewResult?.proposedTool?.toolName
                ? previewResult?.proposedTool?.argumentsJson
                : '{}',
          });

      setExecutionResult(result);
      setReviewOpen(false);
      setUserConsentProvided(false);
      onExecutionComplete?.(result);
    } catch (error) {
      setExecuteError(getBackendErrorMessage(error));
    } finally {
      setIsExecuteLoading(false);
      setLiveProgressLine(null);
      await stopRemediationStream();
    }
  };

  const displayedSteps =
    previewResult?.reasoningSteps && previewResult.reasoningSteps.length > 0
      ? previewResult.reasoningSteps
      : liveSteps;

  return (
    <>
      <motion.section
        variants={cardFadeUp}
        initial="hidden"
        animate="visible"
        transition={cardTransition}
        className="rounded-xl border border-[var(--rigmd-border)] bg-[#101821] p-4"
      >
        <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
          <div className="flex items-start gap-3">
            <ShieldCheck
              size={18}
              className="mt-0.5 shrink-0 text-cyan-300"
            />

            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h5 className="text-sm font-bold text-white">
                  Autonomous ReAct Diagnostic &amp; Remediation Agent
                </h5>
                {previewResult?.engineMode && (
                  <span className="rounded-full border border-cyan-400/30 bg-cyan-400/10 px-2 py-0.5 font-mono text-[10px] text-cyan-200">
                    {previewResult.engineMode}
                  </span>
                )}
              </div>

              <p className="mt-1 max-w-2xl text-xs leading-relaxed text-slate-400">
                Runs live read-only WMI &amp; OS inspection tools (Thought &rarr; Tool Call &rarr; Observation), computes a real dry-run impact preview, and verifies telemetry deltas after user-approved execution.
              </p>
            </div>
          </div>

          <motion.button
            type="button"
            onClick={openReview}
            disabled={requestActive || !sessionId || !diagnosedCategory}
            whileTap={buttonTap}
            className="inline-flex items-center justify-center gap-2 rounded-lg border border-cyan-400/35 bg-cyan-400/10 px-4 py-2.5 text-xs font-bold text-cyan-200 transition hover:bg-cyan-400/15 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isPreviewLoading ? (
              <Loader2 size={14} className="animate-spin" />
            ) : (
              <SlidersHorizontal size={14} />
            )}

            {isPreviewLoading ? 'Running ReAct Tools...' : 'Run Agent & Review Action'}
          </motion.button>
        </div>

        {isPreviewLoading && (
          <div className="mt-4 space-y-2 rounded-lg border border-cyan-400/25 bg-cyan-400/5 p-3">
            <div className="flex items-center gap-2 text-xs text-cyan-200">
              <Terminal size={14} className="animate-pulse" />
              <span className="font-mono">
                {liveProgressLine || 'Executing Tier 0 diagnostic tools...'}
              </span>
            </div>
            {liveSteps.length > 0 && <ReActTimeline steps={liveSteps} />}
          </div>
        )}

        {previewError && (
          <p className="mt-4 rounded-lg border border-red-400/25 bg-red-400/5 p-3 text-xs text-red-200">
            {previewError}
          </p>
        )}

        {executeError && (
          <p className="mt-4 rounded-lg border border-red-400/25 bg-red-400/5 p-3 text-xs text-red-200">
            {executeError}
          </p>
        )}

        {executionResult && (
          <div className="mt-4">
            <ResultCard result={executionResult} />
          </div>
        )}
      </motion.section>

      {reviewOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 px-4 py-6">
          <motion.div
            initial={{ opacity: 0, y: 12, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.98 }}
            className="max-h-[92vh] w-full max-w-3xl overflow-y-auto rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5 shadow-2xl"
          >
            <div className="flex items-start justify-between gap-4">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-xs font-bold uppercase tracking-wider text-cyan-300">
                    ReAct Agent Investigation &amp; Dry-Run Preview
                  </p>
                  {previewResult?.engineMode && (
                    <span className="rounded-full border border-cyan-400/30 bg-cyan-400/10 px-2 py-0.5 font-mono text-[10px] text-cyan-200">
                      {previewResult.engineMode}
                    </span>
                  )}
                </div>

                <h3 className="mt-2 text-xl font-bold text-white">
                  {dynamicTitle}
                </h3>

                <p className="mt-2 text-sm leading-relaxed text-slate-300">
                  {dynamicDescription}
                </p>
              </div>

              <button
                type="button"
                onClick={() => setReviewOpen(false)}
                className="rounded-lg border border-[var(--rigmd-border)] p-2 text-slate-400 hover:text-white"
              >
                <X size={16} />
              </button>
            </div>

            <div className="mt-5 space-y-4">
              {/* 1. Multi-Turn ReAct Trace Timeline */}
              {displayedSteps.length > 0 && (
                <ReActTimeline steps={displayedSteps} />
              )}

              {/* 2. Root Cause Synthesis & Live Evidence Citations */}
              {(previewResult?.rootCauseAnalysis ||
                previewResult?.proposedTool?.evidenceCitations?.length) && (
                <div className="rounded-xl border border-cyan-400/20 bg-cyan-400/[0.05] p-4">
                  <div className="flex items-center gap-2">
                    <Cpu size={15} className="text-cyan-300" />
                    <h4 className="text-xs font-bold uppercase tracking-wider text-cyan-200">
                      Synthesized Root Cause &amp; Live Telemetry Citations
                    </h4>
                  </div>

                  {previewResult?.rootCauseAnalysis && (
                    <p className="mt-2 text-sm leading-relaxed text-slate-200">
                      {previewResult.rootCauseAnalysis}
                    </p>
                  )}

                  {previewResult?.proposedTool?.evidenceCitations &&
                    previewResult.proposedTool.evidenceCitations.length > 0 && (
                      <ul className="mt-3 space-y-1.5 border-t border-cyan-400/15 pt-2.5 font-mono text-xs text-cyan-100/90">
                        {previewResult.proposedTool.evidenceCitations.map(
                          (citation, i) => (
                            <li key={i}>&bull; {citation}</li>
                          ),
                        )}
                      </ul>
                    )}
                </div>
              )}

              {/* 3. Tool Selection & Live Dry-Run Impact Metrics */}
              <div className="rounded-xl border border-[var(--rigmd-border)] bg-[#0b1118] p-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="flex items-center gap-2">
                    <Wrench size={15} className="text-emerald-300" />
                    <h4 className="text-xs font-bold uppercase tracking-wider text-emerald-200">
                      Selected OS Remediation Tool &amp; Pre-Flight Dry Run
                    </h4>
                  </div>

                  <select
                    value={activeToolId}
                    onChange={(e) => handleToolSelectionChange(e.target.value)}
                    disabled={requestActive}
                    className="rounded-lg border border-white/15 bg-[#101821] px-3 py-1.5 text-xs font-semibold text-white focus:border-cyan-400 focus:outline-none"
                  >
                    {REMEDIATION_TOOL_OPTIONS.map((opt) => (
                      <option key={opt.id} value={opt.id}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                </div>

                {effectiveDryRun && !isCloseSelectedAppMode && (
                  <div className="mt-3 grid grid-cols-1 gap-2.5 sm:grid-cols-3">
                    <div className="rounded-lg border border-white/10 bg-black/30 p-3">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        Items Affected
                      </p>
                      <p className="mt-1 text-base font-bold text-white">
                        {effectiveDryRun.affectedItemsCount.toLocaleString()}
                      </p>
                    </div>

                    <div className="rounded-lg border border-white/10 bg-black/30 p-3">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        Estimated Space / RAM Impact
                      </p>
                      <p className="mt-1 text-base font-bold text-cyan-300">
                        {formatBytes(effectiveDryRun.estimatedBytesAffected)}
                      </p>
                    </div>

                    <div className="rounded-lg border border-white/10 bg-black/30 p-3">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        Privilege Check
                      </p>
                      <p className="mt-1 text-xs font-bold text-white">
                        {effectiveDryRun.requiresAdmin
                          ? effectiveDryRun.isRunningAsAdmin
                            ? 'Admin Required (Elevated OK)'
                            : 'Requires Admin Elevation'
                          : 'Standard User Safe'}
                      </p>
                    </div>
                  </div>
                )}

                {effectiveDryRun?.affectedTargets &&
                  effectiveDryRun.affectedTargets.length > 0 &&
                  !isCloseSelectedAppMode && (
                    <div className="mt-3 rounded-lg border border-white/10 bg-black/20 p-3">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        Measured Target Paths / Services
                      </p>
                      <ul className="mt-1.5 space-y-1 font-mono text-xs text-slate-300">
                        {effectiveDryRun.affectedTargets.map((t, idx) => (
                          <li key={idx}>&bull; {t}</li>
                        ))}
                      </ul>
                    </div>
                  )}
              </div>

              {/* 4. Safety & Warnings */}
              <div className="rounded-xl border border-amber-400/20 bg-amber-400/[0.05] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-amber-200">
                  Safety &amp; Pre-Execution Warnings
                </h4>

                <ul className="mt-2.5 space-y-1.5 text-sm text-slate-300">
                  {dynamicWarnings.map((item) => (
                    <li key={item}>- {item}</li>
                  ))}
                </ul>
              </div>
            </div>

            {isCloseSelectedAppMode && (
              <div className="mt-4 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-white">
                  Choose apps to close
                </h4>

                <p className="mt-2 text-xs leading-relaxed text-slate-500">
                  Pick only apps you are ready to close. RigMD strictly blocks protected Windows,
                  Security, and RigMD system processes.
                </p>

                {memoryApps.length === 0 ? (
                  <p className="mt-3 rounded-lg border border-amber-400/25 bg-amber-400/[0.06] p-3 text-xs leading-relaxed text-amber-100">
                    RigMD does not see a memory-heavy user app (&gt;100 MB) that is eligible to close
                    right now. You can select another remediation tool above (such as Clear Temporary Files).
                  </p>
                ) : (
                  <div className="mt-3 space-y-3">
                    {memoryApps.map((app) => {
                      const selected =
                        selectedProcessNames.includes(app.name);

                      return (
                        <label
                          key={app.id}
                          className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm transition ${
                            selected
                              ? 'border-cyan-400/60 bg-cyan-400/[0.08] text-cyan-100'
                              : 'border-[var(--rigmd-border)] bg-black/10 text-slate-300 hover:border-cyan-400/30'
                          }`}
                        >
                          <input
                            type="checkbox"
                            name="memory-app"
                            checked={selected}
                            onChange={() =>
                              setSelectedProcessNames((prev) =>
                                selected
                                  ? prev.filter((name) => name !== app.name)
                                  : [...prev, app.name],
                              )
                            }
                            className="mt-1"
                          />

                          <span className="min-w-0 flex-1">
                            <span className="flex flex-wrap items-center gap-2">
                              <span className="font-bold text-white">
                                {app.displayName || app.name}
                              </span>

                              <span className="rounded-full border border-slate-400/25 bg-slate-400/10 px-2 py-0.5 text-[10px] font-bold uppercase text-slate-300">
                                {app.appKind || 'App'}
                              </span>

                              <span className="rounded-full border border-cyan-400/25 bg-cyan-400/10 px-2 py-0.5 text-[10px] font-bold uppercase text-cyan-200">
                                {formatMemory(app.memoryMb)}
                              </span>
                            </span>

                            <span className="mt-1 block text-xs leading-relaxed text-slate-500">
                              {app.processCount} related process
                              {app.processCount === 1 ? '' : 'es'} will be
                              closed if Windows allows it.
                            </span>

                            {app.detail && (
                              <span className="mt-2 block text-xs leading-relaxed text-slate-400">
                                {app.detail}
                              </span>
                            )}

                            {selected && app.closeWarning && (
                              <span className="mt-2 block rounded-md border border-amber-400/20 bg-amber-400/[0.05] p-2 text-xs leading-relaxed text-amber-100">
                                {app.closeWarning}
                              </span>
                            )}
                          </span>
                        </label>
                      );
                    })}
                  </div>
                )}

                {selectedMemoryApps.length > 0 && (
                  <div className="mt-3 rounded-lg border border-red-400/25 bg-red-400/[0.06] p-3 text-xs leading-relaxed text-red-100">
                    You selected {selectedAppNames.join(', ')}. RigMD will try
                    to close only these apps, then recheck whether memory
                    pressure improved.
                  </div>
                )}
              </div>
            )}

            <label className="mt-4 flex cursor-pointer items-start gap-3 rounded-xl border border-amber-400/25 bg-amber-400/[0.06] p-4 text-sm text-amber-100">
              <input
                type="checkbox"
                checked={userConsentProvided}
                onChange={(event) =>
                  setUserConsentProvided(event.target.checked)
                }
                className="mt-1"
              />

              <span>
                I have reviewed the live ReAct observations and dry-run impact preview and authorize RigMD to execute this tool.
                <span className="mt-1 block text-xs text-amber-100/70">
                  Explicit user approval is required before any Tier 1 or Tier 2 OS tool is executed.
                </span>
              </span>
            </label>

            {isExecuteLoading && liveProgressLine && (
              <div className="mt-3 flex items-center gap-2 rounded-lg border border-cyan-400/30 bg-cyan-400/10 p-3 font-mono text-xs text-cyan-200">
                <Activity size={14} className="animate-spin" />
                <span>{liveProgressLine}</span>
              </div>
            )}

            <div className="mt-5 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
              <button
                type="button"
                onClick={() => setReviewOpen(false)}
                className="rounded-lg border border-[var(--rigmd-border)] px-4 py-2.5 text-sm font-bold text-slate-300 hover:text-white"
              >
                Cancel
              </button>

              <motion.button
                type="button"
                onClick={runExecution}
                disabled={!canProceed}
                whileTap={buttonTap}
                className="inline-flex items-center justify-center gap-2 rounded-lg bg-cyan-400 px-4 py-2.5 text-sm font-bold text-[#041014] transition hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {isExecuteLoading ? (
                  <Loader2 size={16} className="animate-spin" />
                ) : (
                  <Play size={16} />
                )}

                {isExecuteLoading
                  ? 'Executing & Verifying...'
                  : effectiveDryRun && !effectiveDryRun.canExecute
                    ? 'Cannot Execute (See Warnings)'
                    : isCloseSelectedAppMode && selectedProcessNames.length === 0
                      ? 'Choose at least one app'
                      : userConsentProvided
                        ? 'Approve & Execute Tool'
                        : 'Check the box to proceed'}
              </motion.button>
            </div>
          </motion.div>
        </div>
      )}
    </>
  );
}
