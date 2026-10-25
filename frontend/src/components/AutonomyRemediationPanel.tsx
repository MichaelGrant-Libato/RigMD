import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { motion } from 'motion/react';
import {
  Activity,
  ArrowRight,
  AlertTriangle,
  Bot,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
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
import DiagnosticReadings from './DiagnosticReadings';
import { hardwareInspectionNotice, requiresHardwareInspection, reviewOnlyNotice, memoryActionDisposition, crashCheckNotice, type ScanScope } from '../lib/repairSupport';
import { friendlyToolName, getActionExplanation, getDiagnosticOverview, friendlyVerification,
  getSnapshotReadings, plainDiagnosticText, type DiagnosticReading } from '../lib/diagnosticLanguage';

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
  previewCloseSelectedApp,
  runAutonomyExecution,
  runAutonomyPreview,
  startRemediationStream,
  stopRemediationStream,
} from '../services/autonomyService';

interface AutonomyRemediationPanelProps {
  readings?: DiagnosticReading[];
  originalReadings?: DiagnosticReading[];
  check?: ScanScope;
  onOpenMemoryCheck?: () => void;
  followUpSummary?: string;
  followUpCheckedAt?: string | null;
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

interface RemediationToolOption {
  id: string;
  label: string;
  userLabel: string;
  reason: string;
}

const REMEDIATION_TOOL_OPTIONS: RemediationToolOption[] = [
  {
    id: 'clear_temp_files',
    label: 'Clear Temporary Files (Tier 1)',
    userLabel: 'Clear temporary files',
    reason: 'Best for storage pressure, temp-cache buildup, and slow startup cleanup.',
  },
  {
    id: 'flush_dns_cache',
    label: 'Flush DNS Cache (Tier 1)',
    userLabel: 'Refresh DNS/network name cache',
    reason: 'Best for websites failing to load, DNS failures, or connection name-resolution issues.',
  },
  {
    id: 'restart_windows_explorer',
    label: 'Restart Windows Explorer (Tier 1)',
    userLabel: 'Restart Windows Explorer',
    reason: 'Best for taskbar, desktop, display shell, or Explorer-related UI problems.',
  },
  {
    id: 'rescan_plug_and_play_devices',
    label: 'Rescan Connected Devices (Tier 2)',
    userLabel: 'Rescan connected devices',
    reason: 'Best for USB, peripheral, or Device Manager errors where Windows needs to detect the device again.',
  },
  {
    id: 'clear_browser_cache',
    label: 'Clear Browser Caches (Tier 2)',
    userLabel: 'Clear browser caches',
    reason: 'Best when browser storage or browser workload is part of the problem.',
  },
  {
    id: 'clear_windows_update_cache',
    label: 'Clear Windows Update Cache (Tier 2)',
    userLabel: 'Clear Windows Update download cache',
    reason: 'Best for storage use or Windows Update download/cache problems.',
  },
  {
    id: 'run_system_file_checker',
    label: 'Run System File Checker / SFC (Tier 2)',
    userLabel: 'Repair protected Windows system files',
    reason: 'Best for driver, display, blue-screen, or Windows integrity problems.',
  },
  {
    id: 'terminate_processes',
    label: 'Close agent-selected high-use apps (Tier 2)',
    userLabel: 'Close high-use apps selected by RigMD',
    reason: 'Best when the agent identified specific running apps that are using too much memory or CPU.',
  },
  {
    id: 'close_selected_app',
    label: 'Choose memory-heavy apps to close (User selected)',
    userLabel: 'Choose memory-heavy apps to close',
    reason: 'Best when you want to choose exactly which user apps RigMD may close.',
  },
];

const REMEDIATION_TOOL_LOOKUP = new Map(
  REMEDIATION_TOOL_OPTIONS.map((tool) => [tool.id, tool]),
);

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
  const verification = verificationState.toLowerCase();
  const state = (attemptState || verificationState).toLowerCase();
  const safety = result.safety;

  if (verification.includes('not fixed') || verification.includes('unresolved')) {
    return 'unresolved';
  }

  if (verification.includes('needs help') || verification.includes('worse')) {
    return 'failed';
  }

  if (verification.includes('could not confirm') || verification.includes('unknown')) {
    return 'info';
  }

  if (verification.includes('fixed') || verification.includes('resolved')) {
    return 'success';
  }

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
      title: 'Follow-up check passed',
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
      title: state === 'unresolved' ? 'Action completed, issue still active' : 'Needs your attention',
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

function getToolOption(toolId?: string, displayName?: string): RemediationToolOption | null {
  if (!toolId) {
    return null;
  }

  return REMEDIATION_TOOL_LOOKUP.get(toolId) ?? {
    id: toolId,
    label: displayName || toolId,
    userLabel: displayName || toolId.replace(/_/g, ' '),
    reason: 'Recommended by the agent from the live readings for this diagnosis.',
  };
}

function getToolDisplayName(toolId?: string, displayName?: string) {
  return friendlyToolName(toolId, displayName);
}

function getDiagnosisMatchedToolIds(diagnosedCategory: string) {
  const category = diagnosedCategory.toLowerCase();

  if (
    category.includes('network') ||
    category.includes('dns') ||
    category.includes('internet')
  ) {
    return ['flush_dns_cache'];
  }

  if (
    category.includes('driver') ||
    category.includes('pnp') ||
    category.includes('usb') ||
    category.includes('device manager')
  ) {
    return ['rescan_plug_and_play_devices', 'run_system_file_checker'];
  }

  if (category.includes('display')) {
    return ['restart_windows_explorer', 'rescan_plug_and_play_devices', 'run_system_file_checker'];
  }

  if (
    category.includes('blue screen') ||
    category.includes('stop error') ||
    category.includes('system crash')
  ) {
    return ['run_system_file_checker'];
  }

  if (
    category.includes('application crash') ||
    category.includes('app crash')
  ) {
    return [];
  }

  if (/memory|\bram\b/i.test(category)) return ['close_selected_app'];

  if (
    category.includes('performance') ||
    category.includes('stuttering') ||
    category.includes('freezing') ||
    category.includes('slow')
  ) {
    return ['close_selected_app', 'clear_browser_cache', 'clear_temp_files', 'restart_windows_explorer'];
  }

  if (
    category.includes('thermal') ||
    category.includes('overheat') ||
    category.includes('fan') ||
    category.includes('cpu load')
  ) {
    return ['close_selected_app'];
  }

  if (
    category.includes('storage') ||
    category.includes('disk') ||
    category.includes('temp') ||
    category.includes('cache')
  ) {
    return ['clear_temp_files', 'clear_browser_cache', 'clear_windows_update_cache'];
  }

  if (category.includes('battery') || category.includes('power')) {
    return ['close_selected_app'];
  }

  return [];
}

function getContextualToolOptions(
  diagnosedCategory: string,
  previewResult?: AutonomyResult | null,
) {
  const ids = new Set<string>();
  const proposedTool = previewResult?.proposedTool;

  if (proposedTool?.toolName) {
    ids.add(proposedTool.toolName);
  }

  for (const id of getDiagnosisMatchedToolIds(diagnosedCategory)) {
    ids.add(id);
  }

  return [...ids].filter(id => !/memory|\bram\b/i.test(diagnosedCategory) || id === 'close_selected_app')
    .map((id) => getToolOption(id, proposedTool?.toolName === id ? proposedTool.displayName : undefined))
    .filter((tool): tool is RemediationToolOption => Boolean(tool));
}

function formatStepType(stepType: string) {
  const normalized = stepType.toLowerCase();
  if (normalized.includes('thought')) {
    return 'Reasoning';
  }
  if (normalized.includes('toolcall')) {
    return 'Check';
  }
  if (normalized.includes('observation')) {
    return 'Result';
  }
  if (normalized.includes('dryrun')) {
    return 'Safety preview';
  }
  if (normalized.includes('approval')) {
    return 'Needs approval';
  }
  if (normalized.includes('execution')) {
    return 'Action';
  }
  if (normalized.includes('verification')) {
    return 'Verification';
  }
  return stepType;
}

function formatStepTitle(step: ReActTraceStep) {
  const normalized = step.stepType.toLowerCase();
  if (normalized.includes('toolcall')) {
    return `Checking ${getToolDisplayName(step.toolName)}`;
  }
  if (normalized.includes('observation')) {
    return `Result from ${getToolDisplayName(step.toolName)}`;
  }
  if (normalized.includes('dryrun')) {
    return `Safety preview for ${getToolDisplayName(step.toolName)}`;
  }
  if (normalized.includes('approval')) {
    return 'Waiting for your approval';
  }
  if (normalized.includes('thought')) {
    return 'Agent reasoning';
  }
  return step.title;
}

function formatStepContent(step: ReActTraceStep) {
  const normalized = step.stepType.toLowerCase();
  if (normalized.includes('toolcall')) {
    return `RigMD is reading ${getToolDisplayName(step.toolName)} evidence. This check does not change Windows.`;
  }
  if (normalized.includes('approval')) {
    return `${getToolDisplayName(step.toolName)} is ready to run after you review the safety preview and approve it.`;
  }
  if (normalized.includes('thought')) {
    return step.content
      .replace(/I will invoke/gi, 'RigMD will check')
      .replace(/telemetry/gi, 'device readings');
  }
  return step.content
    .replace(/telemetry/gi, 'device readings')
    .replace(/Tier 0/gi, 'read-only')
    .replace(/Tier 1/gi, 'low-risk')
    .replace(/Tier 2/gi, 'approval-required');
}

function formatCitation(citation: string) {
  const [toolName, ...rest] = citation.split(':');
  const summary = rest.join(':').trim();
  if (!summary) {
    return citation.replace(/telemetry/gi, 'device readings');
  }

  return `${getToolDisplayName(toolName.trim())}: ${summary.replace(/telemetry/gi, 'device readings')}`;
}

type SnapshotRecord = Record<string, unknown>;

function parseSnapshot(json?: string): SnapshotRecord | null {
  if (!json || json === '{}' || json === 'null') {
    return null;
  }

  try {
    const parsed = JSON.parse(json) as unknown;
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as SnapshotRecord
      : null;
  } catch {
    return null;
  }
}

function readRecord(source: SnapshotRecord | null, key: string): SnapshotRecord | null {
  const value = source?.[key];
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as SnapshotRecord
    : null;
}

function readNumber(source: SnapshotRecord | null, key: string): number | null {
  const value = source?.[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function getSnapshotFacts(toolName: string, snapshot: SnapshotRecord | null) {
  return getSnapshotReadings(toolName, snapshot).map(reading => `${reading.label}: ${reading.value}`);
}

function getUnresolvedReason(diagnosedCategory: string, result: AutonomyResult) {
  const toolName = result.verificationReport?.verificationToolName || '';
  const after = parseSnapshot(result.verificationReport?.afterSnapshotJson);
  const category = diagnosedCategory.toLowerCase();

  if (
    toolName === 'inspect_full_device_profile' ||
    category.includes('driver') ||
    category.includes('pnp') ||
    category.includes('usb') ||
    category.includes('device manager')
  ) {
    const errors = readNumber(after, 'deviceErrorsCount');
    if (errors && errors > 0) {
      return `${errors} Device Manager error${errors === 1 ? '' : 's'} still reported after the action.`;
    }
  }

  if (category.includes('memory') || category.includes('ram') || category.includes('slow') || category.includes('performance')) {
    const memory = readRecord(after, 'memory');
    const usage = readNumber(memory, 'usagePercent');
    return usage !== null
      ? `The follow-up check recorded ${usage.toFixed(1)}% memory use. Review the remaining warning.`
      : 'The follow-up check still sees performance pressure.';
  }

  if (category.includes('storage') || category.includes('disk')) {
    return 'Storage readings still need attention after the action.';
  }

  if (category.includes('network') || category.includes('dns') || category.includes('internet')) {
    return 'Network readings still show a connection or name-resolution problem.';
  }

  if (category.includes('thermal') || category.includes('overheat') || category.includes('fan')) {
    return 'The cooling check still reported a concern. Review the available temperature readings.';
  }

  if (category.includes('application crash') || category.includes('app crash') || category.includes('blue screen')) {
    return 'Windows still has stability evidence that needs review.';
  }

  return 'The follow-up Windows check still sees the original issue.';
}

function getNextSteps(diagnosedCategory: string, result: AutonomyResult) {
  const state = getState(result);
  if (state !== 'unresolved') {
    return [] as string[];
  }

  const category = diagnosedCategory.toLowerCase();

  if (category.includes('driver') || category.includes('pnp') || category.includes('usb') || category.includes('device manager')) {
    return [
      'Open Device Manager and expand the device category with the warning icon.',
      'Unplug and reconnect the affected USB or peripheral device, then recheck the current status.',
      'If the same Code 43 error remains, try another USB port or update/remove only that specific device entry in Device Manager.',
    ];
  }

  if (category.includes('network') || category.includes('dns') || category.includes('internet')) {
    return [
      'Disconnect and reconnect Wi-Fi or Ethernet, then recheck the current status.',
      'Restart the router if other devices also have connection issues.',
      'If DNS still fails, try another DNS provider or review adapter settings.',
    ];
  }

  if (category.includes('storage') || category.includes('disk') || category.includes('cache')) {
    return [
      'Review the storage details and confirm which drive is still under pressure.',
      'Move or delete large user files only after checking them yourself.',
      'If drive health is not healthy, back up important files before doing more cleanup.',
    ];
  }

  if (category.includes('memory') || category.includes('ram') || category.includes('slow') || category.includes('performance') || category.includes('stuttering') || category.includes('freezing')) {
    return [
      'Close or save work in high-memory apps, then recheck the current status.',
      'Check Task Manager for apps that returned after the action.',
      'If memory stays high after closing apps, restart Windows and retest under normal use.',
    ];
  }

  if (category.includes('thermal') || category.includes('overheat') || category.includes('fan') || category.includes('cpu load')) {
    return [
      'Let the device cool for a few minutes, then recheck the current status.',
      'Check airflow, vents, and surface placement while the device is under load.',
      'If temperature rises quickly with low workload, inspect cooling or fan behavior.',
    ];
  }

  if (category.includes('battery') || category.includes('power')) {
    return [
      'Check whether the issue happens on battery, charger, or both.',
      'Close high-use apps and recheck the current status.',
      'If battery health is poor or drain remains high, compare with Windows battery settings.',
    ];
  }

  if (category.includes('application crash') || category.includes('app crash')) {
    return [
      'Open Windows Reliability Monitor and check the latest failing app name.',
      'Update or repair the affected app, then recheck the current status.',
      'If only one app keeps crashing, focus on that app instead of changing system-wide settings.',
    ];
  }

  if (category.includes('blue screen') || category.includes('stop error') || category.includes('system crash')) {
    return [
      'Review recent Windows critical events before running another repair.',
      'Disconnect recently added hardware if the crash started after adding it.',
      'If crashes continue, collect the stop code or dump details for deeper review.',
    ];
  }

  return [
    'Review the still-failing evidence below.',
    'Recheck the current status after changing only one thing.',
    'If the same evidence remains, use the related Windows tool shown in the diagnosis.',
  ];
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
          const displayTitle = formatStepTitle(step);
          const displayContent = formatStepContent(step);

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
                    {formatStepType(step.stepType)}
                  </span>

                  <span className="font-semibold text-white">
                    {displayTitle}
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
                {displayContent}
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

function TechnicalCheckDetails({ steps, engine, analysis, citations }: {
  steps?: ReActTraceStep[]; engine?: string; analysis?: string; citations?: string[];
}) {
  return <details className="mt-4 min-w-0 border-t border-white/10 pt-3 text-xs text-slate-400">
    <summary className="cursor-pointer py-2 font-semibold text-slate-300">Technical check details</summary>
    {engine && <p className="my-2 break-words">Engine: {engine}</p>}
    {analysis && <p className="my-3 whitespace-pre-wrap break-words leading-relaxed">{analysis}</p>}
    {citations?.length ? <ul className="my-3 space-y-2 break-words">{citations.map((citation, index) => <li key={index}>{formatCitation(citation)}</li>)}</ul> : null}
    {steps?.length ? <ReActTimeline steps={steps} /> : <p className="py-2">No detailed check log was returned.</p>}
  </details>;
}

export function ProofList({ execution }: { execution?: AutonomyExecution }) {
  if (!execution?.proof?.length) {
    return null;
  }

  return (
    <div className="mt-4 grid grid-cols-1 gap-2 md:grid-cols-2">
      {execution.proof.map((item, index) => (
        <div
          key={`${item.label}-${index}`}
          className="min-w-0 border-t border-current/15 py-3"
        >
          <p className="text-[10px] font-bold uppercase tracking-wider opacity-70">
            {item.label === 'Directory size' ? 'Temporary-file storage' : item.label === 'File count' ? 'Temporary files' : plainDiagnosticText(item.label || 'Checked')}
          </p>

          {item.status && /^(App closed|App still running|App state not verified)/.test(item.status) ? (
            <>
              <p className="mt-1 text-sm font-semibold text-white">{item.status}</p>
              <details className="mt-2 text-xs leading-relaxed opacity-80">
                <summary className="cursor-pointer">App process details</summary>
                <p className="mt-1 break-words">{item.before} &rarr; {item.after}</p>
              </details>
            </>
          ) : item.before && item.after ? (
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
              {plainDiagnosticText(item.meaning)}
            </p>
          )}
        </div>
      ))}
    </div>
  );
}

function FollowUpEvidence({
  result,
}: {
  result: AutonomyResult;
}) {
  const report = result.verificationReport;
  if (!report) {
    return null;
  }

  const before = parseSnapshot(report.beforeSnapshotJson);
  const after = parseSnapshot(report.afterSnapshotJson);
  const beforeFacts = getSnapshotFacts(report.verificationToolName, before);
  const afterFacts = getSnapshotFacts(report.verificationToolName, after);

  if (beforeFacts.length === 0 && afterFacts.length === 0) {
    return (
      <div className="mt-4 border-t border-current/20 pt-3 text-xs">
        <p className="font-bold uppercase tracking-wider opacity-80">
          Follow-up check ({getToolDisplayName(report.verificationToolName)})
        </p>
        <p className="mt-1 leading-relaxed opacity-90">
          {plainDiagnosticText(report.summary).replace(/Before:/g, 'Before check:').replace(/After:/g, 'After check:')}
        </p>
      </div>
    );
  }

  return (
    <div className="mt-4 border-t border-current/20 pt-3">
      <p className="text-xs font-bold uppercase tracking-wider opacity-80">
        Follow-up check ({getToolDisplayName(report.verificationToolName)})
      </p>

      <div className="mt-2 grid grid-cols-1 gap-2 md:grid-cols-2">
        <div className="min-w-0 py-2">
          <p className="text-[10px] font-bold uppercase tracking-wider opacity-70">
            Before action
          </p>
          <ul className="mt-1 space-y-1 text-xs leading-relaxed opacity-90">
            {(beforeFacts.length ? beforeFacts : ['No baseline reading available']).map((fact) => (
              <li key={fact}>{fact}</li>
            ))}
          </ul>
        </div>

        <div className="rounded-md border border-current/15 bg-black/20 p-2.5">
          <p className="text-[10px] font-bold uppercase tracking-wider opacity-70">
            After action
          </p>
          <ul className="mt-1 space-y-1 text-xs leading-relaxed opacity-90">
            {(afterFacts.length ? afterFacts : ['No follow-up reading available']).map((fact) => (
              <li key={fact}>{fact}</li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

function NextStepsPanel({
  diagnosedCategory,
  result,
}: {
  diagnosedCategory: string;
  result: AutonomyResult;
}) {
  const steps = getNextSteps(diagnosedCategory, result);
  if (steps.length === 0) {
    return null;
  }

  return (
    <div className="mt-3 rounded-lg border border-current/20 bg-black/20 p-3">
      <p className="text-xs font-bold uppercase tracking-wider opacity-80">
        What to try next
      </p>
      <ol className="mt-2 list-decimal space-y-1.5 pl-4 text-xs leading-relaxed opacity-90">
        {steps.map((step) => (
          <li key={step}>{step}</li>
        ))}
      </ol>
    </div>
  );
}

function ResultCard({
  result,
  diagnosedCategory,
  followUpSummary,
}: {
  result: AutonomyResult;
  diagnosedCategory: string;
  followUpSummary?: string;
}) {
  const state = followUpSummary ? 'info' : getState(result);
  const attempt = getLatestAttempt(result);
  const statusLabel = followUpSummary ? 'Follow-up checked' : friendlyVerification(result.verification ?? attempt?.verification ?? getAttemptStateLabel(attempt?.state));
  const verifiedResolved = statusLabel === 'Issue no longer detected';
  const style = getStatusStyle(state === 'success' && !verifiedResolved ? 'info' : state);
  const Icon = style.icon;
  const summary =
    state === 'unresolved'
      ? 'The action ran, but the follow-up Windows check still sees the issue.'
      : state === 'success'
        ? verifiedResolved ? 'The follow-up check no longer reported this issue. A problem that comes and goes may still need another check.' : 'The action finished. Its effect on the original issue has not been confirmed.'
        : result.execution?.success === true
          ? 'The action finished, but the follow-up check could not confirm whether the issue improved.'
          : null;
  const displaySummary =
    followUpSummary ||
    (result.execution?.success === true && result.verificationReport?.verificationToolName === 'query_windows_event_logs'
      ? result.verificationReport.summary : null) ||
    summary ||
    attempt?.notes ||
    result.execution?.summary ||
    result.safety?.rejectionReason ||
    'RigMD finished checking this action.';
  const unresolvedReason = state === 'unresolved'
    ? getUnresolvedReason(diagnosedCategory, result)
    : '';

  return (
    <div className="space-y-3">
      {result.persistenceWarning && <p role="alert" className="rounded-md border border-amber-400/40 bg-amber-400/10 p-3 text-sm text-amber-200">{result.persistenceWarning}</p>}
      <div className={`rounded-xl border p-4 text-sm ${style.className}`}>
        <div className="flex items-start gap-3">
          <Icon size={18} className="mt-0.5 shrink-0" />

          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-xs font-bold uppercase tracking-wider">
                {followUpSummary ? 'Latest follow-up' : state === 'info' && statusLabel === 'Could not verify' ? statusLabel : state === 'success' && !verifiedResolved ? 'Action finished' : style.title}
              </p>

            </div>

            <p className="mt-2 leading-relaxed">
              {plainDiagnosticText(displaySummary)}
            </p>

            {unresolvedReason && (
              <div className="mt-3 border-t border-current/20 pt-3 text-xs leading-relaxed">
                <p className="font-bold uppercase tracking-wider opacity-80">
                  Why it is still active
                </p>
                <p className="mt-1 opacity-90">
                  {unresolvedReason}
                </p>
              </div>
            )}

            {!followUpSummary && <FollowUpEvidence result={result} />}
            {!followUpSummary && <NextStepsPanel diagnosedCategory={diagnosedCategory} result={result} />}

            <span className="mt-3 inline-flex rounded-full border border-current/30 px-2.5 py-1 text-[10px] font-bold uppercase">
              {statusLabel}
            </span>
          </div>
        </div>

        <ProofList execution={result.execution || attempt?.execution} />
      </div>

      <TechnicalCheckDetails steps={result.reasoningSteps} engine={result.engineMode}
        analysis={[result.rootCauseAnalysis, result.execution?.summary, attempt?.notes].filter(Boolean).join('\n')} />
    </div>
  );
}

export default function AutonomyRemediationPanel({
  readings = [],
  originalReadings,
  check,
  onOpenMemoryCheck,
  sessionId,
  diagnosedCategory,
  onExecutionComplete,
  followUpSummary,
  followUpCheckedAt,
}: AutonomyRemediationPanelProps) {
  const memoryDisposition = memoryActionDisposition(check ?? { diagnosed_category: diagnosedCategory }, readings);
  const memoryAction = memoryDisposition === 'direct';
  const actionCategory = memoryAction ? 'High Memory Pressure' : diagnosedCategory;
  const [executedAt, setExecutedAt] = useState<number | null>(null);
  const [previewResult, setPreviewResult] =
    useState<AutonomyResult | null>(null);

  const [executionResult, setExecutionResult] =
    useState<AutonomyResult | null>(null);

  const [liveSteps, setLiveSteps] =
    useState<ReActTraceStep[]>([]);

  const [liveProgressLine, setLiveProgressLine] =
    useState<string | null>(null);

  const [activeToolId, setActiveToolId] =
    useState<string>('');

  const [customDryRun, setCustomDryRun] =
    useState<ToolDryRunPreview | null>(null);
  const previewSequence = useRef(0);

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
  const reviewDialog = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!reviewOpen) return;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    reviewDialog.current?.focus();
    const keepFocusInReview = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return;
      const controls = Array.from(reviewDialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), summary, [tabindex="0"]') ?? [])
        .filter(control => control.getClientRects().length > 0);
      const first = controls[0];
      const last = controls[controls.length - 1];
      if (!first || !last) { event.preventDefault(); return; }
      if (event.shiftKey && (document.activeElement === first || document.activeElement === reviewDialog.current)) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === reviewDialog.current)) {
        event.preventDefault(); first.focus();
      }
    };
    document.addEventListener('keydown', keepFocusInReview);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', keepFocusInReview);
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, [reviewOpen]);

  const isCloseSelectedAppMode = activeToolId === 'close_selected_app';

  const primaryAction = getPrimaryAction(previewResult);

  const effectiveDryRun: ToolDryRunPreview | undefined =
    isCloseSelectedAppMode ? customDryRun ?? undefined : customDryRun ??
    previewResult?.dryRunPreview ??
    previewResult?.proposedTool?.dryRunPreview;

  const dynamicTitle = isCloseSelectedAppMode
    ? 'Close selected memory-heavy apps'
    : getToolDisplayName(
      activeToolId,
      effectiveDryRun?.displayName ||
      previewResult?.proposedTool?.displayName ||
      primaryAction?.name,
    ) ||
      'Review proposed action';

  const dynamicDescription = isCloseSelectedAppMode
    ? 'Select which running applications RigMD should close to reduce live memory pressure.'
    : (effectiveDryRun?.whatWillHappen ||
      primaryAction?.description ||
      previewResult?.plan?.strategyReasoning ||
      'Review the proposed action before execution.')
      .replace(/telemetry/gi, 'device readings')
      .replace(/Tier 1/gi, 'low-risk')
      .replace(/Tier 2/gi, 'approval-required');
  const actionExplanation = getActionExplanation(activeToolId);

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
    !previewError &&
    Boolean(effectiveDryRun?.approvalToken && effectiveDryRun.toolName === activeToolId) &&
    (effectiveDryRun?.canExecute ?? true) &&
    (!isCloseSelectedAppMode || selectedProcessNames.length > 0);

  const selectedMemoryApps =
    memoryApps.filter((app) => selectedProcessNames.includes(app.name));

  const selectedAppNames =
    selectedMemoryApps.map((app) => app.displayName || app.name);

  const contextualToolOptions = useMemo(
    () => getContextualToolOptions(actionCategory, previewResult).filter(option => option.id !== 'close_selected_app' || memoryAction),
    [actionCategory, previewResult, memoryAction],
  );

  const activeToolOption = getToolOption(
    activeToolId,
    previewResult?.proposedTool?.toolName === activeToolId
      ? previewResult.proposedTool.displayName
      : undefined,
  );

  const handleToolSelectionChange = async (nextToolId: string) => {
    const sequence = ++previewSequence.current;
    setActiveToolId(nextToolId);
    setIsPreviewLoading(false);
    setUserConsentProvided(false);
    setCustomDryRun(null);
    setPreviewError(null);

    if (nextToolId === previewResult?.proposedTool?.toolName) {
      setCustomDryRun(null);
      return;
    }

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
      setIsPreviewLoading(true);
      const preview = await previewAgentTool(nextToolId, {}, sessionId);
      if (sequence === previewSequence.current) setCustomDryRun(preview);
    } catch (error) {
      if (sequence === previewSequence.current) setPreviewError(getBackendErrorMessage(error));
    } finally {
      if (sequence === previewSequence.current) setIsPreviewLoading(false);
    }
  };

  const handleAppSelectionChange = async (nextNames: string[]) => {
    const sequence = ++previewSequence.current;
    setSelectedProcessNames(nextNames);
    setUserConsentProvided(false);
    setCustomDryRun(null);
    setPreviewError(null);
    if (nextNames.length === 0) return;
    setIsPreviewLoading(true);
    try {
      const preview = await previewCloseSelectedApp(sessionId, nextNames);
      if (sequence === previewSequence.current) setCustomDryRun(preview);
    } catch (error) {
      if (sequence === previewSequence.current) setPreviewError(getBackendErrorMessage(error));
    } finally {
      if (sequence === previewSequence.current) setIsPreviewLoading(false);
    }
  };

  const openReview = async () => {
    if (requestActive || !sessionId || !diagnosedCategory) {
      return;
    }

    setIsPreviewLoading(true);
    previewSequence.current++;
    setPreviewError(null);
    setExecuteError(null);
    setExecutionResult(null);
    setUserConsentProvided(false);
    setSelectedProcessNames([]);
    setCustomDryRun(null);
    setLiveSteps([]);
    setLiveProgressLine('Starting ReAct diagnostic tool investigation...');

    if (memoryAction) {
      try {
        setMemoryApps(await getMemoryAppCandidates());
        setPreviewResult({ rootCauseAnalysis: 'Review apps using memory. Nothing will close until you select apps and approve the action.' });
        setActiveToolId('close_selected_app');
        setReviewOpen(true);
      } catch (error) {
        setPreviewError(getBackendErrorMessage(error));
      } finally {
        setIsPreviewLoading(false);
        setLiveProgressLine(null);
      }
      return;
    }

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

      const memoryFinding = /memory|\bram\b/i.test(diagnosedCategory);
      const recommendedTool = memoryFinding ? 'close_selected_app' :
        result.proposedTool?.toolName ||
        result.plan?.plannedActions?.[0]?.id ||
        (diagnosedCategory.toLowerCase().includes('memory') && apps.length > 0
          ? 'close_selected_app'
          : getDiagnosisMatchedToolIds(diagnosedCategory)[0] ?? '');

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
            sessionId,
            approvalToken: effectiveDryRun?.approvalToken,
            processNames: selectedProcessNames,
            confirmed: true,
          })
        : await runAutonomyExecution({
            approvalToken: effectiveDryRun?.approvalToken,
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
      setExecutedAt(Date.now());
      setReviewOpen(false);
      setUserConsentProvided(false);
      onExecutionComplete?.(result);
    } catch (error) {
      setExecuteError(getBackendErrorMessage(error));
      setUserConsentProvided(false);
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
  const observations = new Map<string, ReturnType<typeof getSnapshotReadings>>();
  for (const step of displayedSteps) {
    if (step.toolName && step.observationJson) {
      const readings = getSnapshotReadings(step.toolName, step.observationJson);
      if (readings.length) observations.set(step.toolName, readings);
    }
  }
  const reviewReadings = memoryAction ? readings.filter(row => row.metric_id === 'memory_usage' || row.metric_id === 'memory_app_warning')
    : [...observations.values()].flat();

  if (requiresHardwareInspection(diagnosedCategory)) return (
    <section aria-label="Hardware inspection recommended" className="border-l-2 border-amber-400 pl-4 py-3">
      <div className="flex items-center gap-2 text-amber-200"><AlertTriangle size={18} /><h5 className="text-sm font-semibold">Hardware inspection recommended</h5></div>
      <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">{hardwareInspectionNotice}</p>
    </section>
  );

  const reviewNotice = reviewOnlyNotice(diagnosedCategory);
  const crashContext = /crash|stability risk|stop error/i.test(diagnosedCategory) ||
    /^(app-crashes|application-crashes|blue-screen-crash|blue-screen|bsod|system-crashes)$/i.test(check?.scenario_id ?? '');
  const crashNotice = crashCheckNotice(originalReadings ?? readings, originalReadings === readings ? undefined : readings);
  if (memoryDisposition === 'handoff') return (
    <div className="space-y-4">
      {crashContext && <section aria-label="About the crash check" className="border-l-2 border-sky-400 py-2 pl-4">
        <h5 className="text-sm font-semibold text-sky-200">About the crash check</h5>
        <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">{crashNotice}</p>
      </section>}
      <details className="border-t border-[var(--rigmd-border)] py-3" aria-label="Separate memory finding">
        <summary className="cursor-pointer text-sm font-semibold text-amber-200">Other findings: Memory warning needs review</summary>
        <p className="mt-3 max-w-3xl text-sm leading-relaxed text-slate-300">
          Memory is a separate finding. Its relationship to the selected problem has not been established.
        </p>
        <button type="button" onClick={onOpenMemoryCheck} disabled={!onOpenMemoryCheck}
          className="mt-3 inline-flex items-center gap-2 rounded-lg border border-cyan-400/35 bg-cyan-400/10 px-4 py-2.5 text-xs font-bold text-cyan-200 disabled:opacity-50">
          <ArrowRight size={14} /> Open memory check
        </button>
      </details>
    </div>
  );
  const inactiveMemory = /memory|\bram\b/i.test(diagnosedCategory) && memoryDisposition === 'none';
  if ((reviewNotice || inactiveMemory) && !memoryAction) return (
    <section aria-label="Investigation recommended" className="border-l-2 border-sky-400 pl-4 py-3">
      <h5 className="text-sm font-semibold text-sky-200">About this check</h5>
      <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">{crashContext ? crashNotice : inactiveMemory
        ? 'The latest saved readings do not show a verified memory warning. No app-closing action is available. Check current status if symptoms continue.' : reviewNotice}</p>
      {executionResult && <div className="mt-4"><ResultCard result={executionResult} diagnosedCategory="High Memory Pressure"
        followUpSummary={executedAt !== null && followUpCheckedAt && Date.parse(followUpCheckedAt) >= executedAt ? followUpSummary : undefined} /></div>}
    </section>
  );

  return (
    <>
      {reviewNotice && memoryAction && <section aria-label="About the crash check" className="mb-5 border-l-2 border-sky-400 py-2 pl-4">
        <h5 className="text-sm font-semibold text-sky-200">About the crash check</h5>
        <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">{crashNotice}</p>
      </section>}
      <motion.section
        variants={cardFadeUp}
        initial="hidden"
        animate="visible"
        transition={cardTransition}
        className="min-w-0 space-y-4"
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
                  {memoryAction ? 'Help with memory use' : 'Review a possible next step'}
                </h5>
              </div>

              <p className="mt-1 max-w-2xl text-xs leading-relaxed text-slate-400">
                {memoryAction ? 'Review running apps and choose only ones you no longer need. Save your work first. Closing selected apps may reduce memory use; improvement must be checked afterward.' : 'No changes are made during the check. Any proposed action needs your approval.'}
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

            {isPreviewLoading ? 'Checking your device...' : memoryAction ? 'Review apps using memory' : 'Check possible actions'}
          </motion.button>
        </div>

        {isPreviewLoading && (
          <div className="mt-4 space-y-2 rounded-lg border border-cyan-400/25 bg-cyan-400/5 p-3">
            <div className="flex items-center gap-2 text-xs text-cyan-200">
              <Terminal size={14} className="animate-pulse" />
              <span>
                Checking your device. No changes are being made.
              </span>
            </div>
            {liveSteps.length > 0 && <TechnicalCheckDetails steps={liveSteps} />}
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
            <ResultCard result={executionResult} diagnosedCategory={actionCategory}
              followUpSummary={executedAt !== null && followUpCheckedAt && Date.parse(followUpCheckedAt) >= executedAt ? followUpSummary : undefined} />
          </div>
        )}
      </motion.section>

      {reviewOpen && createPortal(
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 px-4 py-6">
          <motion.div
            ref={reviewDialog}
            tabIndex={-1}
            role="dialog"
            aria-modal="true"
            aria-labelledby="action-review-title"
            initial={{ opacity: 0, y: 12, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.98 }}
            className="max-h-[92vh] w-full min-w-0 max-w-3xl overflow-y-auto rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-5 shadow-2xl [overflow-wrap:anywhere]"
          >
            <div className="flex items-start justify-between gap-4">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-xs font-bold uppercase tracking-wider text-cyan-300">
                    Review before making changes
                  </p>
                </div>

                <h3 id="action-review-title" className="mt-2 text-xl font-bold text-white">
                  {dynamicTitle}
                </h3>

                <p className="mt-2 text-sm leading-relaxed text-slate-300">
                  {actionExplanation.description}
                </p>
              </div>

              <button
                type="button"
                onClick={() => setReviewOpen(false)}
                disabled={requestActive}
                aria-label="Close review"
                title="Close review"
                className="rounded-lg border border-[var(--rigmd-border)] p-2 text-slate-400 hover:text-white"
              >
                <X size={16} />
              </button>
            </div>

            <div className="mt-5 space-y-4">
              {(previewError || executeError) && (
                <p role="alert" className="break-words rounded-lg border border-red-400/30 p-3 text-sm text-red-200">
                  {previewError || executeError}
                </p>
              )}
              <DiagnosticReadings readings={reviewReadings} technicalDetails={false} />
              {!reviewReadings.length && <p className="text-sm leading-relaxed text-slate-300">{getDiagnosticOverview(diagnosedCategory).meaning}</p>}

              {/* 3. Tool Selection & Live Dry-Run Impact Metrics */}
              <section className="border-t border-white/10 pt-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="flex items-center gap-2">
                    <Wrench size={15} className="text-emerald-300" />
                    <h4 className="text-xs font-bold uppercase tracking-wider text-emerald-200">
                      What will change
                    </h4>
                  </div>

                  <select
                    aria-label="Action to review"
                    value={activeToolId}
                    onChange={(e) => handleToolSelectionChange(e.target.value)}
                    disabled={requestActive}
                    className="w-full min-w-0 max-w-full rounded-lg border border-white/15 bg-[#101821] px-3 py-1.5 text-xs font-semibold text-white focus:border-cyan-400 focus:outline-none sm:w-auto sm:max-w-[65%]"
                  >
                    {contextualToolOptions.map((opt) => (
                      <option key={opt.id} value={opt.id}>
                        {friendlyToolName(opt.id, opt.userLabel)}
                      </option>
                    ))}
                  </select>
                </div>

                {effectiveDryRun && !isCloseSelectedAppMode && (
                  <div className="mt-3 grid grid-cols-1 gap-2.5 sm:grid-cols-3">
                    <div className="min-w-0 py-2">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        {actionExplanation.itemsLabel}
                      </p>
                      <p className="mt-1 text-base font-bold text-white">
                        {effectiveDryRun.affectedItemsCount.toLocaleString()}
                      </p>
                    </div>

                    <div className="min-w-0 py-2">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        {actionExplanation.bytesLabel}
                      </p>
                      <p className="mt-1 text-base font-bold text-cyan-300">
                        {effectiveDryRun.estimatedBytesKnown === false || activeToolId === 'run_system_file_checker' ? 'Not estimated' : formatBytes(effectiveDryRun.estimatedBytesAffected)}
                      </p>
                    </div>

                    <div className="min-w-0 py-2">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        Permission needed
                      </p>
                      <p className="mt-1 text-xs font-bold text-white">
                        {effectiveDryRun.requiresAdmin
                          ? effectiveDryRun.isRunningAsAdmin
                            ? 'Administrator access is active'
                            : 'Windows administrator approval needed'
                          : 'No administrator access required'}
                      </p>
                    </div>
                  </div>
                )}

                <p className="mt-3 text-sm leading-relaxed text-amber-100">{actionExplanation.caution}</p>
                {effectiveDryRun &&
                  !isCloseSelectedAppMode && (
                    <details className="mt-3 min-w-0 border-t border-white/10 pt-2 text-xs text-slate-400">
                      <summary className="cursor-pointer py-2 font-semibold text-slate-300">Exact action details and affected locations</summary>
                      <p className="my-2 whitespace-pre-wrap break-words leading-relaxed">{dynamicDescription}</p>
                      <ul className="mt-1.5 space-y-1 font-mono text-xs text-slate-300">
                        {effectiveDryRun.affectedTargets?.map((t, idx) => (
                          <li className="break-words [overflow-wrap:anywhere]" key={idx}>&bull; {t}</li>
                        ))}
                      </ul>
                    </details>
                  )}
              </section>

              {/* 4. Safety & Warnings */}
              <div className="rounded-xl border border-amber-400/20 bg-amber-400/[0.05] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-amber-200">
                  Before you continue
                </h4>

                <ul className="mt-2.5 space-y-1.5 text-sm text-slate-300">
                  {dynamicWarnings.map((item) => (
                    <li key={item}>
                      - {plainDiagnosticText(item)}
                    </li>
                  ))}
                </ul>
              </div>
              <TechnicalCheckDetails steps={displayedSteps} engine={previewResult?.engineMode}
                analysis={previewResult?.rootCauseAnalysis} citations={previewResult?.proposedTool?.evidenceCitations} />
            </div>

            {isCloseSelectedAppMode && (
              <div className="mt-4 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-4">
                <h4 className="text-xs font-bold uppercase tracking-wider text-white">
                  Choose apps to close
                </h4>

                <p className="mt-2 text-xs leading-relaxed text-slate-500">
                  Save your work first. Closing an app may lose unsaved changes. Protected Windows, security, and RigMD apps cannot be selected.
                </p>

                {memoryApps.length === 0 ? (
                  <p className="mt-3 rounded-lg border border-amber-400/25 bg-amber-400/[0.06] p-3 text-xs leading-relaxed text-amber-100">
                    RigMD does not see a memory-heavy user app (&gt;100 MB) that is eligible to close
                    right now. Other actions may have different effects; clearing files is not the same as closing apps.
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
                            disabled={requestActive}
                            onChange={() =>
                              void handleAppSelectionChange(
                                selected
                                  ? selectedProcessNames.filter((name) => name !== app.name)
                                  : [...selectedProcessNames, app.name],
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
                              {app.processCount} background part{app.processCount === 1 ? '' : 's'} of this app may also close.
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

                {activeToolOption?.reason && (
                  <p className="mt-3 rounded-lg border border-emerald-400/15 bg-emerald-400/[0.04] p-3 text-xs leading-relaxed text-emerald-100/90">
                    {activeToolOption.reason}
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
                I understand what will change and approve this action.
                <span className="mt-1 block text-xs text-amber-100/70">
                  This action runs only after approval and pressing the action button.
                </span>
              </span>
            </label>

            {isExecuteLoading && liveProgressLine && (
              <div className="mt-3 flex items-center gap-2 rounded-lg border border-cyan-400/30 bg-cyan-400/10 p-3 font-mono text-xs text-cyan-200">
                <Activity size={14} className="animate-spin" />
                <span>Running the approved action and checking the result...</span>
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
                  ? 'Running and checking...'
                  : effectiveDryRun && !effectiveDryRun.canExecute
                    ? 'Action unavailable - review warnings'
                    : isCloseSelectedAppMode && selectedProcessNames.length === 0
                      ? 'Choose at least one app'
                      : userConsentProvided
                        ? isCloseSelectedAppMode ? 'Close selected apps' : 'Run this action'
                        : 'Approval required'}
              </motion.button>
            </div>
          </motion.div>
        </div>, document.body
      )}
    </>
  );
}
