import { useMemo, useState } from 'react';
import { motion } from 'motion/react';
import {
  AlertTriangle,
  CheckCircle2,
  Play,
  RotateCcw,
  ShieldCheck,
  TestTube2,
} from 'lucide-react';

import {
  buttonTap,
  cardFadeUp,
  cardTransition,
} from '../lib/motion';

import {
  type AutonomyExecution,
  type AutonomyResult,
  getBackendErrorMessage,
  runAutonomyDryRun,
  runAutonomyExecution,
} from '../services/autonomyService';

interface AutonomyRemediationPanelProps {
  sessionId: string;
  diagnosedCategory: string;
  onExecutionComplete?: (
    result: AutonomyResult,
  ) => void;
}

const ATTEMPT_STATE_LABELS: Record<
  number,
  string
> = {
  0: 'Planned',
  1: 'SafetyRejected',
  2: 'AwaitingConsent',
  3: 'Executing',
  4: 'ExecutionFailed',
  5: 'VerificationPending',
  6: 'Resolved',
  7: 'Unresolved',
  8: 'Worse',
  9: 'VerificationUnknown',
  10: 'RollbackPending',
  11: 'RolledBack',
  12: 'RollbackFailed',
  13: 'PivotPending',
  14: 'Pivoted',
  15: 'Completed',
};

const VERIFICATION_STATUS_LABELS: Record<
  number,
  string
> = {
  0: 'Resolved',
  1: 'Unresolved',
  2: 'Worse',
  3: 'Unknown',
};

function getLatestAttempt(
  result?: AutonomyResult | null,
) {
  return (
    result?.attempts?.[
      result.attempts.length - 1
    ] ?? null
  );
}

function getAttemptStateLabel(
  state?: string | number,
) {
  if (typeof state === 'number') {
    return (
      ATTEMPT_STATE_LABELS[state] ??
      String(state)
    );
  }

  return state ?? '';
}

function getVerificationLabel(
  verification?: string | number,
) {
  if (typeof verification === 'number') {
    return (
      VERIFICATION_STATUS_LABELS[
        verification
      ] ?? String(verification)
    );
  }

  return verification ?? '';
}

function getState(
  result?: AutonomyResult | null,
) {
  if (!result) {
    return 'info';
  }

  const attempt = getLatestAttempt(result);

  const attemptState =
    getAttemptStateLabel(attempt?.state);

  const verificationState =
    getVerificationLabel(
      result.verification,
    );

  const state = (
    attemptState || verificationState
  ).toLowerCase();

  const safety = result.safety;

  const awaitingConsent =
    state.includes('consent') ||
    (safety?.requiresUserConfirmation ===
      true &&
      safety?.isApproved === false);

  if (awaitingConsent) {
    return 'consent';
  }

  if (
    state.includes('reject') ||
    safety?.isApproved === false
  ) {
    return 'rejected';
  }

  if (
    state.includes('rollbackfailed') ||
    state.includes('rollback_failed')
  ) {
    return 'rollback-failed';
  }

  if (
    state.includes('rolledback') ||
    state.includes('rolled_back') ||
    state.includes('rollback')
  ) {
    return 'rollback';
  }

  if (state.includes('unresolved')) {
    return 'unresolved';
  }

  if (state.includes('worse')) {
    return 'failed';
  }

  if (
    state.includes('failed') ||
    result.execution?.success === false
  ) {
    return 'failed';
  }

  if (
    state.includes('resolved') ||
    state.includes('completed')
  ) {
    return 'success';
  }

  if (
    result.execution?.success === true &&
    result.verification === undefined
  ) {
    return 'success';
  }

  return 'info';
}

function getTone(state: string) {
  if (state === 'success') {
    return 'border-emerald-400/25 bg-emerald-400/5 text-emerald-200';
  }

  if (
    state === 'consent' ||
    state === 'unresolved' ||
    state === 'rollback'
  ) {
    return 'border-amber-400/30 bg-amber-400/5 text-amber-200';
  }

  if (
    state === 'rejected' ||
    state === 'failed' ||
    state === 'rollback-failed'
  ) {
    return 'border-red-400/30 bg-red-400/5 text-red-200';
  }

  return 'border-cyan-400/25 bg-cyan-400/5 text-cyan-200';
}

function getTitle(
  state: string,
  mode: 'dry-run' | 'execute',
) {
  if (state === 'consent') {
    return 'User Consent Required';
  }

  if (state === 'rejected') {
    return 'Action Rejected by Safety Rules';
  }

  if (state === 'unresolved') {
    return 'Remediation Unresolved';
  }

  if (state === 'rollback') {
    return 'Rollback Involved';
  }

  if (state === 'rollback-failed') {
    return 'Rollback Failed';
  }

  if (state === 'failed') {
    return mode === 'dry-run'
      ? 'Dry Run Failed'
      : 'Execution Failed';
  }

  if (state === 'success') {
    return mode === 'dry-run'
      ? 'Dry Run Preview Complete'
      : 'Execution Complete';
  }

  return mode === 'dry-run'
    ? 'Dry Run Preview'
    : 'Execution Result';
}

function getSummary(
  result: AutonomyResult,
  mode: 'dry-run' | 'execute',
) {
  const attempt =
    getLatestAttempt(result);

  const state = getState(result);

  if (state === 'consent') {
    return (
      result.safety?.rejectionReason ||
      attempt?.notes ||
      'RigMD cannot continue until user consent is provided.'
    );
  }

  if (state === 'rejected') {
    return (
      result.safety?.rejectionReason ||
      attempt?.notes ||
      'The backend rejected this remediation action.'
    );
  }

  if (state === 'unresolved') {
    return (
      attempt?.notes ||
      'Execution completed, but verification did not confirm resolution.'
    );
  }

  if (state === 'rollback-failed') {
    return (
      attempt?.notes ||
      attempt?.rollbackResult?.summary ||
      'Rollback failed. Further autonomous remediation was stopped.'
    );
  }

  if (state === 'rollback') {
    return (
      attempt?.notes ||
      attempt?.rollbackResult?.summary ||
      'The previous remediation was rolled back.'
    );
  }

  if (state === 'failed') {
    return (
      attempt?.notes ||
      result.execution?.summary ||
      'The remediation did not complete successfully.'
    );
  }

  if (attempt?.notes) {
    return attempt.notes;
  }

  if (result.execution?.summary) {
    return result.execution.summary;
  }

  return mode === 'dry-run'
    ? 'Dry Run previews what RigMD would do without applying real system changes.'
    : 'Real execution applies remediation only after backend safety evaluation.';
}

function ProofList({
  execution,
}: {
  execution?: AutonomyExecution;
}) {
  if (!execution?.proof?.length) {
    return null;
  }

  return (
    <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-2">
      {execution.proof.map(
        (item, index) => (
          <div
            key={`${item.label}-${index}`}
            className="rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-3"
          >
            <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
              {item.label || 'Proof'}
            </p>

            <p className="mt-1 text-xs font-semibold text-white">
              {item.after ||
                item.status ||
                item.before ||
                'Checked'}
            </p>

            {item.meaning && (
              <p className="mt-1 text-[11px] leading-relaxed text-slate-500">
                {item.meaning}
              </p>
            )}
          </div>
        ),
      )}
    </div>
  );
}

function ResultCard({
  result,
  mode,
}: {
  result: AutonomyResult;
  mode: 'dry-run' | 'execute';
}) {
  const state = getState(result);

  const attempt =
    getLatestAttempt(result);

  const plannedActions =
    result.plan?.plannedActions ?? [];

  return (
    <div
      className={`rounded-xl border p-4 text-sm ${getTone(
        state,
      )}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-xs font-bold uppercase tracking-wider">
            {getTitle(state, mode)}
          </p>

          <p className="mt-2 leading-relaxed">
            {getSummary(result, mode)}
          </p>
        </div>

        <span className="rounded-full border border-current/30 px-2.5 py-1 text-[10px] font-bold uppercase">
          {getAttemptStateLabel(
            attempt?.state,
          ) ||
            getVerificationLabel(
              result.verification,
            ) ||
            (mode === 'dry-run'
              ? 'Preview'
              : 'Completed')}
        </span>
      </div>

      {result.escalated && (
        <div className="mt-3 rounded-lg border border-red-400/25 bg-red-400/5 p-3">
          <p className="text-[10px] font-bold uppercase tracking-wider text-red-200">
            Escalation Required
          </p>

          <p className="mt-1 text-xs text-red-100/90">
            RigMD stopped autonomous
            remediation. Further review or
            manual intervention is
            recommended.
          </p>
        </div>
      )}

      {result.safety && (
        <div className="mt-3 rounded-lg border border-current/20 bg-black/10 p-3">
          <p className="text-[10px] font-bold uppercase tracking-wider">
            Safety Check
          </p>

          <p className="mt-1 text-xs">
            {result.safety.isApproved
              ? 'Allowed by backend safety policy.'
              : 'Not approved by backend safety policy.'}{' '}
            {result.safety
              .requiresUserConfirmation
              ? mode === 'dry-run'
                ? 'Dry-run approved. Explicit user consent is required before real execution.'
                : result.safety.isApproved
                  ? 'Required user consent was acknowledged.'
                  : 'User consent is required.'
              : ''}
          </p>

          {result.safety
            .rejectionReason && (
            <p className="mt-1 text-xs opacity-85">
              {
                result.safety
                  .rejectionReason
              }
            </p>
          )}

          {result.safety.warnings
            ?.length ? (
            <ul className="mt-2 space-y-1 text-xs opacity-85">
              {result.safety.warnings.map(
                (warning) => (
                  <li key={warning}>
                    - {warning}
                  </li>
                ),
              )}
            </ul>
          ) : null}
        </div>
      )}

      {plannedActions.length > 0 && (
        <div className="mt-3 rounded-lg border border-current/20 bg-black/10 p-3">
          <p className="text-[10px] font-bold uppercase tracking-wider">
            Planned Steps
          </p>

          <div className="mt-2 space-y-2">
            {plannedActions.map(
              (action, index) => (
                <div
                  key={
                    action.id ||
                    action.name ||
                    index
                  }
                  className="text-xs"
                >
                  <p className="font-bold text-white">
                    {action.name ||
                      action.id ||
                      'Remediation Action'}
                  </p>

                  {action.description && (
                    <p className="mt-0.5 opacity-85">
                      {
                        action.description
                      }
                    </p>
                  )}

                  <p className="mt-0.5 opacity-70">
                    Risk:{' '}
                    {action.riskLevel ||
                      'Not specified'}

                    {action.isReversible !==
                    undefined
                      ? ` | Reversible: ${
                          action.isReversible
                            ? 'Yes'
                            : 'No'
                        }`
                      : ''}
                  </p>
                </div>
              ),
            )}
          </div>
        </div>
      )}

      {attempt?.rollbackResult && (
        <div className="mt-3 rounded-lg border border-amber-300/25 bg-amber-300/5 p-3">
          <div className="flex items-center gap-2 text-amber-200">
            <RotateCcw size={14} />

            <p className="text-[10px] font-bold uppercase tracking-wider">
              Rollback Status
            </p>
          </div>

          <p className="mt-1 text-xs">
            {attempt.rollbackResult
              .success
              ? 'Rollback completed.'
              : 'Rollback failed.'}
          </p>

          {attempt.rollbackResult
            .summary && (
            <p className="mt-1 text-xs opacity-85">
              {
                attempt.rollbackResult
                  .summary
              }
            </p>
          )}
        </div>
      )}

      <ProofList
        execution={
          result.execution ||
          attempt?.execution
        }
      />

      {result.trace && (
        <details className="mt-3 text-xs opacity-80">
          <summary className="cursor-pointer font-bold uppercase tracking-wider">
            Backend Trace
          </summary>

          <pre className="mt-2 max-h-40 overflow-auto whitespace-pre-wrap rounded-lg border border-current/15 bg-black/10 p-3">
            {result.trace}
          </pre>
        </details>
      )}
    </div>
  );
}

export default function AutonomyRemediationPanel({
  sessionId,
  diagnosedCategory,
  onExecutionComplete,
}: AutonomyRemediationPanelProps) {
  const [
    dryRunResult,
    setDryRunResult,
  ] =
    useState<AutonomyResult | null>(
      null,
    );

  const [
    executionResult,
    setExecutionResult,
  ] =
    useState<AutonomyResult | null>(
      null,
    );

  const [
    dryRunError,
    setDryRunError,
  ] =
    useState<string | null>(null);

  const [
    executeError,
    setExecuteError,
  ] =
    useState<string | null>(null);

  const [
    isDryRunLoading,
    setIsDryRunLoading,
  ] = useState(false);

  const [
    isExecuteLoading,
    setIsExecuteLoading,
  ] = useState(false);

  const [
    userConsentProvided,
    setUserConsentProvided,
  ] = useState(false);

  const consentRequired =
    useMemo(() => {
      if (executionResult) {
        return (
          getState(executionResult) ===
          'consent'
        );
      }

      return (
        dryRunResult?.safety
          ?.requiresUserConfirmation ===
        true
      );
    }, [
      dryRunResult,
      executionResult,
    ]);

  const requestActive =
    isDryRunLoading ||
    isExecuteLoading;

  const executeDisabled =
    requestActive ||
    !sessionId ||
    !diagnosedCategory ||
    (consentRequired &&
      !userConsentProvided);

  const runDryRun = async () => {
    if (
      requestActive ||
      !sessionId ||
      !diagnosedCategory
    ) {
      return;
    }

    setIsDryRunLoading(true);
    setDryRunError(null);
    setExecutionResult(null);
    setUserConsentProvided(false);

    try {
      const result =
        await runAutonomyDryRun({
          sessionId,
          diagnosedCategory,
        });

      setDryRunResult(result);
    } catch (error) {
      setDryRunError(
        getBackendErrorMessage(
          error,
        ),
      );
    } finally {
      setIsDryRunLoading(false);
    }
  };

  const runExecution = async () => {
    if (
      executeDisabled ||
      !sessionId ||
      !diagnosedCategory
    ) {
      return;
    }

    setIsExecuteLoading(true);
    setExecuteError(null);

    try {
      const result =
        await runAutonomyExecution({
          sessionId,
          diagnosedCategory,
          userConsentProvided,
        });

      setExecutionResult(result);

      if (
        getState(result) !==
        'consent'
      ) {
        setUserConsentProvided(
          false,
        );
      }

      onExecutionComplete?.(
        result,
      );
    } catch (error) {
      setExecuteError(
        getBackendErrorMessage(
          error,
        ),
      );
    } finally {
      setIsExecuteLoading(false);
    }
  };

  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={cardTransition}
      className="rounded-xl border border-[var(--rigmd-border)] bg-[#101821] p-4"
    >
      <div className="flex items-start gap-3">
        <ShieldCheck
          size={18}
          className="mt-0.5 shrink-0 text-cyan-300"
        />

        <div>
          <h5 className="text-xs font-bold uppercase tracking-wider text-white">
            Autonomy Controller
            Remediation
          </h5>

          <p className="mt-1 text-xs leading-relaxed text-slate-500">
            RigMD previews and executes
            remediation through backend
            safety, verification, rollback,
            and escalation rules.
          </p>
        </div>
      </div>

      <div className="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-2">
        <div className="rounded-xl border border-cyan-400/24 bg-cyan-400/[0.045] p-4">
          <div className="mb-3 flex items-start gap-2">
            <TestTube2
              size={16}
              className="mt-0.5 text-cyan-300"
            />

            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-cyan-200">
                Dry Run Preview
              </p>

              <p className="mt-1 text-xs leading-relaxed text-slate-400">
                Preview the backend
                remediation plan without
                applying real system
                changes.
              </p>
            </div>
          </div>

          <motion.button
            type="button"
            onClick={runDryRun}
            disabled={
              requestActive ||
              !sessionId ||
              !diagnosedCategory
            }
            whileTap={buttonTap}
            className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-400/35 bg-cyan-400/10 px-4 py-2.5 text-xs font-bold text-cyan-200 transition hover:bg-cyan-400/15 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <TestTube2 size={14} />

            {isDryRunLoading
              ? 'Simulating remediation...'
              : 'Simulate Resolution'}
          </motion.button>

          {dryRunError && (
            <p className="mt-3 rounded-lg border border-red-400/25 bg-red-400/5 p-3 text-xs text-red-200">
              {dryRunError}
            </p>
          )}

          {dryRunResult && (
            <div className="mt-3">
              <ResultCard
                result={dryRunResult}
                mode="dry-run"
              />
            </div>
          )}
        </div>

        <div className="rounded-xl border border-amber-400/28 bg-amber-400/[0.04] p-4">
          <div className="mb-3 flex items-start gap-2">
            <AlertTriangle
              size={16}
              className="mt-0.5 text-amber-300"
            />

            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-amber-200">
                Real Execution
              </p>

              <p className="mt-1 text-xs leading-relaxed text-slate-400">
                Applies only the remediation
                approved by the backend
                orchestration and safety
                policy.
              </p>
            </div>
          </div>

          {consentRequired && (
            <label className="mb-3 flex items-start gap-2 rounded-lg border border-amber-400/25 bg-amber-400/5 p-3 text-xs text-amber-100">
              <input
                type="checkbox"
                checked={
                  userConsentProvided
                }
                onChange={(event) =>
                  setUserConsentProvided(
                    event.target.checked,
                  )
                }
                className="mt-0.5"
              />

              <span>
                I understand this
                remediation requires
                explicit user consent and
                want RigMD to continue.
              </span>
            </label>
          )}

          <motion.button
            type="button"
            onClick={runExecution}
            disabled={executeDisabled}
            whileTap={buttonTap}
            className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-amber-400/35 bg-amber-400/10 px-4 py-2.5 text-xs font-bold text-amber-100 transition hover:bg-amber-400/15 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isExecuteLoading ? (
              <CheckCircle2
                size={14}
                className="animate-pulse"
              />
            ) : (
              <Play size={14} />
            )}

            {isExecuteLoading
              ? 'Executing remediation...'
              : consentRequired &&
                  !userConsentProvided
                ? 'Consent Required'
                : 'Execute Remediation'}
          </motion.button>

          {consentRequired &&
            !userConsentProvided && (
              <p className="mt-2 text-xs text-amber-200/80">
                RigMD cannot continue
                until user consent is
                provided.
              </p>
            )}

          {executeError && (
            <p className="mt-3 rounded-lg border border-red-400/25 bg-red-400/5 p-3 text-xs text-red-200">
              {executeError}
            </p>
          )}

          {executionResult && (
            <div className="mt-3">
              <ResultCard
                result={
                  executionResult
                }
                mode="execute"
              />
            </div>
          )}
        </div>
      </div>
    </motion.section>
  );
}