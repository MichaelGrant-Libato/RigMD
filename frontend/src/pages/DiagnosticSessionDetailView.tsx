import DiagnosticReadings from '../components/DiagnosticReadings';
import CheckReadings from '../components/CheckReadings';
import { canCheckCurrentStatus, getCheckOverview, getCheckStatus, getLatestCheckSummary, getCheckAdvice, getAdditionalFindingStatus, crashMetric, getCheckEvidenceLabel, getCheckCoverageNotice, getCheckPanelClassName } from '../lib/checkPresentation';
import { getFindingChanges } from '../lib/findingChanges';
import { explanationSourceLabel } from '../lib/diagnosticReadingVisuals';
import { plainDiagnosticText, friendlyActionCategory, friendlyToolName, friendlyVerification } from '../lib/diagnosticLanguage';
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
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import AutonomyRemediationPanel, { ProofList } from '../components/AutonomyRemediationPanel';

import {
  buttonTap,
  cardFadeUp,
  cardTransition,
  drawerSlideRight,
  pageFade,
  pageTransition,
} from '../lib/motion';

import { apiFetch } from '../lib/api';

import type { AutonomyResult } from '../services/autonomyService';
import {
  getTelemetryDeltaSummary,
  COMPONENT_LABELS,
  SCENARIO_LABELS,
  parseComponentIds,
  titleFromId,
} from '../lib/pastChecksFormatting';

interface ActionAttemptDetail {
  execution_summary?: string | null;
  proof?: import('../services/autonomyService').AutonomyExecutionProof[];
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
  diagnosis_mode?: string;
  component_ids?: string;
  scenario_id?: string;
  agent_id?: string;
  agent_command_id?: string;
  diagnosed_category: string;
  action_category: string;
  confidence_label: string;
  ai_explanation?: string;
  explanation_source?: string;
  explanation_model?: string;
  recommended_next_step?: string;
  primary_result?: string;
  proof?: SessionDetail['resolution_proof'];
  incidental_warning?: string | null;
  component_status?: string;
  resolution_status?: string;
  resolution_checked_at?: string | null;
  resolution_summary?: string;
  resolution_proof?: Array<{
    metric_id?: string;
    captured_at?: string | null;
    evidence_kind?: string;
    rule_version?: string;
    label: string;
    value: string;
    status: string;
    meaning: string;
  }>;
  remediation_history?: RemediationRunDetail[];
}

interface Props {
  sessionId: string;
  onBack: () => void;
  onOpenMemoryCheck?: () => void;
}







function getResolutionButtonText(
  status?: string,
  checking = false,
) {
  const normalized =
    status?.trim().toLowerCase();
  const hasBeenChecked =
    Boolean(normalized) &&
    normalized !== 'open' &&
    normalized !== 'not_checked';

  if (checking) {
    return hasBeenChecked
      ? 'Rechecking...'
      : 'Checking...';
  }

  return hasBeenChecked
    ? 'Recheck Current Status'
    : 'Check Current Status';
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

function isAutomaticDiagnosis(
  session: SessionDetail,
) {
  return Boolean(
    session.diagnosis_mode?.trim(),
  );
}

function formatDiagnosisMode(
  mode?: string,
) {
  if (!mode) {
    return 'Automatic';
  }

  return ({ full: 'Whole computer', component: 'Selected parts', scenario: 'Problem check' } as Record<string, string>)[mode] ?? titleFromId(mode);
}

function formatScenario(scenarioId?: string) {
  return scenarioId ? SCENARIO_LABELS[scenarioId] ?? titleFromId(scenarioId) : 'Not specified';
}

function formatComponentIds(value?: string) {
  const ids = parseComponentIds(value);
  return ids.length ? ids.map(id => COMPONENT_LABELS[id.toLowerCase()] ?? titleFromId(id)).join(', ') : 'Full system';
}

export default function DiagnosticSessionDetailView({
  sessionId,
  onBack,
  onOpenMemoryCheck,
}: Props) {
  const [
    session,
    setSession,
  ] =
    useState<SessionDetail | null>(
      null,
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
      } catch {
        setError(
          'Could not open this diagnostic session.',
        );

        setSession(null);
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
                  resolution_checked_at: undefined,
                  resolution_proof: [],

                  resolution_summary:
                    statusData.last_action_summary ||
                    'The approved action finished. Its effect still needs checking.',
                }
              : prev,
        );

        const resolutionResponse =
          await apiFetch(
            `/api/diagnosis/${sessionId}/check-resolution`,
            {
              method: 'POST',
            },
          );

        if (
          !resolutionResponse.ok
        ) {
          throw new Error(
            `Server returned status ${resolutionResponse.status}`,
          );
        }

        const resolutionData =
          await resolutionResponse.json();

        setSession(
          (prev) =>
            prev
              ? {
                  ...prev,

                  resolution_status:
                    resolutionData.resolution_status,

                  resolution_checked_at:
                    resolutionData.resolution_checked_at,

                  resolution_summary:
                    resolutionData.resolution_summary,

                  resolution_proof:
                    resolutionData.resolution_proof,
                }
              : prev,
        );
      } catch {
        setError(
          'RigMD completed the safe action, but could not recheck the current issue status.',
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
          'Could not recheck the current issue status.',
        );
      } finally {
        setChecking(false);
      }
    };

  const showResolutionStatus = canCheckCurrentStatus(session);

  useEffect(() => {
    fetchSession();
  }, [fetchSession]);

  return (
    <>
      <TopHeader
        title="Check results"
        subtitle="Findings, readings, and next steps"
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
          ) : error && !session ? (
            <div role="alert" className="rounded-2xl border border-red-500/20 bg-red-500/10 p-5 text-red-300">
              {error}
            </div>
          ) : session ? (
            <>
              {error && (
                <div role="alert" className="rounded-lg border border-red-500/20 bg-red-500/10 p-4 text-sm text-red-300">
                  {error}
                </div>
              )}
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
                    <p className="mb-2 text-xs font-semibold text-slate-400">Original check</p>
                    <div className="text-xs font-bold uppercase tracking-[0.22em] text-slate-400">
                      {
                        session.display_date
                      }{' '}
                      {
                        session.display_time
                      }
                    </div>

                    <h2 className="mt-2 text-2xl font-bold text-white">
                      {
                        getCheckOverview(session).title
                      }
                    </h2>

                    {getCheckOverview(session).summary ? (
                      <p className="mt-2 text-sm leading-relaxed text-slate-200">
                        {getCheckOverview(session).summary}
                      </p>
                    ) : session.symptom_type ? (
                      <p className="mt-2 text-sm text-slate-400">
                        {
                          session.symptom_type
                        }
                      </p>
                    ) : null}
                    <p className="mt-2 text-sm leading-relaxed text-slate-400">{getCheckOverview(session).meaning}</p>
                    {getCheckCoverageNotice(session) && <p className="mt-2 text-sm leading-relaxed text-amber-200">{getCheckCoverageNotice(session)}</p>}
                  </div>

                  <div className="flex max-w-full flex-wrap gap-2 md:max-w-[280px]">
                    <span className="rounded border border-emerald-500/40 bg-emerald-500/10 px-3 py-1 text-xs font-bold uppercase text-emerald-400">
                      {
                        friendlyActionCategory(session.action_category)
                      }
                    </span>

                    <span className="rounded border border-cyan-500/40 bg-cyan-500/10 px-3 py-1 text-xs font-bold uppercase text-cyan-400">
                      {
                        getCheckEvidenceLabel(session)
                      }
                    </span>

                    {(showResolutionStatus || getCheckStatus(session).category === 'needs_recheck') && (
                      <span
                        className={`rounded border px-3 py-1 text-xs font-bold uppercase ${getCheckStatus(session).className}`}
                      >
                        {getCheckStatus(session).label}
                      </span>
                    )}
                  </div>
                </div>

                {isAutomaticDiagnosis(
                  session,
                ) ? (
                  <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        What was checked
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.diagnosis_mode ===
                        'scenario'
                          ? formatScenario(
                              session.scenario_id,
                            )
                          : session.diagnosis_mode ===
                              'component'
                            ? formatComponentIds(
                                session.component_ids,
                              )
                            : 'Full System'}
                      </p>
                    </div>

                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Check type
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {formatDiagnosisMode(
                          session.diagnosis_mode,
                        )}
                      </p>
                    </div>

                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Source
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        Readings from this PC
                      </p>
                    </div>
                  </div>
                ) : (
                  <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Activity
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.affected_activity ||
                          'Not recorded'}
                      </p>
                    </div>

                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Frequency
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.frequency ||
                          'Not recorded'}
                      </p>
                    </div>

                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Severity
                      </p>

                      <p className="mt-1 text-sm font-semibold text-white">
                        {session.severity ||
                          'Not recorded'}
                      </p>
                    </div>
                  </div>
                )}

                {session.recommended_next_step && (
                  <div className="mt-5 rounded-xl border border-cyan-500/20 bg-cyan-500/[0.045] p-4">
                    <h3 className="text-xs font-bold uppercase tracking-wider text-cyan-300">
                      {session.resolution_proof?.length ? 'Advice from the original check' : 'What to do next'}
                    </h3>

                    <p className="mt-1.5 text-sm leading-relaxed text-slate-200">
                      {
                        getCheckAdvice(session)
                      }
                    </p>
                  </div>
                )}

                <div className="mt-5"><CheckReadings check={session} /></div>
                {!session.proof?.length && <p className="mt-3 text-xs text-slate-400">The original detailed readings are not available for this saved check. The summary above is the saved result.</p>}
                <details className="mt-4 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                  <summary className="cursor-pointer font-semibold text-slate-200">Technical result details</summary>
                  <p className="mt-3 text-xs text-slate-400">Explanation source: {explanationSourceLabel(session.explanation_source, session.explanation_model)}</p>

                  <p className="mt-2 break-words text-sm text-slate-300">{session.diagnosed_category}: {session.primary_result}</p>
                  <p className="mt-2 text-sm leading-relaxed text-slate-300">
                    {session.ai_explanation ||
                      'No explanation saved for this session.'}
                  </p>
                </details>
              </motion.section>

              {showResolutionStatus && (
                <motion.section
                  variants={
                    cardFadeUp
                  }
                  initial="hidden"
                  animate="visible"
                  transition={
                    cardTransition
                  }
                  className={`rounded-2xl border p-6 ${getCheckPanelClassName(session)}`}
                >
                  <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                    <div>
                      <div className="flex items-center gap-2.5">
                        <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                          Latest status
                        </h3>
                        {(() => {
                          const resInfo = getCheckStatus(session);
                          return (
                            <span
                              className={`rounded border px-2 py-0.5 text-xs font-bold uppercase ${resInfo.className}`}
                            >
                              {resInfo.label}
                            </span>
                          );
                        })()}
                      </div>

                      <p className="mt-2 text-sm leading-relaxed text-slate-400">
                        {getLatestCheckSummary(session)}
                      </p>
                      {session.resolution_checked_at && <p className="mt-1 text-xs text-slate-500">Checked {new Date(session.resolution_checked_at).toLocaleString()}</p>}
                      {crashMetric(session) && getAdditionalFindingStatus(session.resolution_proof) && <p className="mt-2 text-sm font-semibold text-cyan-200">Additional finding: {getAdditionalFindingStatus(session.resolution_proof)}</p>}

                      {crashMetric(session) ? (
                        <p className="mt-1 text-xs text-slate-500">
                          Checks whether Windows recorded another crash after the original check.
                        </p>
                      ) : (
                        <p className="mt-1 text-xs text-slate-500">
                          Compares a fresh device reading with this saved check.
                        </p>
                      )}

                      {getTelemetryDeltaSummary(session.proof, session.resolution_proof) && (
                        <div className="mt-3 rounded-lg border border-cyan-500/25 bg-cyan-500/[0.05] p-3 text-xs text-cyan-200">
                          <span className="font-bold text-cyan-300">Compared with the original check: </span>
                          {plainDiagnosticText(getTelemetryDeltaSummary(session.proof, session.resolution_proof))}
                        </div>
                      )}
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
                      className="inline-flex shrink-0 items-center justify-center gap-2 whitespace-nowrap rounded-lg border border-cyan-500/40 bg-cyan-500/5 px-4 py-2 text-sm font-bold text-cyan-400 hover:bg-cyan-500/10 disabled:opacity-50"
                    >
                      <RefreshCw
                        size={16}
                        className={
                          checking
                            ? 'animate-spin'
                            : ''
                        }
                      />

                      {getResolutionButtonText(
                        session.resolution_status,
                        checking,
                      )}
                    </motion.button>
                  </div>

                  {getFindingChanges(session.proof, session.resolution_proof) && <p className="mt-4 text-sm font-semibold text-cyan-200">{plainDiagnosticText(getFindingChanges(session.proof, session.resolution_proof)!.summary)}</p>}
                  <div className="mt-4"><DiagnosticReadings readings={session.resolution_proof} title="Latest saved readings" /></div>
                  {!session.resolution_proof?.length && <p className="mt-4 text-sm text-slate-400">Detailed follow-up readings were not saved for this check.</p>}
                </motion.section>
              )}

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
                          Actions already tried
                        </h3>

                        <p className="mt-2 text-sm leading-relaxed text-slate-400">
                          Previously recorded actions for this check.
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
                                  className={`rounded border px-2 py-0.5 text-xs font-bold uppercase ${
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

                                <span className="text-xs text-slate-400">
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
                                      className="border-t border-[var(--rigmd-border)] py-3"
                                    >
                                      <div className="flex flex-wrap items-center justify-between gap-2">
                                      <span className="text-xs font-semibold text-slate-300">
                                        {friendlyToolName(attempt.action_code)}
                                      </span>

                                      <span
                                        className={`rounded border px-2 py-0.5 text-xs font-bold uppercase ${
                                          attempt.verification_status ===
                                          'Resolved'
                                            ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
                                            : /^(Unresolved|Worse|Failed)$/i.test(attempt.verification_status || '')
                                              ? 'border-amber-500/30 bg-amber-500/10 text-amber-300'
                                              : 'border-slate-500/30 bg-slate-500/10 text-slate-400'
                                        }`}
                                      >
                                        {friendlyVerification(attempt.verification_status || 'Pending')}
                                      </span>
                                      </div>
                                      {attempt.execution_summary && <p className="mt-2 text-xs leading-relaxed text-slate-300">{plainDiagnosticText(attempt.execution_summary)}</p>}
                                      <ProofList execution={{ proof: attempt.proof }} />
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

              {!isNoActiveIssue(session.diagnosed_category) && (
                <motion.section
                  variants={
                    cardFadeUp
                  }
                  initial="hidden"
                  animate="visible"
                  transition={
                    cardTransition
                  }
                  className="border-t border-[var(--rigmd-border)] pt-5"
                >
                  <div className="space-y-3">
                    <AutonomyRemediationPanel
                      check={session}
                      originalReadings={session.proof}
                      onOpenMemoryCheck={onOpenMemoryCheck}
                      readings={session.resolution_proof?.length ? session.resolution_proof : session.proof}
                      followUpSummary={getFindingChanges(session.proof, session.resolution_proof)?.summary}
                      followUpCheckedAt={session.resolution_checked_at}
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
                  </div>
                </motion.section>
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
