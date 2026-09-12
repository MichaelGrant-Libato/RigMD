import { useMemo, useState } from 'react';
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
  type AgentRemediationActionId,
  getBackendErrorMessage,
  runAgentRemediation,
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

const ACTION_COPY: Record<
  string,
  {
    title: string;
    plainDescription: string;
    willDo: string[];
    mayChange: string[];
    willNotTouch: string[];
    options?: Array<{
      id: string;
      label: string;
      description: string;
      defaultChecked: boolean;
    }>;
  }
> = {
  clear_user_temp_files: {
    title: 'Clean temporary files',
    plainDescription:
      'RigMD will remove safe temporary files that Windows and apps no longer need.',
    willDo: [
      'Look for temporary files created by Windows and apps.',
      'Remove files that are safe to delete.',
      'Check the result after the cleanup finishes.',
    ],
    mayChange: [
      'Some storage space may be freed.',
      'Apps may recreate temporary files later when needed.',
    ],
    willNotTouch: [
      'Your documents, pictures, downloads, and desktop files.',
      'Your saved passwords or personal accounts.',
      'Installed apps.',
    ],
    options: [
      {
        id: 'tempFiles',
        label: 'Temporary files',
        description: 'Recommended. Removes files Windows no longer needs.',
        defaultChecked: true,
      },
    ],
  },

  flush_dns: {
    title: 'Refresh internet lookup cache',
    plainDescription:
      'RigMD will reset the Windows DNS cache. This can help when websites or network connections act strangely.',
    willDo: [
      'Clear saved website lookup records from Windows.',
      'Let Windows rebuild fresh lookup records automatically.',
      'Check the result after the reset finishes.',
    ],
    mayChange: [
      'The next website visit may take a moment longer while Windows rebuilds the cache.',
    ],
    willNotTouch: [
      'Your files.',
      'Your browser history.',
      'Your Wi-Fi password or network settings.',
    ],
  },

  clear_browser_cache: {
    title: 'Clear browser cache',
    plainDescription:
      'RigMD may clear cached browser files that can be rebuilt later.',
    willDo: [
      'Remove saved temporary website files.',
      'Keep the action limited to cache cleanup.',
    ],
    mayChange: [
      'Some websites may load a little slower the first time after cleanup.',
    ],
    willNotTouch: [
      'Bookmarks.',
      'Saved passwords.',
      'Personal files.',
    ],
  },

  run_disk_cleanup: {
    title: 'Run Windows cleanup',
    plainDescription:
      'RigMD may start a Windows cleanup task to remove system-safe temporary files.',
    willDo: [
      'Use Windows cleanup tools.',
      'Remove safe temporary files selected by Windows.',
    ],
    mayChange: [
      'Storage space may improve.',
    ],
    willNotTouch: [
      'Personal files.',
      'Installed applications.',
    ],
  },
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

function getReadableAction(actionId?: string, actionName?: string) {
  if (actionId && ACTION_COPY[actionId]) {
    return ACTION_COPY[actionId];
  }

  return {
    title: actionName || 'Safe action',
    plainDescription:
      'RigMD found a safe action that may help with this diagnosis.',
    willDo: [
      'Run only the approved action for this diagnosis.',
      'Check whether the action completed successfully.',
    ],
    mayChange: [
      'Only the selected safe area of the PC may be changed.',
    ],
    willNotTouch: [
      'Personal files unless the action clearly says so.',
      'Passwords or accounts.',
      'Unrelated Windows settings.',
    ],
  };
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

  const [selectedOptions, setSelectedOptions] =
    useState<Record<string, boolean>>({});

  const requestActive = isPreviewLoading || isExecuteLoading;

  const primaryAction = getPrimaryAction(previewResult);

  const actionCopy = getReadableAction(
    primaryAction?.id,
    primaryAction?.name,
  );

  const plannedActionIds =
    previewResult?.plan?.plannedActions
      ?.map((action) => action.id)
      .filter((id): id is string => typeof id === 'string') ?? [];

  const agentActionId: AgentRemediationActionId | null =
    plannedActionIds.length === 1 &&
    (plannedActionIds[0] === 'clear_user_temp_files' ||
      plannedActionIds[0] === 'flush_dns')
      ? plannedActionIds[0]
      : null;

  const shouldExecuteThroughAgent = agentActionId !== null;

  const canProceed =
    Boolean(previewResult) &&
    Boolean(primaryAction) &&
    userConsentProvided &&
    !requestActive;

  const selectedOptionLabels = useMemo(() => {
    return (actionCopy.options ?? [])
      .filter((option) => selectedOptions[option.id])
      .map((option) => option.label);
  }, [actionCopy.options, selectedOptions]);

  const openReview = async () => {
    if (requestActive || !sessionId || !diagnosedCategory) {
      return;
    }

    setIsPreviewLoading(true);
    setPreviewError(null);
    setExecuteError(null);
    setExecutionResult(null);
    setUserConsentProvided(false);

    try {
      const result = await runAutonomyPreview({
        sessionId,
        diagnosedCategory,
      });

      setPreviewResult(result);

      const action = getPrimaryAction(result);
      const readable = getReadableAction(action?.id, action?.name);

      const defaultOptions: Record<string, boolean> = {};
      readable.options?.forEach((option) => {
        defaultOptions[option.id] = option.defaultChecked;
      });

      setSelectedOptions(defaultOptions);
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

    await startRemediationStream(() => {
      // Keep technical progress hidden from normal users.
    });

    try {
      const result =
        shouldExecuteThroughAgent && agentActionId
          ? await runAgentRemediation(agentActionId)
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
                Safe action
              </h5>

              <p className="mt-1 max-w-2xl text-xs leading-relaxed text-slate-400">
                RigMD can help with this result, but it will show you what may
                change before anything runs.
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

            {isPreviewLoading ? 'Preparing review...' : 'Review Safe Action'}
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
                  {actionCopy.title}
                </h3>

                <p className="mt-2 text-sm leading-relaxed text-slate-400">
                  {actionCopy.plainDescription}
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
              <div className="rounded-xl border border-cyan-400/20 bg-cyan-400/[0.05] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-cyan-200">
                  What RigMD will do
                </h4>

                <ul className="mt-3 space-y-2 text-sm text-slate-300">
                  {actionCopy.willDo.map((item) => (
                    <li key={item}>- {item}</li>
                  ))}
                </ul>
              </div>

              <div className="rounded-xl border border-amber-400/20 bg-amber-400/[0.05] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-amber-200">
                  What may change
                </h4>

                <ul className="mt-3 space-y-2 text-sm text-slate-300">
                  {actionCopy.mayChange.map((item) => (
                    <li key={item}>- {item}</li>
                  ))}
                </ul>
              </div>

              <div className="rounded-xl border border-emerald-400/20 bg-emerald-400/[0.05] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-emerald-200">
                  What RigMD will not touch
                </h4>

                <ul className="mt-3 space-y-2 text-sm text-slate-300">
                  {actionCopy.willNotTouch.map((item) => (
                    <li key={item}>- {item}</li>
                  ))}
                </ul>
              </div>
            </div>

            {actionCopy.options && actionCopy.options.length > 0 && (
              <div className="mt-4 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-white">
                  Choose what to include
                </h4>

                <div className="mt-3 space-y-3">
                  {actionCopy.options.map((option) => (
                    <label
                      key={option.id}
                      className="flex items-start gap-3 rounded-lg border border-[var(--rigmd-border)] bg-black/10 p-3 text-sm text-slate-300"
                    >
                      <input
                        type="checkbox"
                        checked={Boolean(selectedOptions[option.id])}
                        onChange={(event) =>
                          setSelectedOptions((prev) => ({
                            ...prev,
                            [option.id]: event.target.checked,
                          }))
                        }
                        className="mt-1"
                      />

                      <span>
                        <span className="block font-bold text-white">
                          {option.label}
                        </span>

                        <span className="mt-1 block text-xs leading-relaxed text-slate-500">
                          {option.description}
                        </span>
                      </span>
                    </label>
                  ))}
                </div>

                {selectedOptionLabels.length > 0 && (
                  <p className="mt-3 text-xs text-slate-500">
                    Selected: {selectedOptionLabels.join(', ')}
                  </p>
                )}
              </div>
            )}

            <label className="mt-4 flex items-start gap-3 rounded-xl border border-amber-400/25 bg-amber-400/[0.06] p-4 text-sm text-amber-100">
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

                {isExecuteLoading ? 'Running...' : 'Proceed'}
              </motion.button>
            </div>
          </motion.div>
        </div>
      )}
    </>
  );
}