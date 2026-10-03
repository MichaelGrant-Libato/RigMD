import { useMemo, useState } from 'react';
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
  if (toolId === 'inspect_full_device_profile') {
    return 'Device Manager and hardware check';
  }
  if (toolId === 'inspect_gpu_and_displays') {
    return 'graphics and display check';
  }
  if (toolId === 'inspect_memory_and_processes') {
    return 'memory and running apps check';
  }
  if (toolId === 'inspect_cpu_and_thermals') {
    return 'processor and temperature check';
  }
  if (toolId === 'inspect_storage_health') {
    return 'storage health check';
  }
  if (toolId === 'inspect_network_connectivity') {
    return 'network connection check';
  }
  if (toolId === 'inspect_battery_and_power') {
    return 'battery and power check';
  }
  if (toolId === 'query_windows_event_logs') {
    return 'Windows error history check';
  }

  return getToolOption(toolId, displayName)?.userLabel || 'this tool';
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
    return ['run_system_file_checker', 'restart_windows_explorer'];
  }

  if (
    category.includes('memory') ||
    category.includes('ram') ||
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

  if (ids.size === 0) {
    ids.add('clear_temp_files');
  }

  return [...ids]
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

function readString(source: SnapshotRecord | null, key: string): string | null {
  const value = source?.[key];
  return typeof value === 'string' && value.trim() ? value : null;
}

function formatMetric(value: number | null, suffix = '') {
  return value === null ? 'Not available' : `${value.toFixed(value % 1 === 0 ? 0 : 1)}${suffix}`;
}

function getSnapshotFacts(toolName: string, snapshot: SnapshotRecord | null) {
  if (!snapshot) {
    return [] as string[];
  }

  const normalized = toolName.toLowerCase();

  if (normalized === 'inspect_full_device_profile') {
    const gpu = readRecord(snapshot, 'gpu');
    return [
      `Device Manager errors: ${readNumber(snapshot, 'deviceErrorsCount') ?? 0}`,
      `Graphics: ${readString(gpu, 'name') ?? 'Detected'}${readString(gpu, 'driver') ? `, driver ${readString(gpu, 'driver')}` : ''}`,
      `Connected displays: ${readNumber(snapshot, 'connectedDisplays') ?? 0}`,
    ];
  }

  if (normalized === 'inspect_memory_and_processes') {
    const memory = readRecord(snapshot, 'memory');
    const browser = readRecord(snapshot, 'browserSummary');
    return [
      `Memory use: ${formatMetric(readNumber(memory, 'usagePercent'), '%')}`,
      `Browser workload: ${formatMetric(readNumber(browser, 'browserMemoryMb'), ' MB')}`,
    ];
  }

  if (normalized === 'inspect_cpu_and_thermals') {
    const cpu = readRecord(snapshot, 'cpu');
    return [
      `Processor load: ${formatMetric(readNumber(cpu, 'usagePercent'), '%')}`,
      `Temperature: ${formatMetric(readNumber(cpu, 'temperatureCelsius'), '°C')}`,
    ];
  }

  if (normalized === 'inspect_storage_health') {
    const primary = readRecord(snapshot, 'primaryVolume') || readRecord(snapshot, 'volume');
    const smart = readString(snapshot, 'smartStatus') || readString(primary, 'smartStatus') || 'Reported by Windows';
    return [
      `Drive use: ${formatMetric(readNumber(primary, 'usagePercent'), '%')}`,
      `Drive health: ${smart}`,
    ];
  }

  if (normalized === 'inspect_network_connectivity') {
    const connectivity = readRecord(snapshot, 'connectivity') || snapshot;
    return [
      `Ping: ${formatMetric(readNumber(connectivity, 'latencyMs'), ' ms')}`,
      `Packet loss: ${formatMetric(readNumber(connectivity, 'packetLossPercent'), '%')}`,
    ];
  }

  if (normalized === 'inspect_battery_and_power') {
    const battery = readRecord(snapshot, 'battery') || snapshot;
    return [
      `Battery: ${formatMetric(readNumber(battery, 'chargePercent'), '%')}`,
      `Power state: ${readString(battery, 'powerLineStatus') || readString(snapshot, 'activePowerPlan') || 'Detected'}`,
    ];
  }

  if (normalized === 'query_windows_event_logs') {
    const focus = readString(snapshot, 'eventFocus')?.toLowerCase() || '';
    const eventLabel = focus.includes('applicationcrash')
      ? 'Application crash/fault events'
      : focus.includes('bugcheck')
        ? 'System crash/stop-error events'
        : focus.includes('boot')
          ? 'Boot/startup events'
          : 'Recent Windows events';

    return [
      `${eventLabel}: ${readNumber(snapshot, 'eventCount') ?? readNumber(snapshot, 'count') ?? 'Detected'}`,
    ];
  }

  return [];
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
      ? `Memory is still high at ${usage.toFixed(1)}%.`
      : 'The follow-up check still sees performance pressure.';
  }

  if (category.includes('storage') || category.includes('disk')) {
    return 'Storage readings still need attention after the action.';
  }

  if (category.includes('network') || category.includes('dns') || category.includes('internet')) {
    return 'Network readings still show a connection or name-resolution problem.';
  }

  if (category.includes('thermal') || category.includes('overheat') || category.includes('fan')) {
    return 'Temperature or processor-load readings still need attention.';
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
      <div className="mt-3 rounded-lg border border-current/20 bg-black/20 p-3 text-xs">
        <p className="font-bold uppercase tracking-wider opacity-80">
          Follow-up check ({getToolDisplayName(report.verificationToolName)})
        </p>
        <p className="mt-1 leading-relaxed opacity-90">
          {report.summary.replace(/Before:/g, 'Before check:').replace(/After:/g, 'After check:')}
        </p>
      </div>
    );
  }

  return (
    <div className="mt-3 rounded-lg border border-current/20 bg-black/20 p-3">
      <p className="text-xs font-bold uppercase tracking-wider opacity-80">
        Follow-up check ({getToolDisplayName(report.verificationToolName)})
      </p>

      <div className="mt-2 grid grid-cols-1 gap-2 md:grid-cols-2">
        <div className="rounded-md border border-current/15 bg-black/20 p-2.5">
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
}: {
  result: AutonomyResult;
  diagnosedCategory: string;
}) {
  const state = getState(result);
  const style = getStatusStyle(state);
  const Icon = style.icon;
  const attempt = getLatestAttempt(result);
  const verificationLabel = getVerificationLabel(result.verification);
  const summary =
    state === 'unresolved'
      ? 'The action ran, but the follow-up Windows check still sees the issue.'
      : state === 'success'
        ? result.execution?.summary || attempt?.notes || 'The action completed and the follow-up check passed.'
        : null;
  const displaySummary =
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
              {displaySummary}
            </p>

            {unresolvedReason && (
              <div className="mt-3 rounded-lg border border-current/20 bg-black/20 p-3 text-xs leading-relaxed">
                <p className="font-bold uppercase tracking-wider opacity-80">
                  Why it is still active
                </p>
                <p className="mt-1 opacity-90">
                  {unresolvedReason}
                </p>
              </div>
            )}

            <FollowUpEvidence result={result} />
            <NextStepsPanel diagnosedCategory={diagnosedCategory} result={result} />

            <span className="mt-3 inline-flex rounded-full border border-current/30 px-2.5 py-1 text-[10px] font-bold uppercase">
              {verificationLabel ||
                getAttemptStateLabel(attempt?.state) ||
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

  const contextualToolOptions = useMemo(
    () => getContextualToolOptions(diagnosedCategory, previewResult),
    [diagnosedCategory, previewResult],
  );

  const activeToolOption = getToolOption(
    activeToolId,
    previewResult?.proposedTool?.toolName === activeToolId
      ? previewResult.proposedTool.displayName
      : undefined,
  );

  const handleToolSelectionChange = async (nextToolId: string) => {
    setActiveToolId(nextToolId);
    setUserConsentProvided(false);

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
                Checks the related Windows readings first, previews the safest matching action, and waits for your approval before changing anything.
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

            {isPreviewLoading ? 'Checking device readings...' : 'Run Agent & Review Action'}
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
            <ResultCard result={executionResult} diagnosedCategory={diagnosedCategory} />
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
                    Investigation &amp; Safe Action Preview
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
                      Why RigMD picked this action
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
                            <li key={i}>&bull; {formatCitation(citation)}</li>
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
                      Recommended action and safety preview
                    </h4>
                  </div>

                  <select
                    value={activeToolId}
                    onChange={(e) => handleToolSelectionChange(e.target.value)}
                    disabled={requestActive}
                    className="rounded-lg border border-white/15 bg-[#101821] px-3 py-1.5 text-xs font-semibold text-white focus:border-cyan-400 focus:outline-none"
                  >
                    {contextualToolOptions.map((opt) => (
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
                        Estimated space or memory impact
                      </p>
                      <p className="mt-1 text-base font-bold text-cyan-300">
                        {formatBytes(effectiveDryRun.estimatedBytesAffected)}
                      </p>
                    </div>

                    <div className="rounded-lg border border-white/10 bg-black/30 p-3">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                        Permission needed
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
                        What this action may touch
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
                    <li key={item}>
                      - {item
                        .replace(/telemetry/gi, 'device readings')
                        .replace(/Tier 1/gi, 'low-risk')
                        .replace(/Tier 2/gi, 'approval-required')}
                    </li>
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
                I reviewed the readings and safety preview. I approve RigMD to run this action.
                <span className="mt-1 block text-xs text-amber-100/70">
                  RigMD will not change Windows until this box is checked.
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
