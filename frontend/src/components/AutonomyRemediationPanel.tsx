import { useState } from 'react';
import { motion } from 'motion/react';
import {
  AlertTriangle,
  CheckCircle2,
  Info,
  Loader2,
  Play,
  ShieldCheck,
  SlidersHorizontal,
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
  closeSelectedApp,
  getMemoryAppCandidates,
  getBackendErrorMessage,
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
      title: 'Done',
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

function shouldUseCloseSelectedAppFlow(diagnosedCategory: string) {
  const category = diagnosedCategory.toLowerCase();
  return category.includes('memory');
}

function formatMemory(memoryMb: number) {
  if (memoryMb >= 1024) {
    return `${(memoryMb / 1024).toFixed(1)} GB`;
  }

  return `${Math.round(memoryMb)} MB`;
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

          <p className="mt-1 text-xs font-semibold text-white">
            {item.after || item.status || item.before || 'Completed'}
          </p>

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
    <div className={`rounded-xl border p-4 text-sm ${style.className}`}>
      <div className="flex items-start gap-3">
        <Icon size={18} className="mt-0.5 shrink-0" />

        <div className="min-w-0">
          <p className="text-xs font-bold uppercase tracking-wider">
            {style.title}
          </p>

          <p className="mt-2 leading-relaxed">
            {summary}
          </p>

          <span className="mt-3 inline-flex rounded-full border border-current/30 px-2.5 py-1 text-[10px] font-bold uppercase">
            {getAttemptStateLabel(attempt?.state) ||
              getVerificationLabel(result.verification) ||
              'Completed'}
          </span>
        </div>
      </div>

      <ProofList execution={result.execution || attempt?.execution} />
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

  const closeSelectedAppFlow =
    shouldUseCloseSelectedAppFlow(diagnosedCategory);

  const primaryAction = getPrimaryAction(previewResult);

  const dynamicTitle = closeSelectedAppFlow
    ? 'Close selected memory-heavy apps'
    : primaryAction?.name || 'Review proposed remediation';

  const dynamicDescription = closeSelectedAppFlow
    ? 'Select which running applications RigMD should close to reduce live memory pressure.'
    : primaryAction?.description ||
      previewResult?.plan?.strategyReasoning ||
      'Review the proposed remediation parameters before execution.';

  const dynamicWarnings =
    previewResult?.safety?.warnings && previewResult.safety.warnings.length > 0
      ? previewResult.safety.warnings
      : ['Explicit user confirmation is required before executing system changes.'];

  const canProceed =
    Boolean(previewResult) &&
    userConsentProvided &&
    !requestActive &&
    (!closeSelectedAppFlow || selectedProcessNames.length > 0);

  const selectedMemoryApps =
    memoryApps.filter((app) => selectedProcessNames.includes(app.name));

  const selectedAppNames =
    selectedMemoryApps.map((app) => app.displayName || app.name);

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
    setMemoryApps([]);

    try {
      if (closeSelectedAppFlow) {
        const apps = await getMemoryAppCandidates();

        setMemoryApps(apps);
        setPreviewResult({
          plan: {
            sessionId,
            plannedActions: [
              {
                id: 'close_selected_app',
                name: 'Close selected app',
                description:
                  'Close only the memory-heavy app selected by the user.',
                category: 'Troubleshoot',
                riskLevel: 'User-confirmed',
                isReversible: false,
                requiresUserConfirmation: true,
              },
            ],
          },
          safety: {
            isApproved: true,
            requiresUserConfirmation: true,
            warnings: [
              'The selected app may close open windows, tabs, downloads, or unsaved work.',
            ],
          },
        });

        setReviewOpen(true);
        return;
      }

      const result = await runAutonomyPreview({
        sessionId,
        diagnosedCategory,
      });

      setPreviewResult(result);
      setReviewOpen(true);
    } catch (error) {
      setPreviewError(getBackendErrorMessage(error));
    } finally {
      setIsPreviewLoading(false);
    }
  };

  const runExecution = async () => {
    if (!canProceed || !sessionId || !diagnosedCategory) {
      return;
    }

    setIsExecuteLoading(true);
    setExecuteError(null);

    await startRemediationStream(() => {});

    try {
      const result = closeSelectedAppFlow
        ? await closeSelectedApp({
            processNames: selectedProcessNames,
            confirmed: true,
          })
        : await runAutonomyExecution({
            sessionId,
            diagnosedCategory,
            userConsentProvided: true,
          });

      setExecutionResult(result);
      setReviewOpen(false);
      setUserConsentProvided(false);
      onExecutionComplete?.(result);
    } catch (error) {
      setExecuteError(getBackendErrorMessage(error));
    } finally {
      setIsExecuteLoading(false);
      await stopRemediationStream();
    }
  };

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
              <h5 className="text-sm font-bold text-white">
                Autonomous Remediation
              </h5>

              <p className="mt-1 max-w-2xl text-xs leading-relaxed text-slate-400">
                Review proposed remediation actions and approve execution.
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

            {isPreviewLoading ? 'Preparing review...' : 'Review Action'}
          </motion.button>
        </div>

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
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4 py-6">
          <motion.div
            initial={{ opacity: 0, y: 12, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.98 }}
            className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5 shadow-2xl"
          >
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-xs font-bold uppercase tracking-wider text-cyan-300">
                  Review before changes
                </p>

                <h3 className="mt-2 text-xl font-bold text-white">
                  {dynamicTitle}
                </h3>

                <p className="mt-2 text-sm leading-relaxed text-slate-400">
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

            <div className="mt-5 grid grid-cols-1 gap-3">
              {previewResult?.plan?.strategyReasoning && (
                <div className="rounded-xl border border-cyan-400/20 bg-cyan-400/[0.05] p-4">
                  <h4 className="text-xs font-bold uppercase tracking-wider text-cyan-200">
                    Orchestrator Reasoning
                  </h4>

                  <p className="mt-2 text-sm text-slate-300">
                    {previewResult.plan.strategyReasoning}
                  </p>
                </div>
              )}

              <div className="rounded-xl border border-amber-400/20 bg-amber-400/[0.05] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-amber-200">
                  Safety &amp; Warnings
                </h4>

                <ul className="mt-3 space-y-2 text-sm text-slate-300">
                  {dynamicWarnings.map((item) => (
                    <li key={item}>- {item}</li>
                  ))}
                </ul>
              </div>
            </div>

            {closeSelectedAppFlow && (
              <div className="mt-4 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-white">
                  Choose apps to close
                </h4>

                <p className="mt-2 text-xs leading-relaxed text-slate-500">
                  Pick only apps you are ready to close. RigMD hides Windows,
                  RigMD, and background helper processes from this list.
                </p>

                {memoryApps.length === 0 ? (
                  <p className="mt-3 rounded-lg border border-amber-400/25 bg-amber-400/[0.06] p-3 text-xs leading-relaxed text-amber-100">
                    RigMD does not see a memory-heavy app that is safe to close
                    right now. Close apps manually if needed, then use Check if
                    Fixed.
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
                I understand what RigMD is about to do and want to continue.
                <span className="mt-1 block text-xs text-amber-100/70">
                  This box must be checked before the Proceed button becomes active.
                </span>
              </span>
            </label>

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
                  ? 'Running...'
                  : closeSelectedAppFlow && selectedProcessNames.length === 0
                    ? 'Choose at least one app'
                    : userConsentProvided
                    ? 'Proceed'
                    : 'Check the box to proceed'}
              </motion.button>
            </div>
          </motion.div>
        </div>
      )}
    </>
  );
}
