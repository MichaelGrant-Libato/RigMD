import {
  useCallback,
  useEffect,
  useState,
} from 'react';

import { motion } from 'motion/react';

import {
  ArrowLeft,
  CheckCircle2,
  Clock3,
  RefreshCw,
  ShieldCheck,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import AutonomyRemediationPanel from '../components/AutonomyRemediationPanel';

import {
  buttonTap,
  cardFadeUp,
  cardTransition,
  drawerSlideRight,
  pageFade,
  pageTransition,
  staggerContainer,
} from '../lib/motion';

import { apiFetch } from '../lib/api';

import type { AutonomyResult } from '../services/autonomyService';

interface ActionAttemptDetail {
  action_code: string;
  verification_status?: string | null;
  created_at: string;
}

interface RemediationRunDetail {
  run_id: string;
  status: string;
  created_at: string;
  completed_at?: string | null;
  attempts: ActionAttemptDetail[];
}

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
  resolution_proof?: Array<{
    label: string;
    value: string;
    status: string;
    meaning: string;
  }>;
  remediation_history?: RemediationRunDetail[];
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;
}

interface Props {
  sessionId: string;
  onBack: () => void;
}

function getResolutionLabel(
  status?: string,
) {
  if (status === 'resolved') {
    return 'Resolved';
  }

  if (
    status === 'still_active'
  ) {
    return 'Still Active';
  }

  if (
    status === 'needs_recheck'
  ) {
    return 'Needs Recheck';
  }

  return 'Open';
}

function getResolutionStyle(
  status?: string,
) {
  if (status === 'resolved') {
    return 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300';
  }

  if (
    status === 'still_active'
  ) {
    return 'border-red-500/40 bg-red-500/10 text-red-300';
  }

  if (
    status === 'needs_recheck'
  ) {
    return 'border-orange-500/40 bg-orange-500/10 text-orange-300';
  }

  return 'border-cyan-500/40 bg-cyan-500/10 text-cyan-300';
}

function isNoActiveIssue(
  diagnosedCategory?: string | null,
) {
  return (
    diagnosedCategory
      ?.trim()
      .toLowerCase() ===
    'no active issue detected'
  );
}

export default function DiagnosticSessionDetailView({
  sessionId,
  onBack,
}: Props) {
  const [
    session,
    setSession,
  ] =
    useState<SessionDetail | null>(
      null,
    );

  const [
    actions,
    setActions,
  ] =
    useState<RemediationAction[]>(
      [],
    );

  const [
    loading,
    setLoading,
  ] =
    useState(true);

  const [
    checking,
    setChecking,
  ] =
    useState(false);

  const [
    error,
    setError,
  ] =
    useState<string | null>(
      null,
    );

  const fetchSession =
    useCallback(async () => {
      setLoading(true);
      setError(null);

      try {
        const response =
          await apiFetch(
            `/api/diagnosis/sessions/${sessionId}`,
          );

        if (!response.ok) {
          throw new Error(
            `Server returned status ${response.status}`,
          );
        }

        const data =
          await response.json();

        const loadedSession =
          data?.session ??
          (data?.session_id
            ? data
            : null);

        setSession(
          loadedSession,
        );

        if (
          loadedSession
            ?.diagnosed_category &&
          !isNoActiveIssue(
            loadedSession.diagnosed_category,
          )
        ) {
          const actionsResponse =
            await apiFetch(
              `/api/remediation/actions?category=${encodeURIComponent(
                loadedSession.diagnosed_category,
              )}`,
            );

          if (
            !actionsResponse.ok
          ) {
            throw new Error(
              `Server returned status ${actionsResponse.status}`,
            );
          }

          const actionsData =
            await actionsResponse.json();

          setActions(
            Array.isArray(
              actionsData?.actions,
            )
              ? actionsData.actions
              : [],
          );
        } else {
          setActions([]);
        }
      } catch {
        setError(
          'Could not open this diagnostic session.',
        );

        setSession(null);
        setActions([]);
      } finally {
        setLoading(false);
      }
    }, [sessionId]);

  const handleAutonomyExecutionComplete =
    async (
      result: AutonomyResult,
    ) => {
      if (
        !result.execution?.success
      ) {
        return;
      }

      try {
        const statusResponse =
          await apiFetch(
            `/api/diagnosis/${sessionId}/needs-recheck`,
            {
              method: 'POST',
            },
          );

        if (
          !statusResponse.ok
        ) {
          throw new Error(
            `Server returned status ${statusResponse.status}`,
          );
        }

        const statusData =
          await statusResponse.json();

        setSession(
          (prev) =>
            prev
              ? {
                  ...prev,

                  resolution_status:
                    statusData.resolution_status ??
                    'needs_recheck',

                  resolution_summary:
                    statusData.last_action_summary ||
                    'A safe action was performed. Run a follow-up check to see if the issue improved.',
                }
              : prev,
        );
      } catch {
        setError(
          'RigMD completed the autonomy action, but could not mark this session for follow-up verification.',
        );
      }
    };

  const checkResolution =
    async () => {
      setChecking(true);
      setError(null);

      try {
        const response =
          await apiFetch(
            `/api/diagnosis/${sessionId}/check-resolution`,
            {
              method: 'POST',
            },
          );

        if (!response.ok) {
          throw new Error(
            `Server returned status ${response.status}`,
          );
        }

        const data =
          await response.json();

        setSession(
          (prev) =>
            prev
              ? {
                  ...prev,

                  resolution_status:
                    data.resolution_status,

                  resolution_checked_at:
                    data.resolution_checked_at,

                  resolution_summary:
                    data.resolution_summary,

                  resolution_proof:
                    data.resolution_proof,
                }
              : prev,
        );
      } catch {
        setError(
          'Could not check whether this issue is fixed yet.',
        );
      } finally {
        setChecking(false);
      }
    };

  useEffect(() => {
    fetchSession();
  }, [fetchSession]);

  return (
    <>
      <TopHeader
        title="Diagnosis Detail"
        subtitle="Review the saved diagnosis, evidence, and recommended next steps"
      />

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
            <ArrowLeft
              size={16}
            />

            Back to History
          </motion.button>

          {loading ? (
            <div className="space-y-4 rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6">
              {Array.from({
                length: 5,
              }).map(
                (
                  _,
                  index,
                ) => (
                  <div
                    key={
                      index
                    }
                    className="h-12 animate-pulse rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)]"
                  />
                ),
              )}
            </div>
          ) : error ? (
            <div className="rounded-2xl border border-red-500/20 bg-red-500/10 p-5 text-red-300">
              {error}
            </div>
          ) : session ? (
            <>
              <motion.section
                variants={
                  drawerSlideRight
                }
                initial="hidden"
                animate="visible"
                exit="exit"
                transition={
                  pageTransition
                }
                className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6"
              >
                <div className="flex flex-col gap-4 border-b border-[var(--rigmd-border)] pb-5 md:flex-row md:items-start md:justify-between">
                  <div>
                    <div className="text-xs font-bold uppercase tracking-[0.22em] text-slate-500">
                      {
                        session.display_date
                      }{' '}
                      {
                        session.display_time
                      }
                    </div>

                    <h2 className="mt-2 text-2xl font-bold text-white">
                      {
                        session.diagnosed_category
                      }
                    </h2>

                    {session.symptom_type && (
                      <p className="mt-2 text-sm text-slate-400">
                        {
                          session.symptom_type
                        }
                      </p>
                    )}
                  </div>

                  <div className="flex flex-wrap gap-2">
                    <span className="rounded border border-emerald-500/40 bg-emerald-500/10 px-3 py-1 text-xs font-bold uppercase text-emerald-400">
                      {
                        session.action_category
                      }
                    </span>

                    <span className="rounded border border-cyan-500/40 bg-cyan-500/10 px-3 py-1 text-xs font-bold uppercase text-cyan-400">
                      {
                        session.confidence_label
                      }
                    </span>

                    {!isNoActiveIssue(
                      session.diagnosed_category,
                    ) && (
                      <span
                        className={`rounded border px-3 py-1 text-xs font-bold uppercase ${getResolutionStyle(
                          session.resolution_status,
                        )}`}
                      >
                        {getResolutionLabel(
                          session.resolution_status,
                        )}
                      </span>
                    )}
                  </div>
                </div>

                {!isNoActiveIssue(
                  session.diagnosed_category,
                ) && (
                  <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Activity
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.affected_activity ||
                          'Not recorded'}
                      </p>
                    </div>

                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Frequency
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.frequency ||
                          'Not recorded'}
                      </p>
                    </div>

                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Severity
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.severity ||
                          'Not recorded'}
                      </p>
                    </div>
                  </div>
                )}

                <div className="mt-5 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">
                    Explanation
                  </h3>

                  <p className="mt-2 text-sm leading-relaxed text-slate-300">
                    {session.ai_explanation ||
                      'No explanation saved for this session.'}
                  </p>
                </div>

                {session.recommended_next_step && (
                  <div className="mt-4 rounded-xl border border-cyan-500/20 bg-cyan-500/[0.045] p-4">
                    <h3 className="text-xs font-bold uppercase tracking-wider text-cyan-300">
                      Recommended Next Step
                    </h3>

                    <p className="mt-2 text-sm leading-relaxed text-slate-300">
                      {
                        session.recommended_next_step
                      }
                    </p>
                  </div>
                )}
              </motion.section>

              {isNoActiveIssue(
                session.diagnosed_category,
              ) ? (
                <motion.section
                  variants={
                    cardFadeUp
                  }
                  initial="hidden"
                  animate="visible"
                  transition={
                    cardTransition
                  }
                  className="rounded-2xl border border-emerald-500/25 bg-emerald-500/[0.045] p-6"
                >
                  <div className="flex items-start gap-3">
                    <CheckCircle2
                      size={22}
                      className="mt-0.5 shrink-0 text-emerald-300"
                    />

                    <div>
                      <h3 className="text-sm font-bold uppercase tracking-wider text-emerald-200">
                        No Active Issue Requiring Remediation
                      </h3>

                      <p className="mt-2 text-sm leading-relaxed text-slate-300">
                        The latest supported Agent evidence does not currently verify an active issue within this diagnosis scope.
                      </p>

                      <p className="mt-3 text-sm leading-relaxed text-slate-400">
                        No remediation or follow-up verification is required right now. Continue monitoring the system under normal use and run a new diagnosis if symptoms appear later.
                      </p>
                    </div>
                  </div>
                </motion.section>
              ) : (
                <>
                  <motion.section
                    variants={
                      cardFadeUp
                    }
                    initial="hidden"
                    animate="visible"
                    transition={
                      cardTransition
                    }
                    className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6"
                  >
                    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                      <div>
                        <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                          Resolution Status
                        </h3>

                        <p className="mt-2 text-sm leading-relaxed text-slate-400">
                          {session.resolution_summary ||
                            'Use a safe action if needed, then check whether the live issue is fixed.'}
                        </p>
                      </div>

                      <motion.button
                        type="button"
                        onClick={
                          checkResolution
                        }
                        disabled={
                          checking
                        }
                        whileTap={
                          buttonTap
                        }
                        className="inline-flex items-center justify-center gap-2 rounded-lg border border-cyan-500/40 bg-cyan-500/5 px-4 py-2 text-sm font-bold text-cyan-400 hover:bg-cyan-500/10 disabled:opacity-50"
                      >
                        <RefreshCw
                          size={16}
                          className={
                            checking
                              ? 'animate-spin'
                              : ''
                          }
                        />

                        {checking
                          ? 'Checking...'
                          : 'Check if Fixed'}
                      </motion.button>
                    </div>

                    {session.resolution_proof &&
                      session
                        .resolution_proof
                        .length >
                        0 && (
                        <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
                          {session.resolution_proof.map(
                            (
                              item,
                            ) => (
                              <div
                                key={
                                  item.label
                                }
                                className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4"
                              >
                                <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                                  {
                                    item.label
                                  }
                                </p>

                                <p className="mt-1 text-sm font-bold text-white">
                                  {
                                    item.value
                                  }
                                </p>

                                <p className="mt-2 text-xs leading-relaxed text-slate-500">
                                  {
                                    item.meaning
                                  }
                                </p>
                              </div>
                            ),
                          )}
                        </div>
                      )}
                  </motion.section>

                  {session.remediation_history &&
                    session
                      .remediation_history
                      .length >
                      0 && (
                      <motion.section
                        variants={
                          cardFadeUp
                        }
                        initial="hidden"
                        animate="visible"
                        transition={
                          cardTransition
                        }
                        className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6"
                      >
                        <div className="flex items-start gap-3">
                          <Clock3
                            size={20}
                            className="mt-0.5 text-amber-400"
                          />

                          <div>
                            <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                              Remediation History
                            </h3>

                            <p className="mt-2 text-sm leading-relaxed text-slate-400">
                              Past autonomous remediation attempts for this diagnosis.
                            </p>
                          </div>
                        </div>

                        <div className="mt-5 space-y-3">
                          {session.remediation_history.map(
                            (
                              run,
                            ) => (
                              <div
                                key={
                                  run.run_id
                                }
                                className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4"
                              >
                                <div className="flex items-center justify-between">
                                  <div className="flex items-center gap-2">
                                    <span
                                      className={`rounded border px-2 py-0.5 text-[10px] font-bold uppercase ${
                                        run.status ===
                                        'Resolved'
                                          ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300'
                                          : run.status ===
                                              'Failed'
                                            ? 'border-red-500/40 bg-red-500/10 text-red-300'
                                            : 'border-amber-500/40 bg-amber-500/10 text-amber-300'
                                      }`}
                                    >
                                      {
                                        run.status
                                      }
                                    </span>

                                    <span className="text-[11px] text-slate-500">
                                      {new Date(
                                        run.created_at,
                                      ).toLocaleString(
                                        [],
                                        {
                                          dateStyle:
                                            'medium',
                                          timeStyle:
                                            'short',
                                        },
                                      )}
                                    </span>
                                  </div>
                                </div>

                                {run.attempts
                                  .length >
                                  0 && (
                                  <div className="mt-3 space-y-2">
                                    {run.attempts.map(
                                      (
                                        attempt,
                                        idx,
                                      ) => (
                                        <div
                                          key={
                                            idx
                                          }
                                          className="flex items-center justify-between rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-3 py-2"
                                        >
                                          <span className="text-xs font-semibold text-slate-300">
                                            {attempt.action_code.replace(
                                              /_/g,
                                              ' ',
                                            )}
                                          </span>

                                          <span
                                            className={`rounded border px-2 py-0.5 text-[10px] font-bold uppercase ${
                                              attempt.verification_status ===
                                              'Resolved'
                                                ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
                                                : attempt.verification_status
                                                  ? 'border-red-500/30 bg-red-500/10 text-red-300'
                                                  : 'border-slate-500/30 bg-slate-500/10 text-slate-400'
                                            }`}
                                          >
                                            {attempt.verification_status ||
                                              'Pending'}
                                          </span>
                                        </div>
                                      ),
                                    )}
                                  </div>
                                )}
                              </div>
                            ),
                          )}
                        </div>
                      </motion.section>
                    )}

                  <motion.section
                    variants={
                      cardFadeUp
                    }
                    initial="hidden"
                    animate="visible"
                    transition={
                      cardTransition
                    }
                    className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-6"
                  >
                    <div className="flex items-start gap-3">
                      <ShieldCheck
                        size={20}
                        className="mt-0.5 text-cyan-400"
                      />

                      <div>
                        <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                          Safe Actions Available
                        </h3>

                        <p className="mt-2 text-sm leading-relaxed text-slate-400">
                          Preview the backend autonomy plan, run an approved action if needed, then use Check if Fixed to prove whether the issue is resolved.
                        </p>
                      </div>
                    </div>

                    <div className="mt-5 space-y-3">
                      <AutonomyRemediationPanel
                        diagnosedCategory={
                          session.diagnosed_category
                        }
                        onExecutionComplete={
                          handleAutonomyExecutionComplete
                        }
                        sessionId={
                          sessionId
                        }
                      />

                      {actions.length ===
                      0 ? (
                        <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4 text-sm text-slate-500">
                          No safe action is available for this saved diagnosis.
                        </div>
                      ) : (
                        <motion.div
                          variants={
                            staggerContainer
                          }
                          initial="hidden"
                          animate="visible"
                          className="space-y-3"
                        >
                          {actions.map(
                            (
                              action,
                            ) => (
                              <motion.div
                                key={
                                  action.id
                                }
                                variants={
                                  cardFadeUp
                                }
                                transition={
                                  cardTransition
                                }
                                className="rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4"
                              >
                                <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                                  <div>
                                    <h4 className="text-sm font-bold text-cyan-400">
                                      {
                                        action.label
                                      }
                                    </h4>

                                    <p className="mt-1 text-xs leading-relaxed text-slate-400">
                                      {
                                        action.description
                                      }
                                    </p>

                                    <p className="mt-1 text-[11px] text-slate-500">
                                      {
                                        action.risk
                                      }
                                    </p>
                                  </div>

                                  <span className="rounded-lg border border-cyan-500/30 bg-cyan-500/10 px-3 py-2 text-[10px] font-bold uppercase tracking-wider text-cyan-300">
                                    Backend candidate
                                  </span>
                                </div>
                              </motion.div>
                            ),
                          )}
                        </motion.div>
                      )}
                    </div>
                  </motion.section>
                </>
              )}
            </>
          ) : (
            <div className="rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-6 py-16 text-center">
              <CheckCircle2
                size={40}
                className="mx-auto mb-4 text-slate-600"
              />

              <h3 className="text-lg font-bold text-white">
                Session not found
              </h3>
            </div>
          )}
        </div>
      </motion.div>
    </>
  );
}