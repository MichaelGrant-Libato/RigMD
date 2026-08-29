import {
  useMemo,
  useState,
} from 'react';

import {
  motion,
} from 'motion/react';

import {
  Stethoscope,
  CheckCircle2,
  AlertTriangle,
  Activity,
  Zap,
  ShieldCheck,
  FileText,
  Info,
  Check,
  Layers,
  Microchip,
  MemoryStick,
  Wifi,
  Battery,
  Fan,
  Gauge,
  HardDrive,
  Monitor,
  Power,
  Thermometer,
  Usb,
  Volume2,
  RefreshCw,
  ScanSearch,
  Database,
  type LucideIcon,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import AutonomyRemediationPanel from '../components/AutonomyRemediationPanel';

import {
  buttonTap,
  cardFadeUp,
  cardTransition,
  hoverLift,
  pageFade,
  pageTransition,
} from '../lib/motion';

import {
  apiGet,
  apiPost,
} from '../lib/api';

import type {
  AgentSnapshotResponse,
} from '../types/rigmd';

import type {
  AutonomyResult,
} from '../services/autonomyService';

interface AgentCommandResponse {
  id: string;
  agentId: string;
  commandType: string;

  status:
    | 'pending'
    | 'running'
    | 'completed'
    | 'failed';

  requestedAt: string;
  claimedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
}

type DiagnosisMode =
  | 'full'
  | 'component'
  | 'scenario';

type DiagnosisStage =
  | 'idle'
  | 'requesting'
  | 'scanning'
  | 'loading-evidence'
  | 'analyzing'
  | 'completed'
  | 'failed';

interface DiagnosticProof {
  label: string;
  value: string;
  status: string;
  meaning: string;
}

interface VerificationTarget {
  target: string;
  label: string;
  description: string;
}

interface DiagnosticReport {
  diagnosed_category: string;
  action_category: string;
  confidence_label: string;
  ai_explanation: string;
  recommended_next_step: string;

  proof?: DiagnosticProof[];

  verification_target?: VerificationTarget;

  session_id?: string;

  resolution_status?: string;
  resolution_checked_at?: string | null;
  resolution_summary?: string;
  resolution_proof?: DiagnosticProof[];

  last_action_status?: string | null;
  last_action_summary?: string;
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;
}

interface DiagnosisModeOption {
  id: DiagnosisMode;
  title: string;
  description: string;
  action: string;
  icon: LucideIcon;
}

interface DiagnosisComponent {
  id: string;
  title: string;
  description: string;
  icon: LucideIcon;
}

interface DiagnosisScenario {
  id: string;
  title: string;
  description: string;
  icon: LucideIcon;
}

const AGENT_ID =
  import.meta.env.VITE_AGENT_ID;

const AUTOMATIC_SCAN_TIMEOUT_MS =
  45_000;

const AUTOMATIC_SCAN_POLL_MS =
  1_500;

const wait = (
  milliseconds: number,
) =>
  new Promise<void>((resolve) => {
    window.setTimeout(
      resolve,
      milliseconds,
    );
  });

const DIAGNOSIS_MODES: DiagnosisModeOption[] = [
  {
    id: 'full',
    title: 'Full System Diagnosis',
    description:
      'Scan the complete PC and collect fresh system, hardware, storage, memory, graphics, and process evidence.',
    action: 'Scan Entire PC',
    icon: Stethoscope,
  },

  {
    id: 'component',
    title: 'Diagnose by Component',
    description:
      'Focus the diagnosis on selected PC components while still collecting the evidence needed for correlation.',
    action: 'Choose Components',
    icon: Microchip,
  },

  {
    id: 'scenario',
    title: 'Diagnose by Scenario',
    description:
      'Start from a common problem such as slow performance, overheating, startup trouble, or display issues.',
    action: 'Choose Scenario',
    icon: Layers,
  },
];

const COMPONENT_GROUPS: {
  group: string;
  items: DiagnosisComponent[];
}[] = [
  {
    group: 'Core Hardware',

    items: [
      {
        id: 'cpu',
        title: 'CPU / Processor',
        description:
          'Processor configuration, workload, and system behavior.',
        icon: Microchip,
      },

      {
        id: 'memory',
        title: 'RAM / Memory',
        description:
          'Memory capacity, utilization, and pressure.',
        icon: MemoryStick,
      },

      {
        id: 'gpu',
        title: 'GPU / Graphics',
        description:
          'Graphics adapter, driver, and display information.',
        icon: Monitor,
      },

      {
        id: 'storage',
        title: 'Storage / SSD / HDD',
        description:
          'Drive capacity, utilization, and storage configuration.',
        icon: HardDrive,
      },
    ],
  },

  {
    group: 'System & Boot',

    items: [
      {
        id: 'os',
        title: 'Operating System',
        description:
          'Windows version and operating-system information.',
        icon: Activity,
      },

      {
        id: 'startup',
        title: 'Startup / Boot',
        description:
          'Startup-related system evidence and configuration.',
        icon: Power,
      },

      {
        id: 'drivers',
        title: 'Drivers',
        description:
          'Installed device and driver-related evidence.',
        icon: AlertTriangle,
      },

      {
        id: 'thermal',
        title: 'Thermals / Fan',
        description:
          'Available thermal and workload-related evidence.',
        icon: Fan,
      },

      {
        id: 'battery',
        title: 'Battery / Power',
        description:
          'Power-related system information when available.',
        icon: Battery,
      },
    ],
  },

  {
    group: 'Connectivity & Devices',

    items: [
      {
        id: 'network',
        title: 'Network / Internet',
        description:
          'Network adapter and connectivity-related evidence.',
        icon: Wifi,
      },

      {
        id: 'display',
        title: 'Display',
        description:
          'Display adapter and graphics-related information.',
        icon: Monitor,
      },

      {
        id: 'peripherals',
        title: 'USB / Peripherals',
        description:
          'Connected device and peripheral-related information.',
        icon: Usb,
      },

      {
        id: 'audio',
        title: 'Audio',
        description:
          'Audio-device and related driver evidence.',
        icon: Volume2,
      },
    ],
  },
];

const DIAGNOSIS_SCENARIOS: DiagnosisScenario[] = [
  {
    id: 'slow-system',
    title: 'Slow System',
    description:
      'The PC feels sluggish, freezes, or struggles during normal use.',
    icon: Gauge,
  },

  {
    id: 'slow-boot',
    title: 'Slow Boot',
    description:
      'Windows takes too long to start or desktop loading feels unusually slow.',
    icon: Power,
  },

  {
    id: 'blue-screen-crash',
    title: 'Blue Screen / Crash',
    description:
      'Unexpected crashes, stop errors, or random system restarts.',
    icon: AlertTriangle,
  },

  {
    id: 'driver-error',
    title: 'Driver Problem',
    description:
      'A device behaves incorrectly or appears to have a driver-related problem.',
    icon: AlertTriangle,
  },

  {
    id: 'no-display',
    title: 'Display Problem',
    description:
      'Screen flicker, visual glitches, black-screen behavior, or display instability.',
    icon: Monitor,
  },

  {
    id: 'overheating-loud-fan',
    title: 'Overheating / Loud Fan',
    description:
      'The PC feels unusually hot or fans become noticeably loud.',
    icon: Thermometer,
  },

  {
    id: 'network-problem',
    title: 'Network Problem',
    description:
      'Connection drops, adapter issues, or unusual network behavior.',
    icon: Wifi,
  },

  {
    id: 'app-crashes',
    title: 'Application Crashes',
    description:
      'Applications freeze, close unexpectedly, or fail during normal use.',
    icon: Activity,
  },

  {
    id: 'stuttering-freezing',
    title: 'Stuttering / Freezing',
    description:
      'Apps, mouse movement, video, or general system activity periodically freezes.',
    icon: Gauge,
  },

  {
    id: 'storage-problem',
    title: 'Storage Problem',
    description:
      'File access, saving, loading, or available drive space appears problematic.',
    icon: HardDrive,
  },
];

function isNoActiveIssue(
  report: DiagnosticReport | null,
) {
  return (
    report?.diagnosed_category
      .toLowerCase() ===
    'no active issue detected'
  );
}

function normalizeActionCategory(
  action: string | undefined | null,
) {
  const value =
    (action ?? '').toLowerCase();

  if (
    value.includes('maintain')
  ) {
    return 'Maintain';
  }

  if (
    value.includes('monitor')
  ) {
    return 'Monitor';
  }

  if (
    value.includes('troubleshoot')
  ) {
    return 'Troubleshoot';
  }

  if (
    value.includes('escalate')
  ) {
    return 'Escalate';
  }

  return action || 'Action';
}

function getActionTone(
  action: string | undefined | null,
) {
  const normalized =
    normalizeActionCategory(action);

  if (normalized === 'Maintain') {
    return {
      badge:
        'border-emerald-400/35 bg-emerald-400/10 text-emerald-300',
      border:
        'border-emerald-400/30',
      soft:
        'bg-emerald-400/[0.055]',
      text:
        'text-emerald-300',
      accent:
        'border-l-emerald-400',
    };
  }

  if (normalized === 'Monitor') {
    return {
      badge:
        'border-cyan-400/35 bg-cyan-400/10 text-cyan-300',
      border:
        'border-cyan-400/30',
      soft:
        'bg-cyan-400/[0.055]',
      text:
        'text-cyan-300',
      accent:
        'border-l-cyan-400',
    };
  }

  if (normalized === 'Troubleshoot') {
    return {
      badge:
        'border-amber-400/35 bg-amber-400/10 text-amber-300',
      border:
        'border-amber-400/30',
      soft:
        'bg-amber-400/[0.055]',
      text:
        'text-amber-300',
      accent:
        'border-l-amber-400',
    };
  }

  if (normalized === 'Escalate') {
    return {
      badge:
        'border-red-400/35 bg-red-400/10 text-red-300',
      border:
        'border-red-400/30',
      soft:
        'bg-red-400/[0.055]',
      text:
        'text-red-300',
      accent:
        'border-l-red-400',
    };
  }

  return {
    badge:
      'border-slate-500/35 bg-slate-500/10 text-slate-300',
    border:
      'border-slate-500/30',
    soft:
      'bg-slate-500/[0.055]',
    text:
      'text-slate-300',
    accent:
      'border-l-slate-400',
  };
}

function getProofCardStyle(
  action: string | undefined | null,
  status?: string,
) {
  const statusValue =
    (status ?? '').toLowerCase();

  if (
    normalizeActionCategory(action) ===
      'Escalate' ||
    statusValue === 'critical'
  ) {
    return 'border-red-500/30 bg-red-500/5';
  }

  if (
    normalizeActionCategory(action) ===
      'Troubleshoot' ||
    statusValue === 'high' ||
    statusValue === 'elevated' ||
    statusValue === 'detected'
  ) {
    return 'border-amber-500/30 bg-amber-500/5';
  }

  if (
    normalizeActionCategory(action) ===
    'Monitor'
  ) {
    return 'border-cyan-500/30 bg-cyan-500/5';
  }

  return 'border-emerald-500/25 bg-emerald-500/5';
}

function getResolutionView(
  status?: string,
) {
  const value =
    (status ?? '').toLowerCase();

  if (value === 'resolved') {
    return {
      label: 'Resolved',
      className:
        'border-emerald-500/30 bg-emerald-500/5 text-emerald-300',
      fallback:
        'The issue is no longer detected in the latest live scan.',
    };
  }

  if (
    value === 'still_active' ||
    value === 'unresolved'
  ) {
    return {
      label: 'Unresolved',
      className:
        'border-amber-500/30 bg-amber-500/5 text-amber-300',
      fallback:
        'The issue is still detected after checking the latest live scan.',
    };
  }

  if (value === 'needs_recheck') {
    return {
      label: 'Needs Recheck',
      className:
        'border-cyan-500/30 bg-cyan-500/5 text-cyan-300',
      fallback:
        'A safe action was recorded. Check the latest live scan to confirm whether the issue improved.',
    };
  }

  return {
    label: 'Not Checked',
    className:
      'border-slate-500/30 bg-slate-500/5 text-slate-300',
    fallback:
      'Use Check if Fixed after a safe action or manual inspection.',
  };
}

function getApiErrorMessage(
  err: any,
  fallback: string,
) {
  const detail =
    err?.response?.data?.detail;

  const message = (() => {
    if (
      typeof detail ===
      'string'
    ) {
      return detail;
    }

    if (
      Array.isArray(detail)
    ) {
      return detail
        .map(
          (item) =>
            item?.msg ||
            item?.message ||
            JSON.stringify(item),
        )
        .join(' ');
    }

    if (
      detail &&
      typeof detail ===
        'object'
    ) {
      return (
        detail.msg ||
        detail.message ||
        JSON.stringify(detail)
      );
    }

    return (
      err?.message ||
      fallback
    );
  })();

  if (
    /at\s+\S+\s+\(|Traceback|System\./.test(
      message,
    )
  ) {
    return fallback;
  }

  return message;
}

function getActionPreview(
  action: RemediationAction,
) {
  const previews: Record<
    string,
    {
      mode:
        | 'automated'
        | 'assisted'
        | 'read-only';
      label: string;
      postTarget?: string;
    }
  > = {
    clear_user_temp_files: {
      mode: 'automated',
      label:
        'Automated Cleanup',
      postTarget:
        'storage_settings',
    },

    open_task_manager: {
      mode: 'assisted',
      label:
        'Assisted Tool',
      postTarget:
        'task_manager',
    },

    open_startup_apps: {
      mode: 'assisted',
      label:
        'Assisted Tool',
      postTarget:
        'startup_apps',
    },

    open_storage_settings: {
      mode: 'assisted',
      label:
        'Assisted Tool',
      postTarget:
        'storage_settings',
    },

    chkdsk_readonly: {
      mode: 'read-only',
      label:
        'Read-Only Check',
      postTarget:
        'storage_settings',
    },

    show_gpu_reset_shortcut: {
      mode: 'assisted',
      label:
        'Assisted Tool',
    },

    open_backup_settings: {
      mode: 'assisted',
      label:
        'Assisted Tool',
      postTarget:
        'backup_settings',
    },

    open_device_manager: {
      mode: 'assisted',
      label:
        'Assisted Tool',
      postTarget:
        'device_manager',
    },
  };

  return (
    previews[action.id] ?? {
      mode: 'assisted',
      label:
        'Assisted Tool',
    }
  );
}

export default function NewDiagnosisView() {
  const [
    diagnosisMode,
    setDiagnosisMode,
  ] =
    useState<DiagnosisMode>(
      'full',
    );

  const [
    selectedComponents,
    setSelectedComponents,
  ] =
    useState<string[]>([]);

  const [
    selectedScenarioId,
    setSelectedScenarioId,
  ] =
    useState<string>('');

  const [
    diagnosisStage,
    setDiagnosisStage,
  ] =
    useState<DiagnosisStage>(
      'idle',
    );

  const [
    commandId,
    setCommandId,
  ] =
    useState<string | null>(
      null,
    );

  const [
    snapshot,
    setSnapshot,
  ] =
    useState<AgentSnapshotResponse | null>(
      null,
    );

  const [
    report,
    setReport,
  ] =
    useState<DiagnosticReport | null>(
      null,
    );

  const [
    remediationActions,
    setRemediationActions,
  ] =
    useState<RemediationAction[]>(
      [],
    );

  const [
    error,
    setError,
  ] =
    useState<string | null>(
      null,
    );

  const [
    checkingResolution,
    setCheckingResolution,
  ] =
    useState(false);

  const [
    inspecting,
    setInspecting,
  ] =
    useState(false);

  const allComponents =
    useMemo(
      () =>
        COMPONENT_GROUPS.flatMap(
          (group) =>
            group.items,
        ),
      [],
    );

  const activeMode =
    DIAGNOSIS_MODES.find(
      (mode) =>
        mode.id ===
        diagnosisMode,
    ) ??
    DIAGNOSIS_MODES[0];

  const ActiveModeIcon =
    activeMode.icon;

  const selectedComponentDetails =
    selectedComponents
      .map((componentId) =>
        allComponents.find(
          (component) =>
            component.id ===
            componentId,
        ),
      )
      .filter(
        Boolean,
      ) as DiagnosisComponent[];

  const selectedScenario =
    DIAGNOSIS_SCENARIOS.find(
      (scenario) =>
        scenario.id ===
        selectedScenarioId,
    );

  const diagnosisBusy =
    diagnosisStage ===
      'requesting' ||
    diagnosisStage ===
      'scanning' ||
    diagnosisStage ===
      'loading-evidence' ||
    diagnosisStage ===
      'analyzing';

  const canRunDiagnosis =
    diagnosisMode === 'full'
      ? true
      : diagnosisMode ===
          'component'
        ? selectedComponents.length >
          0
        : Boolean(
            selectedScenarioId,
          );

  const runButtonLabel =
    diagnosisStage ===
    'requesting'
      ? 'Creating Scan Request...'
      : diagnosisStage ===
          'scanning'
        ? 'Scanning This PC...'
        : diagnosisStage ===
            'loading-evidence'
          ? 'Loading Evidence...'
          : diagnosisStage ===
              'analyzing'
            ? 'Analyzing Evidence...'
            : diagnosisStage ===
                'completed'
              ? 'Run Diagnosis Again'
              : diagnosisMode ===
                  'full'
                ? 'Run Full Diagnosis'
                : diagnosisMode ===
                    'component'
                  ? 'Run Component Diagnosis'
                  : 'Run Scenario Diagnosis';

  const stageDescription =
    diagnosisStage ===
    'requesting'
      ? 'Sending a scan request to the installed RigMD Agent.'
      : diagnosisStage ===
          'scanning'
        ? 'The RigMD Agent is collecting fresh system and process evidence.'
        : diagnosisStage ===
            'loading-evidence'
          ? 'The scan finished. RigMD is retrieving the fresh snapshot.'
          : diagnosisStage ===
              'analyzing'
            ? 'Fresh evidence is ready for diagnostic interpretation.'
            : '';

  const selectedScopeLabel =
    diagnosisMode === 'full'
      ? 'Entire PC'
      : diagnosisMode ===
          'component'
        ? selectedComponentDetails
            .map(
              (component) =>
                component.title,
            )
            .join(', ')
        : selectedScenario
            ?.title ?? '';

  const selectedCardStyle =
    'border-cyan-300/55 bg-[#12343a] text-teal-50 shadow-[inset_0_1px_0_rgba(34,211,238,0.07)]';

  const unselectedCardStyle =
    'border-[var(--rigmd-border)] bg-[var(--rigmd-card)] text-slate-300 hover:border-[#2b5261] hover:bg-[var(--rigmd-card-hover)]';

  const softPanelStyle =
    'border-[var(--rigmd-border)] bg-[#101821]';

  const softTileStyle =
    'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)]';

  const iconBoxStyle = (
    selected: boolean,
  ) =>
    selected
      ? 'border-cyan-300/35 bg-cyan-300/12 text-cyan-200'
      : 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] text-slate-400';

  const selectDiagnosisMode = (
    mode: DiagnosisMode,
  ) => {
    if (diagnosisBusy) {
      return;
    }

    setDiagnosisMode(mode);

    setSelectedComponents(
      [],
    );

    setSelectedScenarioId(
      '',
    );

    setSnapshot(null);
    setReport(null);
    setRemediationActions(
      [],
    );
    setError(null);
    setCommandId(null);

    setDiagnosisStage(
      'idle',
    );
  };

  const toggleComponent = (
    componentId: string,
  ) => {
    if (diagnosisBusy) {
      return;
    }

    setSelectedComponents(
      (current) =>
        current.includes(
          componentId,
        )
          ? current.filter(
              (item) =>
                item !==
                componentId,
            )
          : [
              ...current,
              componentId,
            ],
    );

    setReport(null);
    setSnapshot(null);
    setRemediationActions(
      [],
    );
    setError(null);

    setDiagnosisStage(
      'idle',
    );
  };

  const selectAllComponents =
    () => {
      if (diagnosisBusy) {
        return;
      }

      const allIds =
        allComponents.map(
          (component) =>
            component.id,
        );

      const alreadyAllSelected =
        allIds.every((id) =>
          selectedComponents.includes(
            id,
          ),
        );

      setSelectedComponents(
        alreadyAllSelected
          ? []
          : allIds,
      );

      setSnapshot(null);
      setReport(null);
      setRemediationActions(
        [],
      );
      setError(null);

      setDiagnosisStage(
        'idle',
      );
    };

  const chooseScenario = (
    scenarioId: string,
  ) => {
    if (diagnosisBusy) {
      return;
    }

    setSelectedScenarioId(
      scenarioId,
    );

    setSnapshot(null);
    setReport(null);
    setRemediationActions(
      [],
    );
    setError(null);

    setDiagnosisStage(
      'idle',
    );
  };

    const handleRunDiagnosis =
    async () => {
      if (
        !canRunDiagnosis ||
        diagnosisBusy
      ) {
        return;
      }

      if (!AGENT_ID) {
        setSnapshot(null);
        setReport(null);
        setRemediationActions(
          [],
        );
        setCommandId(null);

        setError(
          'No RigMD Agent ID is configured. Add VITE_AGENT_ID to the frontend environment file.',
        );

        setDiagnosisStage(
          'failed',
        );

        return;
      }

      setDiagnosisStage(
        'requesting',
      );

      setCommandId(null);
      setSnapshot(null);
      setReport(null);
      setRemediationActions(
        [],
      );
      setError(null);

      try {
        const createResponse =
          await apiPost<AgentCommandResponse>(
            `/api/agent/${AGENT_ID}/scan-request`,
            {},
            {
              headers: {
                'X-Client-ID':
                  AGENT_ID,
              },
            },
          );

        const newCommandId =
          createResponse.data.id;

        if (!newCommandId) {
          throw new Error(
            'RigMD did not return a scan command ID.',
          );
        }

        setCommandId(
          newCommandId,
        );

        setDiagnosisStage(
          'scanning',
        );

        const deadline =
          Date.now() +
          AUTOMATIC_SCAN_TIMEOUT_MS;

        let completed =
          false;

        while (
          Date.now() <
          deadline
        ) {
          await wait(
            AUTOMATIC_SCAN_POLL_MS,
          );

          const commandResponse =
            await apiGet<AgentCommandResponse>(
              `/api/agent/${AGENT_ID}/commands/${newCommandId}`,
              {
                headers: {
                  'X-Client-ID':
                    AGENT_ID,
                },
              },
            );

          const command =
            commandResponse.data;

          if (
            command.status ===
            'completed'
          ) {
            completed =
              true;

            break;
          }

          if (
            command.status ===
            'failed'
          ) {
            throw new Error(
              command.errorMessage ||
                'The RigMD Agent reported that the scan failed.',
            );
          }
        }

        if (!completed) {
          throw new Error(
            'The RigMD Agent did not finish the scan within 45 seconds.',
          );
        }

        setDiagnosisStage(
          'loading-evidence',
        );

        const snapshotResponse =
          await apiGet<AgentSnapshotResponse>(
            `/api/agent/${AGENT_ID}/snapshot`,
            {
              headers: {
                'X-Client-ID':
                  AGENT_ID,
              },
            },
          );

        const freshSnapshot =
          snapshotResponse.data;

        if (
          !freshSnapshot?.hardware
        ) {
          throw new Error(
            'The scan completed, but no hardware evidence was returned.',
          );
        }

        setSnapshot(
          freshSnapshot,
        );

      setDiagnosisStage(
        'analyzing',
      );

      const automaticResponse =
        await apiPost<DiagnosticReport>(
          '/api/diagnosis/automatic',
          {
            agentId:
              AGENT_ID,

            commandId:
              newCommandId,

            mode:
              diagnosisMode,

            componentIds:
              diagnosisMode === 'component'
                ? selectedComponents
                : [],

            scenarioId:
              diagnosisMode === 'scenario'
                ? selectedScenarioId
                : null,
          },
          {
            headers: {
              'X-Client-ID':
                AGENT_ID,
            },
          },
        );

      const automaticReport =
        automaticResponse.data;

      if (
        !automaticReport ||
        !automaticReport.diagnosed_category
      ) {
        throw new Error(
          'RigMD completed the scan, but no diagnostic interpretation was returned.',
        );
      }

      setReport(
        automaticReport,
      );

      setDiagnosisStage(
        'completed',
      );

      } catch (err: any) {
        console.error(
          'One-click diagnosis error:',
          err,
        );

        setError(
          getApiErrorMessage(
            err,
            'RigMD could not complete the diagnosis scan.',
          ),
        );

        setDiagnosisStage(
          'failed',
        );
      }
    };

  const handleCheckResolution =
    async () => {
      if (
        !report?.session_id
      ) {
        return;
      }

      setCheckingResolution(
        true,
      );

      try {
        const response =
          await apiPost<any>(
            `/api/diagnosis/${report.session_id}/check-resolution`,
          );

        setReport((prev) =>
          prev
            ? {
                ...prev,

                resolution_status:
                  response.data
                    .resolution_status,

                resolution_checked_at:
                  response.data
                    .resolution_checked_at,

                resolution_summary:
                  response.data
                    .resolution_summary,

                resolution_proof:
                  response.data
                    .resolution_proof,
              }
            : prev,
        );
      } catch (err) {
        console.error(
          'Resolution check error:',
          err,
        );

        setError(
          'RigMD could not check whether this issue is fixed yet.',
        );
      } finally {
        setCheckingResolution(
          false,
        );
      }
    };

  const getVerificationTarget =
    () => {
      if (
        report?.verification_target
          ?.label
      ) {
        return report
          .verification_target
          .label;
      }

      const category =
        report?.diagnosed_category
          .toLowerCase() ?? '';

      if (
        category.includes(
          'no active issue',
        )
      ) {
        return 'No verification target needed';
      }

      if (
        category.includes(
          'driver',
        )
      ) {
        return 'Windows Device Manager';
      }

      if (
        category.includes(
          'display',
        )
      ) {
        return 'Device Manager - Display adapters';
      }

      if (
        category.includes(
          'storage',
        )
      ) {
        return 'Windows Storage settings';
      }

      if (
        category.includes(
          'boot',
        ) ||
        category.includes(
          'startup',
        )
      ) {
        return 'Windows Startup Apps settings';
      }

      if (
        category.includes(
          'thermal',
        )
      ) {
        return 'Task Manager - Performance tab';
      }

      if (
        category.includes(
          'os performance',
        )
      ) {
        return 'Task Manager - Processes tab';
      }

      return 'Windows Reliability Monitor';
    };

  const getVerificationDescription =
    () => {
      return (
        report
          ?.verification_target
          ?.description ||
        'This opens the closest Windows tool related to the selected diagnosis.'
      );
    };

  const handleVerifyLocation =
    async () => {
      if (
        !report ||
        isNoActiveIssue(
          report,
        )
      ) {
        return;
      }

      setInspecting(true);

      try {
        const category =
          report.diagnosed_category.toLowerCase();

        const targetParam =
          report
            .verification_target
            ?.target ||
          (category.includes(
            'display',
          )
            ? 'device_manager'
            : category.includes(
                  'driver',
                )
              ? 'device_manager'
              : category.includes(
                    'boot',
                  ) ||
                  category.includes(
                    'startup',
                  )
                ? 'startup_apps'
                : category.includes(
                      'storage',
                    )
                  ? 'storage_settings'
                  : category.includes(
                        'thermal',
                      )
                    ? 'task_manager'
                    : category.includes(
                          'os performance',
                        )
                      ? 'task_manager'
                      : 'reliability_monitor');

        await apiPost(
          '/api/remediation/open-target',
          {
            target:
              targetParam,
          },
        );
      } catch (err) {
        console.error(
          'Inspection error:',
          err,
        );

        setError(
          'RigMD could not open the Windows verification tool. You can still open it manually from Windows Search.',
        );
      } finally {
        setInspecting(false);
      }
    };

  const handleAutonomyExecutionComplete =
    async (
      result: AutonomyResult,
    ) => {
      if (
        !result.execution
          ?.success ||
        !report?.session_id
      ) {
        return;
      }

      try {
        const statusResponse =
          await apiPost<{
            resolution_status?: string;
            last_action_status?:
              | string
              | null;
            last_action_summary?: string;
          }>(
            `/api/diagnosis/${report.session_id}/needs-recheck`,
          );

        setReport((prev) =>
          prev
            ? {
                ...prev,

                resolution_status:
                  statusResponse
                    .data
                    ?.resolution_status,

                last_action_status:
                  statusResponse
                    .data
                    ?.last_action_status,

                last_action_summary:
                  statusResponse
                    .data
                    ?.last_action_summary,
              }
            : prev,
        );
      } catch (err) {
        console.error(
          'Resolution status update error:',
          err,
        );

        setError(
          'RigMD completed the autonomy action, but could not mark this session for follow-up verification.',
        );
      }
    };

  const handlePostExecutionInspect =
    async (
      action: RemediationAction,
    ) => {
      const preview =
        getActionPreview(
          action,
        );

      const target =
        preview.postTarget ||
        report
          ?.verification_target
          ?.target;

      if (!target) {
        return;
      }

      setInspecting(true);

      try {
        await apiPost(
          '/api/remediation/open-target',
          {
            target,
          },
        );
      } catch (err) {
        console.error(
          'Post-execution inspection error:',
          err,
        );

        setError(
          'RigMD could not open the related Windows location. You can still open it manually from Windows Search.',
        );
      } finally {
        setInspecting(false);
      }
    };

  const resetDiagnosis =
    () => {
      setDiagnosisMode(
        'full',
      );

      setSelectedComponents(
        [],
      );

      setSelectedScenarioId(
        '',
      );

      setDiagnosisStage(
        'idle',
      );

      setCommandId(null);
      setSnapshot(null);
      setReport(null);

      setRemediationActions(
        [],
      );

      setError(null);

      setCheckingResolution(
        false,
      );

      setInspecting(false);
    };

  return (
    <>
      <TopHeader
        title="New Diagnosis"
        subtitle="Choose a diagnosis type, scan the PC, and review fresh system evidence"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-5 py-5 lg:px-6"
      >
        <div className="mx-auto grid w-full max-w-[1512px] grid-cols-1 gap-5 xl:grid-cols-[minmax(0,1fr)_390px]">
          <div className="min-w-0 space-y-5">
            <motion.section
              variants={cardFadeUp}
              initial="hidden"
              animate="visible"
              transition={cardTransition}
              className={`overflow-hidden rounded-2xl border ${softPanelStyle}`}
            >
              <div className="border-b border-[var(--rigmd-border)] px-5 py-4">
                <div className="flex items-start gap-3">
                  <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-cyan-400/25 bg-cyan-400/10 text-cyan-300">
                    <ScanSearch size={22} />
                  </div>

                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="text-xl font-bold text-white">
                        One-Click Diagnosis
                      </h2>

                      <span className="rounded-full border border-teal-300/25 bg-teal-300/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-teal-200">
                        Recommended
                      </span>
                    </div>

                    <p className="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
                      Select what you want RigMD to inspect, then run a fresh scan using the installed Windows Agent.
                    </p>
                  </div>
                </div>
              </div>

              <div className="p-5">
                <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
                  {DIAGNOSIS_MODES.map((mode) => {
                    const selected =
                      diagnosisMode === mode.id;

                    const Icon =
                      mode.icon;

                    return (
                      <motion.button
                        key={mode.id}
                        type="button"
                        disabled={diagnosisBusy}
                        onClick={() =>
                          selectDiagnosisMode(
                            mode.id,
                          )
                        }
                        whileHover={
                          diagnosisBusy
                            ? undefined
                            : hoverLift
                        }
                        whileTap={
                          diagnosisBusy
                            ? undefined
                            : buttonTap
                        }
                        className={`flex min-h-[178px] flex-col items-start justify-between rounded-xl border p-5 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${
                          selected
                            ? selectedCardStyle
                            : unselectedCardStyle
                        }`}
                      >
                        <span className="flex w-full items-center justify-between gap-3">
                          <span
                            className={`flex h-11 w-11 items-center justify-center rounded-lg border ${iconBoxStyle(
                              selected,
                            )}`}
                          >
                            <Icon size={21} />
                          </span>

                          {selected && (
                            <span className="inline-flex items-center gap-1 rounded-full border border-cyan-300/30 bg-cyan-300/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-cyan-200">
                              <Check size={12} />
                              Selected
                            </span>
                          )}
                        </span>

                        <span className="mt-5">
                          <span className="block text-base font-bold text-white">
                            {mode.title}
                          </span>

                          <span className="mt-2 block text-sm leading-relaxed text-slate-400">
                            {mode.description}
                          </span>

                          <span className="mt-3 block text-[11px] font-bold uppercase tracking-wider text-cyan-300">
                            {mode.action}
                          </span>
                        </span>
                      </motion.button>
                    );
                  })}
                </div>

                {diagnosisMode === 'full' && (
                  <div className="mt-5 rounded-xl border border-cyan-400/18 bg-cyan-400/[0.035] p-4">
                    <div className="flex items-start gap-3">
                      <ShieldCheck
                        size={18}
                        className="mt-0.5 shrink-0 text-cyan-300"
                      />

                      <div>
                        <p className="text-sm font-bold text-white">
                          Full System Scan
                        </p>

                        <p className="mt-1 text-xs leading-relaxed text-slate-400">
                          RigMD will collect all currently supported system evidence. No questionnaire is required.
                        </p>
                      </div>
                    </div>
                  </div>
                )}

                {diagnosisMode ===
                  'component' && (
                  <div className="mt-5 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-5">
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <div>
                        <h3 className="font-bold text-white">
                          Choose components
                        </h3>

                        <p className="mt-1 text-sm text-slate-500">
                          Select one or more subsystems to focus the diagnosis.
                        </p>
                      </div>

                      <button
                        type="button"
                        disabled={diagnosisBusy}
                        onClick={selectAllComponents}
                        className="rounded-lg border border-teal-300/30 bg-teal-300/10 px-3 py-2 text-xs font-bold text-teal-200 transition hover:bg-teal-300/15 disabled:cursor-not-allowed disabled:opacity-50"
                      >
                        {selectedComponents.length ===
                        allComponents.length
                          ? 'Clear all'
                          : 'Select all'}
                      </button>
                    </div>

                    <div className="mt-5 space-y-5">
                      {COMPONENT_GROUPS.map(
                        (group) => (
                          <div key={group.group}>
                            <p className="mb-3 text-[11px] font-bold uppercase tracking-[0.2em] text-slate-500">
                              {group.group}
                            </p>

                            <div className="grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-3">
                              {group.items.map(
                                (component) => {
                                  const selected =
                                    selectedComponents.includes(
                                      component.id,
                                    );

                                  const Icon =
                                    component.icon;

                                  return (
                                    <motion.button
                                      key={
                                        component.id
                                      }
                                      type="button"
                                      disabled={
                                        diagnosisBusy
                                      }
                                      onClick={() =>
                                        toggleComponent(
                                          component.id,
                                        )
                                      }
                                      whileTap={
                                        diagnosisBusy
                                          ? undefined
                                          : buttonTap
                                      }
                                      className={`flex min-h-[106px] items-start gap-3 rounded-lg border p-4 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${
                                        selected
                                          ? selectedCardStyle
                                          : unselectedCardStyle
                                      }`}
                                    >
                                      <span
                                        className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border ${iconBoxStyle(
                                          selected,
                                        )}`}
                                      >
                                        <Icon
                                          size={18}
                                        />
                                      </span>

                                      <span className="min-w-0 flex-1">
                                        <span className="flex items-start justify-between gap-2">
                                          <span className="font-bold text-white">
                                            {
                                              component.title
                                            }
                                          </span>

                                          {selected && (
                                            <Check
                                              size={15}
                                              className="shrink-0 text-teal-300"
                                            />
                                          )}
                                        </span>

                                        <span className="mt-1 block text-xs leading-relaxed text-slate-500">
                                          {
                                            component.description
                                          }
                                        </span>
                                      </span>
                                    </motion.button>
                                  );
                                },
                              )}
                            </div>
                          </div>
                        ),
                      )}
                    </div>
                  </div>
                )}

                {diagnosisMode ===
                  'scenario' && (
                  <div className="mt-5 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-5">
                    <div>
                      <h3 className="font-bold text-white">
                        Choose a scenario
                      </h3>

                      <p className="mt-1 text-sm text-slate-500">
                        Select the closest problem. RigMD will still use fresh system evidence instead of relying only on the label.
                      </p>
                    </div>

                    <div className="mt-5 grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-3">
                      {DIAGNOSIS_SCENARIOS.map(
                        (scenario) => {
                          const selected =
                            selectedScenarioId ===
                            scenario.id;

                          const Icon =
                            scenario.icon;

                          return (
                            <motion.button
                              key={scenario.id}
                              type="button"
                              disabled={
                                diagnosisBusy
                              }
                              onClick={() =>
                                chooseScenario(
                                  scenario.id,
                                )
                              }
                              whileTap={
                                diagnosisBusy
                                  ? undefined
                                  : buttonTap
                              }
                              className={`flex min-h-[110px] items-start gap-3 rounded-lg border p-4 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${
                                selected
                                  ? selectedCardStyle
                                  : unselectedCardStyle
                              }`}
                            >
                              <span
                                className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border ${iconBoxStyle(
                                  selected,
                                )}`}
                              >
                                <Icon
                                  size={18}
                                />
                              </span>

                              <span className="min-w-0 flex-1">
                                <span className="flex items-start justify-between gap-2">
                                  <span className="font-bold text-white">
                                    {scenario.title}
                                  </span>

                                  {selected && (
                                    <Check
                                      size={15}
                                      className="shrink-0 text-teal-300"
                                    />
                                  )}
                                </span>

                                <span className="mt-1 block text-xs leading-relaxed text-slate-500">
                                  {
                                    scenario.description
                                  }
                                </span>
                              </span>
                            </motion.button>
                          );
                        },
                      )}
                    </div>
                  </div>
                )}

                <div className="mt-5 flex flex-col gap-3 rounded-xl border border-[var(--rigmd-border)] bg-[#0d151d] p-4 lg:flex-row lg:items-center lg:justify-between">
                  <div>
                    <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                      Selected Scope
                    </p>

                    <p className="mt-1 text-sm font-semibold text-white">
                      {selectedScopeLabel ||
                        'Select a diagnosis scope'}
                    </p>
                  </div>

                  <motion.button
                    type="button"
                    disabled={
                      !canRunDiagnosis ||
                      diagnosisBusy
                    }
                    onClick={
                      handleRunDiagnosis
                    }
                    whileHover={
                      canRunDiagnosis &&
                      !diagnosisBusy
                        ? hoverLift
                        : undefined
                    }
                    whileTap={
                      canRunDiagnosis &&
                      !diagnosisBusy
                        ? buttonTap
                        : undefined
                    }
                    className="flex min-w-[230px] items-center justify-center gap-2 rounded-xl bg-[#2dd4bf] px-5 py-3.5 text-sm font-bold text-[#041014] transition hover:bg-[#5eead4] disabled:cursor-not-allowed disabled:bg-slate-700/35 disabled:text-slate-500"
                  >
                    {diagnosisBusy ? (
                      <RefreshCw
                        size={17}
                        className="animate-spin"
                      />
                    ) : (
                      <Zap size={17} />
                    )}

                    {runButtonLabel}
                  </motion.button>
                </div>

                {!canRunDiagnosis &&
                  diagnosisMode !==
                    'full' && (
                    <p className="mt-2 text-right text-xs text-slate-500">
                      {diagnosisMode ===
                      'component'
                        ? 'Select at least one component before running diagnosis.'
                        : 'Select a scenario before running diagnosis.'}
                    </p>
                  )}
              </div>
            </motion.section>

            {diagnosisBusy && (
              <motion.section
                variants={cardFadeUp}
                initial="hidden"
                animate="visible"
                transition={cardTransition}
                className="rounded-2xl border border-cyan-400/20 bg-[#101821] p-5"
              >
                <div className="flex items-start gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-cyan-400/25 bg-cyan-400/10 text-cyan-300">
                    <Activity
                      size={19}
                      className="animate-pulse"
                    />
                  </div>

                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-bold text-white">
                      {runButtonLabel}
                    </p>

                    <p className="mt-1 text-xs leading-relaxed text-slate-500">
                      {stageDescription}
                    </p>

                    <div className="mt-4 h-1.5 overflow-hidden rounded-full bg-[var(--rigmd-card-soft)]">
                      <motion.div
                        className="h-full rounded-full bg-cyan-400"
                        initial={{
                          width: '8%',
                        }}
                        animate={{
                          width:
                            diagnosisStage ===
                            'requesting'
                              ? '22%'
                              : diagnosisStage ===
                                  'scanning'
                                ? '62%'
                                : diagnosisStage ===
                                    'loading-evidence'
                                  ? '86%'
                                  : '96%',
                        }}
                        transition={{
                          duration: 0.35,
                          ease: 'easeOut',
                        }}
                      />
                    </div>
                  </div>
                </div>
              </motion.section>
            )}

            {error && (
              <motion.section
                variants={cardFadeUp}
                initial="hidden"
                animate="visible"
                transition={cardTransition}
                className="rounded-2xl border border-red-400/25 bg-red-400/10 p-5"
              >
                <div className="flex items-start gap-3">
                  <AlertTriangle
                    size={19}
                    className="mt-0.5 shrink-0 text-red-300"
                  />

                  <div>
                    <p className="text-sm font-bold text-red-100">
                      Diagnosis could not be completed
                    </p>

                    <p className="mt-1 text-xs leading-relaxed text-red-200/80">
                      {error}
                    </p>
                  </div>
                </div>
              </motion.section>
            )}

            {snapshot &&
              diagnosisStage ===
                'completed' && (
                <motion.section
                  variants={cardFadeUp}
                  initial="hidden"
                  animate="visible"
                  transition={cardTransition}
                  className={`rounded-2xl border p-5 ${softPanelStyle}`}
                >
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <div className="flex items-center gap-2">
                        <CheckCircle2
                          size={18}
                          className="text-emerald-300"
                        />

                        <h3 className="text-lg font-bold text-white">
                          Fresh System Evidence
                        </h3>
                      </div>

                      <p className="mt-1 text-xs text-slate-500">
                        Captured{' '}
                        {new Date(
                          snapshot.capturedAt,
                        ).toLocaleString()}
                      </p>
                    </div>

                    <span className="inline-flex w-fit items-center gap-1.5 rounded-lg border border-emerald-400/25 bg-emerald-400/10 px-3 py-1.5 text-[10px] font-bold uppercase tracking-wider text-emerald-300">
                      <Database size={12} />
                      Agent Snapshot
                    </span>
                  </div>

                  <div className="mt-5 grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Diagnosis Mode
                      </p>

                      <p className="mt-2 text-sm font-bold text-white">
                        {activeMode.title}
                      </p>

                      <p className="mt-1 text-xs leading-relaxed text-slate-500">
                        {selectedScopeLabel}
                      </p>
                    </div>

                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Memory
                      </p>

                      <p className="mt-2 text-xl font-bold text-white">
                        {Number(
                          snapshot.hardware.ram
                            .usagePercent,
                        ).toFixed(1)}
                        %
                      </p>

                      <p className="mt-1 text-xs text-slate-500">
                        {Number(
                          snapshot.hardware.ram
                            .usedGb,
                        ).toFixed(1)}{' '}
                        GB of{' '}
                        {Number(
                          snapshot.hardware.ram
                            .totalGb,
                        ).toFixed(1)}{' '}
                        GB
                      </p>
                    </div>

                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Graphics
                      </p>

                      <p className="mt-2 truncate text-sm font-bold text-white">
                        {snapshot.hardware.gpu
                          .name ||
                          'Detected GPU'}
                      </p>

                      <p className="mt-1 text-xs text-slate-500">
                        Graphics evidence received
                      </p>
                    </div>

                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                        Storage Type
                      </p>

                      <p className="mt-2 text-sm font-bold text-white">
                        {snapshot.hardware
                          .primaryStorageType ||
                          'Detected'}
                      </p>

                      <p className="mt-1 text-xs text-slate-500">
                        Storage evidence received
                      </p>
                    </div>
                  </div>

                  <div className="mt-4 rounded-xl border border-cyan-400/18 bg-cyan-400/[0.035] p-4">
                    <div className="flex items-start gap-3">
                      <Info
                        size={17}
                        className="mt-0.5 shrink-0 text-cyan-300"
                      />

                      <div>
                        <p className="text-xs font-bold uppercase tracking-wider text-cyan-200">
                          Scan complete
                        </p>

                          <p className="mt-1 text-xs leading-relaxed text-slate-400">
                            The installed RigMD Agent returned fresh system evidence and the backend analyzed the snapshot using evidence-based diagnostic rules.
                          </p>

                        {commandId && (
                          <p className="mt-2 break-all font-mono text-[10px] text-slate-600">
                            Command: {commandId}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>
                </motion.section>
              )}

            {report && (
              <motion.section
                variants={cardFadeUp}
                initial="hidden"
                animate="visible"
                transition={cardTransition}
                className="space-y-5"
              >
                <div
                  className={`rounded-2xl border p-5 ${getActionTone(report.action_category).border} ${getActionTone(report.action_category).soft}`}
                >
                  <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                    <div className="min-w-0">
                      <div className="mb-3 flex flex-wrap items-center gap-2">
                        <span className="rounded border border-cyan-400/25 bg-cyan-400/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-cyan-300">
                          Diagnosis Ready
                        </span>

                        <span
                          className={`rounded border px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider ${getActionTone(report.action_category).badge}`}
                        >
                          {normalizeActionCategory(
                            report.action_category,
                          )}
                        </span>
                      </div>

                      <p className="text-[10px] font-bold uppercase tracking-[0.18em] text-slate-500">
                        Probable Cause
                      </p>

                      <h3 className="mt-1 text-2xl font-bold leading-tight text-white">
                        {
                          report.diagnosed_category
                        }
                      </h3>

                      {report.confidence_label
                        ?.toLowerCase()
                        .includes('low') && (
                        <p className="mt-2 text-sm text-slate-400">
                          The available evidence is limited, so this result should be treated as a starting point for further verification.
                        </p>
                      )}
                    </div>

                    <div className="grid min-w-[240px] grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-1">
                      <div
                        className={`rounded-lg border p-3 ${softTileStyle}`}
                      >
                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                          Recommended Action
                        </p>

                        <p
                          className={`mt-1 text-sm font-bold ${getActionTone(report.action_category).text}`}
                        >
                          {normalizeActionCategory(
                            report.action_category,
                          )}
                        </p>
                      </div>

                      <div
                        className={`rounded-lg border p-3 ${softTileStyle}`}
                      >
                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                          Confidence
                        </p>

                        <p className="mt-1 text-sm font-bold text-white">
                          {report.confidence_label ||
                            'Not provided'}
                        </p>
                      </div>
                    </div>
                  </div>
                </div>

                <div>
                  <h4 className="mb-2 text-xs font-bold uppercase tracking-wider text-gray-500">
                    Why RigMD Suggested This
                  </h4>

                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4">
                    <p className="text-sm leading-relaxed text-gray-300">
                      {report.ai_explanation ||
                        'RigMD did not return a diagnostic explanation for this result.'}
                    </p>
                  </div>
                </div>

                {report.proof &&
                  report.proof.length > 0 && (
                    <div>
                      <h4 className="mb-2 text-xs font-bold uppercase tracking-wider text-gray-500">
                        Evidence / Live Scan Proof
                      </h4>

                      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                        {report.proof.map(
                          (item) => (
                            <motion.div
                              key={
                                item.label
                              }
                              variants={
                                cardFadeUp
                              }
                              whileHover={
                                hoverLift
                              }
                              transition={
                                cardTransition
                              }
                              className={`rounded-xl border p-4 ${getProofCardStyle(
                                report.action_category,
                                item.status,
                              )}`}
                            >
                              <p className="text-[10px] font-bold uppercase tracking-wider text-gray-500">
                                {
                                  item.label
                                }
                              </p>

                              <p className="mt-1 text-sm font-bold text-white">
                                {
                                  item.value
                                }
                              </p>

                              <p className="mt-2 text-xs leading-relaxed text-gray-400">
                                {
                                  item.meaning
                                }
                              </p>

                              <p className="mt-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">
                                Source: Live scan
                              </p>
                            </motion.div>
                          ),
                        )}
                      </div>
                    </div>
                  )}

                {!isNoActiveIssue(
                  report,
                ) && (
                  <div className="rounded-xl border border-cyan-500/20 bg-cyan-500/[0.045] p-4">
                    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                      <div className="min-w-0">
                        <h4 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-white">
                          <FileText
                            size={14}
                            className="text-cyan-400"
                          />
                          Targeted Verification
                        </h4>

                        <p className="mt-3 text-[10px] font-bold uppercase tracking-wider text-slate-500">
                          Verification Target
                        </p>

                        <p className="mt-1 text-sm font-bold text-cyan-200">
                          {getVerificationTarget()}
                        </p>

                        <p className="mt-2 text-xs leading-relaxed text-slate-400">
                          {
                            getVerificationDescription()
                          }
                        </p>
                      </div>

                      <motion.button
                        type="button"
                        disabled={inspecting}
                        onClick={
                          handleVerifyLocation
                        }
                        whileTap={
                          buttonTap
                        }
                        className="w-full shrink-0 rounded-lg border border-cyan-500/40 bg-cyan-500/10 px-4 py-2.5 text-center text-xs font-bold text-cyan-200 transition-all hover:bg-cyan-500/15 disabled:cursor-not-allowed disabled:opacity-50 md:w-auto"
                      >
                        {inspecting
                          ? 'Opening...'
                          : 'Verify & Inspect'}
                      </motion.button>
                    </div>
                  </div>
                )}

                <div
                  className={`rounded-xl border border-l-4 p-4 ${getActionTone(report.action_category).border} ${getActionTone(report.action_category).soft} ${getActionTone(report.action_category).accent}`}
                >
                  <h4 className="mb-1 text-xs font-bold uppercase tracking-wider text-white">
                    Recommended Next Step
                  </h4>

                  <p className="text-sm leading-relaxed text-gray-300">
                    {report.recommended_next_step ||
                      'RigMD did not return a recommended next step for this result.'}
                  </p>
                </div>

                {isNoActiveIssue(
                  report,
                ) && (
                  <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-4 text-xs text-emerald-300">
                    No active issue requiring remediation was verified from the latest system evidence.
                  </div>
                )}

                {!isNoActiveIssue(
                  report,
                ) &&
                  remediationActions.length >
                    0 && (
                    <div className="space-y-4">
                      <div>
                        <h4 className="text-xs font-bold uppercase tracking-wider text-white">
                          Safe Actions Available
                        </h4>

                        <p className="mt-1 text-xs text-slate-500">
                          Only backend-approved remediation actions are shown here.
                        </p>
                      </div>

                      <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
                        {remediationActions.map(
                          (action) => {
                            const preview =
                              getActionPreview(
                                action,
                              );

                            return (
                              <motion.div
                                key={
                                  action.id
                                }
                                variants={
                                  cardFadeUp
                                }
                                whileHover={
                                  hoverLift
                                }
                                transition={
                                  cardTransition
                                }
                                className="rounded-xl border border-cyan-500/18 bg-[var(--rigmd-card)] p-4"
                              >
                                <div className="flex min-h-full flex-col">
                                  <div className="flex items-start gap-2">
                                    <ShieldCheck
                                      size={
                                        16
                                      }
                                      className="mt-0.5 shrink-0 text-cyan-300"
                                    />

                                    <div>
                                      <h5 className="text-sm font-bold text-white">
                                        {
                                          action.label
                                        }
                                      </h5>

                                      <span className="mt-1 inline-flex rounded border border-cyan-500/20 bg-cyan-500/5 px-2 py-1 text-[9px] font-bold uppercase tracking-wider text-cyan-300">
                                        {
                                          preview.label
                                        }
                                      </span>
                                    </div>
                                  </div>

                                  <p className="mt-3 text-xs leading-relaxed text-gray-400">
                                    {
                                      action.description
                                    }
                                  </p>

                                  <p className="mt-2 text-[11px] leading-relaxed text-slate-600">
                                    Risk:{' '}
                                    {
                                      action.risk
                                    }
                                  </p>

                                  {preview.postTarget && (
                                    <motion.button
                                      type="button"
                                      disabled={
                                        inspecting
                                      }
                                      onClick={() =>
                                        handlePostExecutionInspect(
                                          action,
                                        )
                                      }
                                      whileTap={
                                        buttonTap
                                      }
                                      className="mt-4 w-fit rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-3 py-2 text-[11px] font-bold text-cyan-400 transition-colors hover:bg-cyan-500/10 disabled:opacity-50"
                                    >
                                      {inspecting
                                        ? 'Opening...'
                                        : 'Open Related Tool'}
                                    </motion.button>
                                  )}
                                </div>
                              </motion.div>
                            );
                          },
                        )}
                      </div>
                    </div>
                  )}

                {!isNoActiveIssue(
                  report,
                ) &&
                  remediationActions.length ===
                    0 && (
                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4 text-xs text-gray-500">
                      No backend-approved automatic action is currently available for this result.
                    </div>
                  )}

                {!isNoActiveIssue(
                  report,
                ) &&
                  report.session_id && (
                    <AutonomyRemediationPanel
                      sessionId={
                        report.session_id
                      }
                      diagnosedCategory={
                        report.diagnosed_category
                      }
                      onExecutionComplete={
                        handleAutonomyExecutionComplete
                      }
                    />
                  )}

                {!isNoActiveIssue(
                  report,
                ) &&
                  report.session_id && (
                    <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4">
                      {(() => {
                        const resolutionView =
                          getResolutionView(
                            report.resolution_status,
                          );

                        return (
                          <>
                            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                              <div>
                                <div className="mb-2 flex flex-wrap items-center gap-2">
                                  <h4 className="text-xs font-bold uppercase tracking-wider text-white">
                                    Resolution Status
                                  </h4>

                                  <span
                                    className={`rounded border px-2 py-1 text-[10px] font-bold uppercase tracking-wider ${resolutionView.className}`}
                                  >
                                    {
                                      resolutionView.label
                                    }
                                  </span>
                                </div>

                                <p className="text-xs leading-relaxed text-gray-400">
                                  {report.resolution_summary ||
                                    report.last_action_summary ||
                                    resolutionView.fallback}
                                </p>
                              </div>

                              <motion.button
                                type="button"
                                disabled={
                                  checkingResolution
                                }
                                onClick={
                                  handleCheckResolution
                                }
                                whileTap={
                                  buttonTap
                                }
                                className="rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-4 py-2 text-xs font-bold text-cyan-400 hover:bg-cyan-500/10 disabled:cursor-not-allowed disabled:opacity-50"
                              >
                                {checkingResolution
                                  ? 'Checking if fixed...'
                                  : 'Check if Fixed'}
                              </motion.button>
                            </div>

                            {report.resolution_proof &&
                              report
                                .resolution_proof
                                .length >
                                0 && (
                                <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-2">
                                  {report.resolution_proof.map(
                                    (
                                      item,
                                    ) => (
                                      <div
                                        key={
                                          item.label
                                        }
                                        className={`rounded-lg border p-3 ${getProofCardStyle(
                                          report.action_category,
                                          item.status,
                                        )}`}
                                      >
                                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
                                          {
                                            item.label
                                          }
                                        </p>

                                        <p className="mt-1 text-xs font-semibold text-white">
                                          {
                                            item.value
                                          }
                                        </p>

                                        <p className="mt-1 text-[11px] text-slate-500">
                                          {
                                            item.meaning
                                          }
                                        </p>
                                      </div>
                                    ),
                                  )}
                                </div>
                              )}
                          </>
                        );
                      })()}
                    </div>
                  )}
              </motion.section>
            )}
          </div>

          <aside className="self-start overflow-hidden rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] xl:block">
            <div className="border-b border-[var(--rigmd-border-soft)] p-4">
              <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                Diagnosis Summary
              </p>

              <div className="mt-4 flex items-center gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-cyan-400/22 bg-cyan-400/10 text-cyan-300">
                  <ActiveModeIcon
                    size={20}
                  />
                </div>

                <div className="min-w-0">
                  <h3 className="truncate text-sm font-bold text-white">
                    {activeMode.title}
                  </h3>

                  <p className="text-xs text-slate-500">
                    Selected mode
                  </p>
                </div>
              </div>
            </div>

            <div className="space-y-3 p-4">
              <div
                className={`rounded-lg border p-3.5 ${softTileStyle}`}
              >
                <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                  Scope
                </p>

                {diagnosisMode ===
                'component' ? (
                  selectedComponentDetails.length >
                  0 ? (
                    <div className="mt-3 space-y-2">
                      {selectedComponentDetails
                        .slice(0, 5)
                        .map(
                          (
                            component,
                          ) => (
                            <div
                              key={
                                component.id
                              }
                              className="flex items-center gap-2 rounded-lg border border-teal-300/18 bg-teal-300/[0.045] px-3 py-2 text-xs font-semibold text-slate-200"
                            >
                              <Check
                                size={
                                  13
                                }
                                className="text-teal-300"
                              />

                              {
                                component.title
                              }
                            </div>
                          ),
                        )}

                      {selectedComponentDetails.length >
                        5 && (
                        <p className="text-xs text-slate-500">
                          +
                          {selectedComponentDetails.length -
                            5}{' '}
                          more
                        </p>
                      )}
                    </div>
                  ) : (
                    <p className="mt-2 text-sm text-slate-500">
                      No components selected.
                    </p>
                  )
                ) : diagnosisMode ===
                  'scenario' ? (
                  <div className="mt-3">
                    {selectedScenario ? (
                      <div className="flex items-center gap-2 rounded-lg border border-cyan-300/18 bg-cyan-300/[0.055] px-3 py-3 text-sm font-semibold text-slate-100">
                        <Check
                          size={15}
                          className="text-cyan-300"
                        />

                        {
                          selectedScenario.title
                        }
                      </div>
                    ) : (
                      <p className="text-sm text-slate-500">
                        No scenario selected.
                      </p>
                    )}
                  </div>
                ) : (
                  <div className="mt-3 flex items-center gap-2 rounded-lg border border-cyan-300/18 bg-cyan-300/[0.055] px-3 py-3 text-sm font-semibold text-slate-100">
                    <Check
                      size={15}
                      className="text-cyan-300"
                    />
                    Entire PC
                  </div>
                )}
              </div>

              <div
                className={`rounded-lg border p-3.5 ${softTileStyle}`}
              >
                <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                  Status
                </p>

                <div className="mt-2 flex items-center gap-2">
                  {diagnosisBusy ? (
                    <RefreshCw
                      size={15}
                      className="animate-spin text-cyan-300"
                    />
                  ) : diagnosisStage ===
                    'completed' ? (
                    <CheckCircle2
                      size={15}
                      className="text-emerald-300"
                    />
                  ) : diagnosisStage ===
                    'failed' ? (
                    <AlertTriangle
                      size={15}
                      className="text-red-300"
                    />
                  ) : (
                    <Activity
                      size={15}
                      className="text-slate-500"
                    />
                  )}

                  <p className="text-sm font-semibold text-white">
                    {diagnosisStage ===
                    'idle'
                      ? 'Ready'
                      : diagnosisStage ===
                          'requesting'
                        ? 'Requesting scan'
                        : diagnosisStage ===
                            'scanning'
                          ? 'Scanning PC'
                          : diagnosisStage ===
                              'loading-evidence'
                            ? 'Loading evidence'
                            : diagnosisStage ===
                                'analyzing'
                              ? 'Analyzing'
                              : diagnosisStage ===
                                  'completed'
                                ? 'Scan completed'
                                : 'Scan failed'}
                  </p>
                </div>
              </div>

              {snapshot && (
                <div
                  className={`rounded-lg border p-3.5 ${softTileStyle}`}
                >
                  <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                    Latest Evidence
                  </p>

                  <div className="mt-3 space-y-2">
                    <div className="flex items-center justify-between gap-3 text-xs">
                      <span className="text-slate-500">
                        Memory
                      </span>

                      <span className="font-semibold text-white">
                        {Number(
                          snapshot.hardware.ram
                            .usagePercent,
                        ).toFixed(
                          1,
                        )}
                        %
                      </span>
                    </div>

                    <div className="flex items-center justify-between gap-3 text-xs">
                      <span className="text-slate-500">
                        Storage
                      </span>

                      <span className="font-semibold text-white">
                        {snapshot.hardware
                          .primaryStorageType ||
                          'Detected'}
                      </span>
                    </div>

                    <div className="flex items-center justify-between gap-3 text-xs">
                      <span className="text-slate-500">
                        GPU
                      </span>

                      <span className="max-w-[180px] truncate font-semibold text-white">
                        {snapshot.hardware.gpu
                          .name ||
                          'Detected'}
                      </span>
                    </div>
                  </div>
                </div>
              )}

              {report && (
                <div
                  className={`rounded-lg border p-3.5 ${softTileStyle}`}
                >
                  <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                    Diagnosis
                  </p>

                  <p className="mt-2 text-sm font-bold leading-relaxed text-white">
                    {
                      report.diagnosed_category
                    }
                  </p>

                  <div className="mt-3 grid grid-cols-2 gap-2">
                    <div className="rounded-md border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-2">
                      <p className="text-[9px] font-bold uppercase tracking-wider text-slate-500">
                        Action
                      </p>

                      <p
                        className={`mt-1 text-xs font-bold ${getActionTone(report.action_category).text}`}
                      >
                        {normalizeActionCategory(
                          report.action_category,
                        )}
                      </p>
                    </div>

                    <div className="rounded-md border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-2">
                      <p className="text-[9px] font-bold uppercase tracking-wider text-slate-500">
                        Confidence
                      </p>

                      <p className="mt-1 text-xs font-bold text-white">
                        {
                          report.confidence_label
                        }
                      </p>
                    </div>
                  </div>
                </div>
              )}

              <motion.button
                type="button"
                onClick={
                  resetDiagnosis
                }
                disabled={
                  diagnosisBusy
                }
                whileTap={
                  diagnosisBusy
                    ? undefined
                    : buttonTap
                }
                className="flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-4 py-3 text-sm font-bold text-cyan-300 transition hover:bg-cyan-500/10 disabled:cursor-not-allowed disabled:opacity-50"
              >
                <RefreshCw size={15} />
                Reset Diagnosis
              </motion.button>
            </div>
          </aside>
        </div>
      </motion.div>
    </>
  );
}