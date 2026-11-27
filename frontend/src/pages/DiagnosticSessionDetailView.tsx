import { afterScreenPaint, finishScanMeasurement } from '../lib/performanceRecording';
import { unavailableReadingCount } from '../lib/performanceMetrics';
import CheckReadings from '../components/CheckReadings';
import FollowUpHistory from '../components/FollowUpHistory';
import type { FollowUpObservation } from '../lib/followUpHistory';
import ProblemFeedback from '../components/ProblemFeedback';
import CheckNextSteps from '../components/CheckNextSteps';
import ResultDetails from '../components/ResultDetails';
import { getCheckOverview, getCheckAdvice, getCheckEvidenceLabel, getCheckCoverageNotice } from '../lib/checkPresentation';
import { getFindingChanges } from '../lib/findingChanges';
import { explanationSourceLabel, explanationStatusLabel } from '../lib/diagnosticReadingVisuals';
import { plainDiagnosticText, friendlyActionCategory, friendlyToolName, friendlyVerification } from '../lib/diagnosticLanguage';
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from 'react';

import { motion } from 'motion/react';

import {
  ArrowLeft,
  CheckCircle2,
  Clock3,
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
import { BatteryHealthPanel, BootHistoryPanel, DriveHealthPanel, RecentChangesPanel, RecordedCrashEventsPanel } from '../components/DeviceDiagnosticPanels';

import type { AutonomyResult } from '../services/autonomyService';
import {
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
  follow_up_history?: FollowUpObservation[];
  follow_up_history_status?: string;
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
  explanation_status?: string;
  drive_health_targets?: Array<{ number: number; model: string; volumes: string[] }>;
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
  backLabel?: string;
  onOpenMemoryCheck?: () => void;
  onOpenSession?: (id: string) => void;
}

const functionalChecks = new Map([
  ['os_integrity', 'Use the Windows check to review available system-file actions. Review the preview before approving a repair.'],
  ['audio_playback', 'Open Windows Settings → System → Sound. Select the affected device and test playback or microphone input.'],
  ['peripheral_function', 'Try the affected device in another known-working port. Retest the device with a known-working cable when applicable.'],
  ['display_picture', 'Open Windows Settings → System → Display. Confirm the affected screen shows a picture and check the input and cable.'],
  ['fan_operation', 'Check that the vents are clear and review the device maker’s cooling guidance if the symptom persists.'],
]);









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
  backLabel = 'Back to History',
  onOpenMemoryCheck,
  onOpenSession,
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

  const waitingForExplanation = session?.session_id === sessionId && session.explanation_status === 'pending';
  const functionalReadings = session?.proof?.filter(reading => reading.status === 'unknown' && functionalChecks.has(reading.metric_id ?? '')) ?? [];
  const [explanationReadError, setExplanationReadError] = useState<string | null>(null);
  useEffect(() => {
    if (!waitingForExplanation) return;
    let disposed = false;
    let timer: ReturnType<typeof setTimeout>;
    const deadline = Date.now() + 32_000;
    async function loadExplanation() {
      try {
        const response = await apiFetch(`/api/diagnosis/sessions/${sessionId}/explanation`, { method: 'POST' });
        if (disposed) return;
        if (response.status === 409 && Date.now() < deadline) {
          timer = setTimeout(() => void loadExplanation(), 1100);
          return;
        }
        if (!response.ok) throw new Error('Cloud explanation could not be read.');
        const next: Pick<SessionDetail, 'ai_explanation' | 'explanation_source' | 'explanation_model' | 'explanation_status'> = await response.json();
        if (!disposed) setSession(current => current?.session_id === sessionId ? { ...current, ...next } : current);
      } catch {
        if (!disposed) setExplanationReadError(sessionId);
      }
    }
    timer = setTimeout(() => void loadExplanation(), 150);
    return () => {
      disposed = true;
      clearTimeout(timer);
    };
  }, [sessionId, waitingForExplanation]);

  useEffect(() => {
    if (loading) return;
    return afterScreenPaint(() => finishScanMeasurement(sessionId, unavailableReadingCount(session?.proof), !session || !!error));
  }, [loading, session, error, sessionId]);

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
                  follow_up_history: resolutionData.follow_up_history,
                  follow_up_history_status: resolutionData.follow_up_history_status,
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
                  follow_up_history: data.follow_up_history,
                  follow_up_history_status: data.follow_up_history_status,
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

  const resultScroll = useRef<HTMLDivElement>(null);
  const loadedSessionId = session?.session_id;
  useLayoutEffect(() => {
    if (!loadedSessionId) return;
    window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
    resultScroll.current?.scrollTo({ top: 0, behavior: 'instant' });
  }, [loadedSessionId]);

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
        ref={resultScroll}
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

            {backLabel}
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
              <CheckNextSteps check={session} checking={checking} onCheckAgain={checkResolution} onOpenSession={onOpenSession}>
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
                      compact
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
              </CheckNextSteps>
              <ProblemFeedback key={`feedback-${sessionId}`} sessionId={sessionId} />
              <FollowUpHistory key={`rechecks-${sessionId}`} observations={session.follow_up_history} status={session.follow_up_history_status} />
              {(session.scenario_id === 'slow-boot' || parseComponentIds(session.component_ids).includes('startup')) &&
                <BootHistoryPanel key={`boot-${sessionId}`} />}
              {(session.scenario_id === 'rapid-battery-drain' || parseComponentIds(session.component_ids).includes('battery')) &&
                session.component_status !== 'NotPresent' && <BatteryHealthPanel key={`battery-${sessionId}`} />}
              {session.proof?.some(reading => ['storage_usage', 'storage_health'].includes(reading.metric_id ?? '') && reading.status === 'high') &&
                (session.drive_health_targets?.length
                  ? session.drive_health_targets.map(drive => <DriveHealthPanel key={`${sessionId}-${drive.number}`}
                      sessionId={sessionId} number={drive.number} model={drive.model} volumes={drive.volumes} />)
                  : <p className="text-sm text-slate-400">Open My Device Info → Storage to choose a drive and read its current health counters.</p>)}
              {session.proof?.some(reading => ['application_crash_history', 'system_crash_history'].includes(reading.metric_id ?? '')) &&
                <RecordedCrashEventsPanel key={`crashes-${sessionId}`} sessionId={sessionId} />}
              <RecentChangesPanel sessionId={sessionId} />
              {functionalReadings.length > 0 &&
                <details className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                  <summary className="cursor-pointer font-semibold text-slate-200">Checks you can complete</summary>
                  <ul className="mt-3 space-y-3 text-sm">
                    {functionalReadings.map(reading => <li key={reading.metric_id}>
                        <span className="font-medium text-slate-100">{reading.label}</span>
                        <p className="text-slate-300">{functionalChecks.get(reading.metric_id ?? '')}</p>
                      </li>)}
                  </ul>
                </details>}
              <ResultDetails label="Original check" title="Original check">
              <div data-testid="original-check-details">
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
                  {explanationStatusLabel(session.explanation_status) && <p role="status" className="mt-1 text-sm text-slate-300">
                    {explanationReadError === sessionId && session.explanation_status === 'pending'
                      ? 'Could not retrieve the updated explanation status.'
                      : explanationStatusLabel(session.explanation_status)}
                  </p>}

                  <p className="mt-2 break-words text-sm text-slate-300">{session.diagnosed_category}: {session.primary_result}</p>
                  <p className="mt-2 text-sm leading-relaxed text-slate-300">
                    {session.ai_explanation ||
                      'No explanation saved for this session.'}
                  </p>
                </details>
              </motion.section>

              </div>
              </ResultDetails>

              {session.remediation_history &&
                session
                  .remediation_history
                  .length >
                  0 && (
                  <ResultDetails label={`Past actions (${session.remediation_history.length})`} title="Past actions" icon={Clock3}>
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
                  </ResultDetails>
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
