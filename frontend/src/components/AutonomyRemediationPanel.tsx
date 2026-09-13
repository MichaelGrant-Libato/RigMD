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
  type MemoryAppCandidate,
  closeSelectedApp,
  getMemoryAppCandidates,
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

type ActionReviewCopy = {
  title: string;
  plainDescription: string;
  willDo: string[];
  mayChange: string[];
  willNotTouch: string[];
  beforeRun?: string[];
  afterRun?: string[];
  options?: Array<{
    id: string;
    label: string;
    description: string;
    defaultChecked: boolean;
  }>;
};

const ACTION_COPY: Record<string, ActionReviewCopy> = {
  close_selected_app: {
    title: 'Close selected app',
    plainDescription:
      'RigMD will close only the app you choose. This can reduce memory pressure when one app is using a large amount of memory.',
    willDo: [
      'Show memory-heavy apps found in the latest live scan.',
      'Close only the app you select.',
      'Run Check if Fixed after the app closes.',
    ],
    mayChange: [
      'The selected app may close open windows or tabs.',
      'Unsaved work, active downloads, forms, or private windows in that app may be lost.',
      'Memory use may drop after Windows finishes closing the app.',
    ],
    willNotTouch: [
      'Apps you do not select.',
      'Personal files saved on disk.',
      'Passwords, accounts, or unrelated Windows settings.',
    ],
    beforeRun: [
      'Choose the app you are ready to close.',
      'Save important work in that app before continuing.',
    ],
    afterRun: [
      'RigMD will recheck the saved diagnosis automatically.',
      'If memory pressure is normal after the app closes, the saved check can become resolved.',
    ],
  },

  clear_user_temp_files: {
    title: 'Clean temporary files',
    plainDescription:
      'RigMD will remove safe temporary files that Windows and apps no longer need.',
    willDo: [
      'Show this review before removing anything.',
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
    beforeRun: [
      'RigMD targets the current Windows user temporary folders.',
      'This is not a scan of Documents, Pictures, Downloads, or Desktop.',
    ],
    afterRun: [
      'RigMD will report whether the cleanup completed.',
      'If proof is available, RigMD will show what was checked after the action.',
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
      'Show this review before changing the DNS cache.',
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
    beforeRun: [
      'RigMD targets the Windows DNS resolver cache only.',
      'This is normally used when websites or network lookups behave incorrectly.',
    ],
    afterRun: [
      'Windows will create fresh lookup records as websites are visited again.',
      'RigMD will report whether the command completed.',
    ],
  },

  clear_browser_cache: {
    title: 'Clear browser cache',
    plainDescription:
      'RigMD may clear cached browser files that can be rebuilt later.',
    willDo: [
      'Show this review before clearing browser cache.',
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
    beforeRun: [
      'RigMD targets cached website files only.',
      'Cache files are copies that browsers can download again later.',
    ],
    afterRun: [
      'Some websites may rebuild their cache the next time they are opened.',
      'RigMD will not sign you out intentionally or remove bookmarks.',
    ],
  },

  run_disk_cleanup: {
    title: 'Run Windows cleanup',
    plainDescription:
      'RigMD may start a Windows cleanup task to remove system-safe temporary files.',
    willDo: [
      'Show this review before starting Windows cleanup.',
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
    beforeRun: [
      'RigMD uses the built-in Windows cleanup path.',
      'Windows decides which system-safe temporary files are eligible.',
    ],
    afterRun: [
      'RigMD will report whether Windows cleanup started or completed.',
      'Available storage may improve if Windows removed temporary files.',
    ],
  },

  restart_explorer: {
    title: 'Restart Windows Explorer',
    plainDescription:
      'RigMD may restart the Windows shell. This can help when the desktop, taskbar, or File Explorer is stuck.',
    willDo: [
      'Show this review before restarting Windows Explorer.',
      'Close and reopen the Windows Explorer process.',
      'Check whether the action completed.',
    ],
    mayChange: [
      'The taskbar, desktop icons, and open File Explorer windows may briefly disappear and return.',
      'Unsaved work inside File Explorer windows may be interrupted.',
    ],
    willNotTouch: [
      'Documents, pictures, downloads, or desktop files.',
      'Passwords or accounts.',
      'Installed apps.',
    ],
    beforeRun: [
      'This targets the Windows desktop shell, not your personal files.',
      'Open apps should stay open, but File Explorer windows may refresh.',
    ],
    afterRun: [
      'The taskbar and desktop should return automatically.',
      'RigMD will report whether the restart completed.',
    ],
  },

  clear_windows_update_cache: {
    title: 'Clear Windows Update cache',
    plainDescription:
      'RigMD may remove downloaded Windows Update files so Windows can download fresh copies later.',
    willDo: [
      'Show this review before touching update cache files.',
      'Target downloaded Windows Update cache files.',
      'Let Windows recreate the cache when updates run again.',
    ],
    mayChange: [
      'Windows Update may need to download update files again.',
      'A later update check may take longer than usual.',
    ],
    willNotTouch: [
      'Personal files.',
      'Passwords or accounts.',
      'Installed apps outside Windows Update cache files.',
    ],
    beforeRun: [
      'This targets Windows Update download cache, not user folders.',
      'This is usually used when update downloads are stuck or corrupted.',
    ],
    afterRun: [
      'Windows Update should be able to rebuild fresh update files.',
      'RigMD will report whether the cache action completed.',
    ],
  },

  run_sfc_scan: {
    title: 'Run Windows system file check',
    plainDescription:
      'RigMD may run the built-in Windows System File Checker to inspect and repair protected Windows system files.',
    willDo: [
      'Show this review before starting the Windows system file check.',
      'Run the official Windows SFC tool.',
      'Report whether Windows found or repaired protected system file problems.',
    ],
    mayChange: [
      'Windows may repair protected system files if corruption is found.',
      'The check may take several minutes.',
    ],
    willNotTouch: [
      'Personal documents, pictures, downloads, or desktop files.',
      'Passwords or accounts.',
      'Third-party app settings unless Windows itself changes a protected system file.',
    ],
    beforeRun: [
      'This targets protected Windows system files only.',
      'It is meant for Windows stability issues, not personal file cleanup.',
    ],
    afterRun: [
      'RigMD will show whether the scan completed.',
      'If Windows reports repairs, RigMD will show that result when available.',
    ],
  },
};

function getFallbackActionCopy(
  actionName?: string,
  diagnosedCategory?: string,
): ActionReviewCopy {
  const category = (diagnosedCategory || '').toLowerCase();
  const readableName = actionName || 'Safe action';

  if (category.includes('memory') || category.includes('resource')) {
    return {
      title: readableName,
      plainDescription:
        'RigMD will check the part of the device related to high memory or resource use before it tries anything.',
      willDo: [
        'Review the current diagnosis and approved safe action.',
        'Apply only the action connected to memory or system resource usage.',
        'Check whether the action completed or needs another look.',
      ],
      mayChange: [
        'Temporary app activity may be reduced or refreshed.',
        'Windows may have more breathing room if the action clears temporary workload data.',
      ],
      willNotTouch: [
        'Documents, pictures, downloads, or desktop files.',
        'Passwords or accounts.',
        'Unrelated Windows settings.',
      ],
      beforeRun: [
        'Affected concept: active memory pressure, which means apps and Windows are using a large share of available RAM.',
        'Affected concept: running workload data, such as temporary app activity RigMD uses to decide if the device is under strain.',
      ],
      afterRun: [
        'Possible change: the device may feel less strained if temporary workload data is cleared or refreshed.',
        'Possible change: RigMD may still say monitoring is needed if memory use remains high after the action.',
      ],
    };
  }

  if (category.includes('storage') || category.includes('disk')) {
    return {
      title: readableName,
      plainDescription:
        'RigMD will focus on storage-related cleanup or checks, not personal files.',
      willDo: [
        'Review the storage-related safe action before running it.',
        'Apply only the approved storage action.',
        'Check whether the action completed or needs another look.',
      ],
      mayChange: [
        'Temporary files or cached update files may be removed.',
        'Available storage space may increase if safe cleanup files are found.',
      ],
      willNotTouch: [
        'Documents, pictures, downloads, or desktop files.',
        'Passwords or accounts.',
        'Installed apps.',
      ],
      beforeRun: [
        'Affected concept: temporary storage, which means files Windows or apps can recreate later.',
        'Affected concept: available space, which means how much room is left on the drive for updates, apps, and normal use.',
      ],
      afterRun: [
        'Possible change: the drive may have more free space.',
        'Possible change: Windows or apps may recreate needed temporary files later.',
      ],
    };
  }

  if (category.includes('network') || category.includes('internet')) {
    return {
      title: readableName,
      plainDescription:
        'RigMD will focus on Windows network lookup data, not your Wi-Fi password or account.',
      willDo: [
        'Review the network-related safe action before running it.',
        'Apply only the approved network action.',
        'Check whether the action completed or needs another look.',
      ],
      mayChange: [
        'Windows may forget old website lookup records.',
        'The next website visit may take a moment while Windows creates fresh lookup records.',
      ],
      willNotTouch: [
        'Personal files.',
        'Browser history.',
        'Wi-Fi passwords or account passwords.',
      ],
      beforeRun: [
        'Affected concept: website lookup cache, which helps Windows remember where websites are located.',
        'Affected concept: network name lookup, not your saved Wi-Fi network or router settings.',
      ],
      afterRun: [
        'Possible change: websites may be looked up fresh the next time you open them.',
        'Possible change: a connection issue caused by stale lookup records may improve.',
      ],
    };
  }

  if (
    category.includes('os') ||
    category.includes('windows') ||
    category.includes('performance')
  ) {
    return {
      title: readableName,
      plainDescription:
        'RigMD will focus on the Windows area related to the diagnosis and avoid personal files.',
      willDo: [
        'Review the Windows-related safe action before running it.',
        'Apply only the approved Windows action.',
        'Check whether the action completed or needs another look.',
      ],
      mayChange: [
        'A Windows tool or setting screen may open.',
        'A Windows background component may refresh if the selected action requires it.',
      ],
      willNotTouch: [
        'Documents, pictures, downloads, or desktop files.',
        'Passwords or accounts.',
        'Unrelated Windows settings.',
      ],
      beforeRun: [
        'Affected concept: Windows behavior related to this diagnosis, such as startup, desktop, or system stability.',
        'Affected concept: the specific safe action shown here, not the whole computer.',
      ],
      afterRun: [
        'Possible change: a Windows tool may open or a Windows component may refresh.',
        'Possible change: RigMD may ask you to check again if it cannot prove the issue improved.',
      ],
    };
  }

  return {
    title: readableName,
    plainDescription:
      'RigMD found a safe action that may help with this diagnosis.',
    willDo: [
      'Show this review before running anything.',
      'Run only the approved action for this diagnosis.',
      'Check whether the action completed successfully.',
    ],
    mayChange: [
      `Only the part related to "${readableName}" may be changed.`,
    ],
    willNotTouch: [
      'Personal files unless the action clearly says so.',
      'Passwords or accounts.',
      'Unrelated Windows settings.',
    ],
    beforeRun: [
      'Affected concept: the specific safe action shown in this popup.',
      'If the affected area is unclear, cancel and do not continue.',
    ],
    afterRun: [
      'Possible change: RigMD will show whether the action completed or failed.',
      'If it cannot confirm success, it will say so instead of pretending it worked.',
    ],
  };
}

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

function getReadableAction(
  actionId?: string,
  actionName?: string,
  diagnosedCategory?: string,
) {
  if (actionId && ACTION_COPY[actionId]) {
    return ACTION_COPY[actionId];
  }

  return getFallbackActionCopy(actionName, diagnosedCategory);
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

  const [selectedOptions, setSelectedOptions] =
    useState<Record<string, boolean>>({});

  const [memoryApps, setMemoryApps] =
    useState<MemoryAppCandidate[]>([]);

  const [selectedProcessNames, setSelectedProcessNames] =
    useState<string[]>([]);

  const requestActive = isPreviewLoading || isExecuteLoading;

  const closeSelectedAppFlow =
    shouldUseCloseSelectedAppFlow(diagnosedCategory);

  const primaryAction = getPrimaryAction(previewResult);

  const actionCopy = getReadableAction(
    closeSelectedAppFlow ? 'close_selected_app' : primaryAction?.id,
    primaryAction?.name,
    diagnosedCategory,
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
    userConsentProvided &&
    !requestActive &&
    (!closeSelectedAppFlow || selectedProcessNames.length > 0);

  const selectedMemoryApps =
    memoryApps.filter((app) => selectedProcessNames.includes(app.name));

  const selectedAppNames =
    selectedMemoryApps.map((app) => app.displayName || app.name);

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

        setSelectedOptions({});
        setReviewOpen(true);
        return;
      }

      const result = await runAutonomyPreview({
        sessionId,
        diagnosedCategory,
      });

      setPreviewResult(result);

      const action = getPrimaryAction(result);
      const readable = getReadableAction(
        action?.id,
        action?.name,
        diagnosedCategory,
      );

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
        closeSelectedAppFlow
          ? await closeSelectedApp({
              processNames: selectedProcessNames,
              confirmed: true,
            })
          : shouldExecuteThroughAgent && agentActionId
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

              {(actionCopy.beforeRun || actionCopy.afterRun) && (
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  {actionCopy.beforeRun && (
                    <div className="rounded-xl border border-slate-400/20 bg-slate-400/[0.04] p-4">
                      <h4 className="text-xs font-bold uppercase tracking-wider text-slate-200">
                        Before it runs
                      </h4>

                      <ul className="mt-3 space-y-2 text-sm text-slate-300">
                        {actionCopy.beforeRun.map((item) => (
                          <li key={item}>- {item}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {actionCopy.afterRun && (
                    <div className="rounded-xl border border-slate-400/20 bg-slate-400/[0.04] p-4">
                      <h4 className="text-xs font-bold uppercase tracking-wider text-slate-200">
                        After it runs
                      </h4>

                      <ul className="mt-3 space-y-2 text-sm text-slate-300">
                        {actionCopy.afterRun.map((item) => (
                          <li key={item}>- {item}</li>
                        ))}
                      </ul>
                    </div>
                  )}
                </div>
              )}
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
