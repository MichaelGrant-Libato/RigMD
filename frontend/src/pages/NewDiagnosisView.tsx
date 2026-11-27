import CheckReadings from '../components/CheckReadings';
import { getApiErrorMessage } from '../lib/apiErrorMessage';
import CheckNextSteps from '../components/CheckNextSteps';
import ResultDetails from '../components/ResultDetails';
import WindowsToolGuide from '../components/WindowsToolGuide';
import { startScanMeasurement } from '../lib/performanceRecording';
import { getCheckOverview, getCheckAdvice, getCheckEvidenceLabel, getCheckCoverageNotice } from '../lib/checkPresentation';
import {
  useEffect,
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
  Gauge,
  HardDrive,
  Monitor,
  Power,
  Thermometer,
  RefreshCw,
  ScanSearch,
  Database,
  type LucideIcon,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import AutonomyRemediationPanel from '../components/AutonomyRemediationPanel';
import { getFindingChanges } from '../lib/findingChanges';

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
  ComponentPresenceInfo,
  HardwarePresenceProbe,
} from '../types/rigmd';

import type {
  AutonomyResult,
} from '../services/autonomyService';

import { explanationSourceLabel } from '../lib/diagnosticReadingVisuals';

interface NewDiagnosisViewProps {
  onDiagnosisComplete: (sessionId: string) => void;
  initialComponentIds?: string[];
  onOpenMemoryCheck?: () => void;
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
  metric_id?: string;
  captured_at?: string | null;
  evidence_kind?: string;
  rule_version?: string;
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
  diagnosis_mode?: string;
  component_ids?: string | string[];
  scenario_id?: string;
  diagnosed_category: string;
  action_category: string;
  confidence_label: string;
  ai_explanation: string;
  explanation_source?: string;
  explanation_model?: string;
  recommended_next_step: string;

  component_status?: 'Present' | 'NotPresent' | string;
  target_scope?: string;
  primary_result?: string;
  incidental_warning?: string | null;

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

const DIAGNOSIS_MODES: DiagnosisModeOption[] = [
  {
    id: 'full',
    title: 'Full computer check',
    description:
      'Review available readings across this computer.',
    icon: Stethoscope,
  },

  {
    id: 'component',
    title: 'Check specific parts',
    description:
      'Review findings for the parts you select.',
    icon: Microchip,
  },

  {
    id: 'scenario',
    title: 'Check a problem',
    description:
      'Start with a symptom you have noticed.',
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
        title: 'Processor',
        description:
          'How busy your processor is and how it is performing.',
        icon: Microchip,
      },

      {
        id: 'memory',
        title: 'Memory (RAM)',
        description:
          'Space used by running apps, separate from your saved files.',
        icon: MemoryStick,
      },

      {
        id: 'gpu',
        title: 'Graphics adapter',
        description:
          'Adapter details, available temperature, and related device warnings.',
        icon: Monitor,
      },

      {
        id: 'storage',
        title: 'Storage drives',
        description:
          'Space for Windows and your saved files, and drive-health warnings.',
        icon: HardDrive,
      },
    ],
  },

  {
    group: 'System & Boot',

    items: [
      {
        id: 'os',
        title: 'Windows',
        description:
          'Windows version and operating-system information.',
        icon: Activity,
      },

      {
        id: 'startup',
        title: 'Startup',
        description:
          'Startup-related readings, with Windows boot history available on the result.',
        icon: Power,
      },

      {
        id: 'drivers',
        title: 'Device drivers',
        description:
          'Warnings about connected devices and the software they need.',
        icon: AlertTriangle,
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
        title: 'Connected displays',
        description:
          'Screens Windows reports and available display-related device warnings.',
        icon: Monitor,
      },

    ],
  },
];

const DIAGNOSIS_SCENARIOS: DiagnosisScenario[] = [
  {
    id: 'slow-system',
    title: 'Slow System',
    description:
      'Windows or apps respond slowly most of the time.',
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
      'The Device feels unusually hot or fans become noticeably loud.',
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
      'An app closes unexpectedly or repeatedly fails.',
    icon: Activity,
  },

  {
    id: 'stuttering-freezing',
    title: 'Stuttering / Freezing',
    description:
      'The pointer, video, or desktop briefly pauses and then resumes.',
    icon: Gauge,
  },

  {
    id: 'storage-problem',
    title: 'Storage Problem',
    description:
      'File access, saving, loading, or available drive space appears problematic.',
    icon: HardDrive,
  },
  {
    id: 'rapid-battery-drain',
    title: 'Battery drains quickly',
    description: 'Battery life drops faster than expected while unplugged.',
    icon: Battery,
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

function friendlyActionLabel(action: string | undefined | null) {
  const labels: Record<string, string> = {
    Monitor: 'Watch for changes',
    Maintain: 'Review cleanup options',
    Troubleshoot: 'Review possible actions',
    Escalate: 'Get more help',
  };
  return labels[normalizeActionCategory(action)] ?? 'Review the result';
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

export default function NewDiagnosisView({onDiagnosisComplete, initialComponentIds, onOpenMemoryCheck}: NewDiagnosisViewProps) {
  const [
    diagnosisMode,
    setDiagnosisMode,
  ] =
    useState<DiagnosisMode>(
      initialComponentIds?.length ? 'component' : 'full',
    );

  const [
    selectedComponents,
    setSelectedComponents,
  ] =
    useState<string[]>(initialComponentIds ?? []);

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

  const [
    preflightProbe,
    setPreflightProbe,
  ] =
    useState<HardwarePresenceProbe | null>(
      null,
    );
  const [preflightFailed, setPreflightFailed] = useState(false);

  useEffect(() => {
    let active = true;

    apiGet<HardwarePresenceProbe>(
      '/api/diagnosis/preflight',
      {
        timeout: 8000,
        headers: {
          'X-Client-ID':
            AGENT_ID || 'local',
        },
      },
    )
      .then((res) => {
        if (active && res.data) {
          setPreflightProbe(
            res.data,
          );
          setSelectedComponents(
            (current) =>
              current.filter(
                (id) => {
                  const comp =
                    res.data
                      ?.components?.[
                      id
                    ];
                  return comp
                    ? comp.isSelectable !==
                        false &&
                        comp.status !==
                          'NotPresent'
                    : true;
                },
              ),
          );
        }
      })
      .catch((err) => {
        if (active) setPreflightFailed(true);
        console.warn(
          'Pre-flight hardware probe warning:',
          err,
        );
      });

    return () => {
      active = false;
    };
  }, []);

  const getComponentPresence = (
    componentId: string,
  ):
    | ComponentPresenceInfo
    | undefined => {
    const probe =
      preflightProbe ??
      snapshot?.hardware?.presence;

    if (
      probe?.components?.[
        componentId
      ]
    ) {
      return probe.components[
        componentId
      ];
    }

    return undefined;
  };

  const isComponentDisabled = (
    componentId: string,
  ): boolean => {
    const presence =
      getComponentPresence(
        componentId,
      );

    return Boolean(
      presence &&
        (presence.status ===
          'NotPresent' ||
          presence.isSelectable ===
            false),
    );
  };

  const allComponents =
    useMemo(
      () =>
        COMPONENT_GROUPS.flatMap(
          (group) =>
            group.items,
        ),
      [],
    );

  const selectableComponentIds =
    allComponents
      .map(
        (component) =>
          component.id,
      )
      .filter(
        (id) =>
          !isComponentDisabled(
            id,
          ),
      );

  const activeMode =
    DIAGNOSIS_MODES.find(
      (mode) =>
        mode.id ===
        diagnosisMode,
    ) ??
    DIAGNOSIS_MODES[0];

  const selectedComponentDetails =
    selectedComponents
      .filter(
        (componentId) =>
          !isComponentDisabled(
            componentId,
          ),
      )
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

  const availableScenarios = isComponentDisabled('battery')
    ? DIAGNOSIS_SCENARIOS.filter(scenario => scenario.id !== 'rapid-battery-drain')
    : DIAGNOSIS_SCENARIOS;
  const selectedScenario = availableScenarios.find(scenario => scenario.id === selectedScenarioId);

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
        ? selectedComponentDetails.length >
          0
        : Boolean(selectedScenario);
  const selectionPrompt = diagnosisMode === 'component'
    ? 'Choose at least one part'
    : 'Choose a problem';

  const runButtonLabel =
    diagnosisStage ===
    'requesting'
      ? 'Preparing your check...'
      : diagnosisStage ===
          'scanning'
        ? 'Scanning This Device...'
        : diagnosisStage ===
            'loading-evidence'
          ? 'Loading readings...'
          : diagnosisStage ===
              'analyzing'
            ? 'Checking your results...'
            : diagnosisStage ===
                'completed'
              ? 'Run another check'
              : diagnosisMode ===
                  'full'
                ? 'Check my computer'
                : diagnosisMode ===
                    'component'
                  ? 'Check selected parts'
                  : 'Check this problem';

  const stageDescription =
    diagnosisStage ===
    'requesting'
      ? 'Preparing to check your computer...'
      : diagnosisStage ===
          'scanning'
        ? 'Checking Windows, device sensors, and running apps. This may take a moment.'
        : diagnosisStage ===
            'loading-evidence'
          ? 'The scan is complete. Loading the readings.'
          : diagnosisStage ===
              'analyzing'
            ? 'Reviewing the readings and preparing your results.'
            : '';

  const selectedScopeLabel =
    diagnosisMode === 'full'
      ? 'Available Windows readings'
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
    if (diagnosisBusy || mode === diagnosisMode) {
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
    if (
      diagnosisBusy ||
      isComponentDisabled(
        componentId,
      )
    ) {
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

      const alreadyAllSelected =
        selectableComponentIds.length >
          0 &&
        selectableComponentIds.every(
          (id) =>
            selectedComponents.includes(
              id,
            ),
        );

      setSelectedComponents(
        alreadyAllSelected
          ? []
          : selectableComponentIds,
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

      const performanceRun = startScanMeasurement(diagnosisMode);
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
        setDiagnosisStage('scanning');

        const safeComponentIds =
          diagnosisMode === 'component'
            ? selectedComponents.filter(
                (id) =>
                  !isComponentDisabled(
                    id,
                  ),
              )
            : [];

        const localResponse = await apiPost<DiagnosticReport>(
          '/api/diagnosis/local-scan',
          {
            mode: diagnosisMode,
            componentIds: safeComponentIds,
            scenarioId: diagnosisMode === 'scenario' ? selectedScenario?.id : null,
          },
          {
            headers: { 'X-Client-ID': AGENT_ID || 'local' },
          }
        );

        const automaticReport = localResponse.data;

        if (
          !automaticReport ||
          !automaticReport.diagnosed_category
        ) {
        throw new Error(
          'RigMD completed the scan, but no diagnostic interpretation was returned.',
        );
      }

      if (!automaticReport.session_id?.trim()) {
        throw new Error('The check completed, but its saved result could not be opened. Please try again.');
      }

      setDiagnosisStage(
        'completed',
      );
      performanceRun.saved(automaticReport.session_id);
      onDiagnosisComplete(automaticReport.session_id);

      } catch (err: unknown) {
        performanceRun.failed();
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
          await apiPost<Pick<DiagnosticReport, 'resolution_status' | 'resolution_checked_at' | 'resolution_summary' | 'resolution_proof'>>(
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
          'RigMD could not recheck the current issue status.',
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

        const { data } = await apiPost<{ success: boolean; summary?: string }>(
          '/api/remediation/open-target',
          {
            target:
              targetParam,
          },
        );
        if (data.success !== true) throw new Error(data.summary || 'Windows could not open this tool.');
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
                resolution_checked_at: undefined,
                resolution_proof: [],

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
        await handleCheckResolution();
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
        const { data } = await apiPost<{ success: boolean; summary?: string }>(
          '/api/remediation/open-target',
          {
            target,
          },
        );
        if (data.success !== true) throw new Error(data.summary || 'Windows could not open this tool.');
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
        title="Check My Device"
        subtitle="Check your computer and review suggested next steps"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar min-h-0 flex-1 overflow-y-auto px-4 pb-3 pt-4 lg:px-6"
      >
        <div className="mx-auto w-full max-w-[1320px]">
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
                    <h2 className="text-lg font-bold text-white">Choose a check</h2>
                    <p className="mt-1 text-sm text-slate-400">Review the whole computer, select parts, or start with a symptom.</p>
                  </div>
                </div>
              </div>

              <div className="p-4 sm:p-5">
                <div role="group" aria-label="Check type" className="grid grid-cols-1 gap-3 md:grid-cols-3">
                  {DIAGNOSIS_MODES.map((mode) => {
                    const selected =
                      diagnosisMode === mode.id;

                    const Icon =
                      mode.icon;

                    return (
                      <motion.button
                        key={mode.id}
                        type="button"
                        aria-pressed={selected}
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
                        className={`flex min-w-0 items-start gap-3 rounded-xl border p-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-cyan-300 disabled:cursor-not-allowed disabled:opacity-60 ${
                          selected
                            ? selectedCardStyle
                            : unselectedCardStyle
                        }`}
                      >
                        <span className={`grid size-9 shrink-0 place-items-center rounded-lg border ${iconBoxStyle(selected)}`}>
                          <Icon size={18} aria-hidden="true" />
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="block text-sm font-bold text-white">{mode.title}</span>
                          <span className="mt-1 block text-xs leading-relaxed text-slate-400">{mode.description}</span>
                        </span>
                        {selected && <Check size={15} aria-hidden="true" className="shrink-0 text-cyan-200" />}
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
                          What this check includes
                        </p>

                        <p className="mt-1 text-xs leading-relaxed text-slate-400">
                          Reviews processor activity, memory use, storage space, and available graphics, device, and network readings. The result lists anything RigMD could not verify.
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
                          Choose parts
                        </h3>

                        <p className="mt-1 text-sm text-slate-400">
                          Select the parts you want to review.
                        </p>
                      </div>

                      <button
                        type="button"
                        disabled={diagnosisBusy || selectableComponentIds.length === 0}
                        onClick={selectAllComponents}
                        className="rounded-lg border border-teal-300/30 bg-teal-300/10 px-3 py-2 text-xs font-bold text-teal-200 transition hover:bg-teal-300/15 disabled:cursor-not-allowed disabled:opacity-50"
                      >
                        {selectableComponentIds.length >
                          0 &&
                        selectableComponentIds.every(
                          (id) =>
                            selectedComponents.includes(
                              id,
                            ),
                        )
                          ? 'Clear all'
                          : 'Select available parts'}
                      </button>
                    </div>

                    {preflightFailed && <p role="status" className="mt-3 text-xs text-amber-200">
                      Hardware detection is unavailable. You can still choose parts to check.
                    </p>}
                    <div className="mt-4 space-y-4">
                      {COMPONENT_GROUPS.map(
                        (group) => (
                          <div key={group.group}>
                            <p className="mb-3 text-xs font-bold uppercase tracking-[0.2em] text-slate-400">
                              {group.group}
                            </p>

                            <div role="group" aria-label={group.group} className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                              {group.items.map(
                                (component) => {
                                  const presence =
                                    getComponentPresence(
                                      component.id,
                                    );

                                  const isNotPresent =
                                    isComponentDisabled(
                                      component.id,
                                    );

                                  const selected =
                                    !isNotPresent &&
                                    selectedComponents.includes(
                                      component.id,
                                    );

                                  const Icon =
                                    component.icon;

                                  return (
                                    <motion.button
                                      key={component.id}
                                      type="button"
                                      aria-pressed={selected}
                                      disabled={
                                        diagnosisBusy ||
                                        isNotPresent
                                      }
                                      onClick={() =>
                                        toggleComponent(
                                          component.id,
                                        )
                                      }
                                      whileTap={
                                        diagnosisBusy ||
                                        isNotPresent
                                          ? undefined
                                          : buttonTap
                                      }
                                      className={`flex min-w-0 items-start gap-3 rounded-lg border p-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-cyan-300 disabled:cursor-not-allowed ${
                                        isNotPresent
                                          ? 'border-slate-700/45 bg-[#0c1219] text-slate-500 opacity-75'
                                          : selected
                                            ? selectedCardStyle
                                            : `${unselectedCardStyle} disabled:opacity-60`
                                      }`}
                                    >
                                      <span
                                        className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border ${
                                          isNotPresent
                                            ? 'border-slate-700/50 bg-slate-800/40 text-slate-500'
                                            : iconBoxStyle(
                                                selected,
                                              )
                                        }`}
                                      >
                                        <Icon
                                          size={18}
                                          aria-hidden="true"
                                        />
                                      </span>

                                      <span className="min-w-0 flex-1">
                                        <span className="flex flex-wrap items-start justify-between gap-2">
                                          <span
                                            className={`font-bold ${
                                              isNotPresent
                                                ? 'text-slate-400'
                                                : 'text-white'
                                            }`}
                                          >
                                            {
                                              component.title
                                            }
                                          </span>

                                          {presence?.badge && (isNotPresent || presence.status === 'Unknown') ? (
                                            <span className="shrink-0 rounded-full border border-amber-400/35 bg-amber-400/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider text-amber-300">
                                              {presence.badge}
                                            </span>
                                          ) : presence?.badge ? (
                                            <span className="shrink-0 rounded-full border border-cyan-400/25 bg-cyan-400/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider text-cyan-300">
                                              {
                                                presence.badge
                                              }
                                            </span>
                                          ) : null}
                                          {selected && <Check size={15} aria-hidden="true" className="shrink-0 text-teal-300" />}
                                        </span>

                                        <span className="mt-1 block text-xs leading-relaxed text-slate-400">
                                          {
                                            component.description
                                          }
                                        </span>

                                        {presence?.reason && (isNotPresent || presence.status === 'Unknown') && (
                                          <span className="mt-1.5 block text-[11px] font-medium text-amber-300/85">
                                            {presence.reason}
                                          </span>
                                        )}
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
                        Choose a problem
                      </h3>

                      <p className="mt-1 text-sm text-slate-400">
                        Pick the symptom closest to what you’re seeing.
                      </p>
                    </div>

                    <div role="group" aria-label="Problems to check" className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                      {availableScenarios.map(
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
                              aria-pressed={selected}
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
                              className={`flex min-w-0 items-start gap-3 rounded-lg border p-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-cyan-300 disabled:cursor-not-allowed disabled:opacity-60 ${
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
                                  aria-hidden="true"
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
                                      aria-hidden="true"
                                      className="shrink-0 text-teal-300"
                                    />
                                  )}
                                </span>

                                <span className="mt-1 block text-xs leading-relaxed text-slate-400">
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

                    <p className="mt-1 text-xs leading-relaxed text-slate-400">
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
                <motion.details
                  variants={cardFadeUp}
                  initial="hidden"
                  animate="visible"
                  transition={cardTransition}
                  className={`rounded-2xl border p-5 ${softPanelStyle}`}
                >
                  <summary className="mb-3 font-semibold text-slate-200">View device readings from this check</summary>
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <div className="flex items-center gap-2">
                        <CheckCircle2
                          size={18}
                          className="text-emerald-300"
                        />

                        <h3 className="text-lg font-bold text-white">
                          Device readings
                        </h3>
                      </div>

                      <p className="mt-1 text-xs text-slate-400">
                        Captured{' '}
                        {new Date(
                          snapshot.capturedAt,
                        ).toLocaleString()}
                      </p>
                    </div>

                    <span className="inline-flex w-fit items-center gap-1.5 rounded-lg border border-emerald-400/25 bg-emerald-400/10 px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-emerald-300">
                      <Database size={12} />
                      Saved readings
                    </span>
                  </div>

                  <div className="mt-5 grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Diagnosis Mode
                      </p>

                      <p className="mt-2 text-sm font-bold text-white">
                        {activeMode.title}
                      </p>

                      <p className="mt-1 text-xs leading-relaxed text-slate-400">
                        {selectedScopeLabel}
                      </p>
                    </div>

                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Memory
                      </p>

                      <p className="mt-2 text-xl font-bold text-white">
                        {Number(
                          snapshot.hardware.ram
                            .usagePercent,
                        ).toFixed(1)}
                        %
                      </p>

                      <p className="mt-1 text-xs text-slate-400">
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
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Graphics
                      </p>

                      <p className="mt-2 truncate text-sm font-bold text-white">
                        {snapshot.hardware.gpu
                          .name ||
                          'Detected GPU'}
                      </p>

                      <p className="mt-1 text-xs text-slate-400">
                        Graphics information collected
                      </p>
                    </div>

                    <div className={`rounded-xl border p-4 ${softTileStyle}`}>
                      <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                        Storage Type
                      </p>

                      <p className="mt-2 text-sm font-bold text-white">
                        {snapshot.hardware
                          .primaryStorageType ||
                          'Detected'}
                      </p>

                      <p className="mt-1 text-xs text-slate-400">
                        Storage information collected
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
                            These readings were used for the result below.
                          </p>

                        {commandId && (
                          <p className="mt-2 break-all font-mono text-xs text-slate-600">
                            Scan reference: {commandId}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>
                </motion.details>
              )}

            {report && (
              <motion.section
                variants={cardFadeUp}
                initial="hidden"
                animate="visible"
                transition={cardTransition}
                className="space-y-5"
              >
                <CheckNextSteps check={report} checking={checkingResolution} onCheckAgain={handleCheckResolution} onOpenSession={onDiagnosisComplete}>
                  {!isNoActiveIssue(
                  report,
                ) && report.session_id && (
                    <AutonomyRemediationPanel
                      compact
                      check={report}
                      originalReadings={report.proof}
                      onOpenMemoryCheck={onOpenMemoryCheck}
                      readings={report.resolution_proof?.length ? report.resolution_proof : report.proof}
                      followUpSummary={getFindingChanges(report.proof, report.resolution_proof)?.summary}
                      followUpCheckedAt={report.resolution_checked_at}
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
                </CheckNextSteps>
                <ResultDetails label="Original check" title="Original check">
                <div className="space-y-5" data-testid="original-check-details">
                <div
                  className={`rounded-2xl border p-5 ${getActionTone(report.action_category).border} ${getActionTone(report.action_category).soft}`}
                >
                  <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                    <div className="min-w-0">
                      <div className="mb-3 flex flex-wrap items-center gap-2">
                        <span className="rounded border border-cyan-400/25 bg-cyan-400/10 px-2.5 py-1 text-xs font-bold uppercase tracking-wider text-cyan-300">
                          Check complete
                        </span>

                        {report.target_scope && (
                          <span className="rounded border border-teal-400/30 bg-teal-400/10 px-2.5 py-1 text-xs font-bold uppercase tracking-wider text-teal-200">
                            Scope: {report.target_scope}
                          </span>
                        )}

                        {report.component_status === 'NotPresent' && (
                          <span className="rounded border border-amber-400/35 bg-amber-400/10 px-2.5 py-1 text-xs font-bold uppercase tracking-wider text-amber-300">
                            Component Not Present
                          </span>
                        )}

                        <span
                          className={`rounded border px-2.5 py-1 text-xs font-bold uppercase tracking-wider ${getActionTone(report.action_category).badge}`}
                        >
                          {friendlyActionLabel(
                            report.action_category,
                          )}
                        </span>
                      </div>

                      <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-400">
                        Original check
                      </p>

                      <h3 className="mt-1 text-2xl font-bold leading-tight text-white">
                        {
                          getCheckOverview(report).title
                        }
                      </h3>

                      {getCheckOverview(report).summary && (
                        <p className="mt-2 text-sm leading-relaxed text-slate-200">
                          {getCheckOverview(report).summary}
                        </p>
                      )}

                      <p className="mt-2 text-sm leading-relaxed text-slate-400">{getCheckOverview(report).meaning}</p>
                    {getCheckCoverageNotice(report) && <p className="mt-2 text-sm leading-relaxed text-amber-200">{getCheckCoverageNotice(report)}</p>}

                      {report.confidence_label
                        ?.toLowerCase()
                        .includes('low') && (
                        <p className="mt-2 text-xs text-slate-400">
                          More checks may be needed to confirm the exact cause.
                        </p>
                      )}
                    </div>

                    <div className="grid min-w-[220px] grid-cols-1 gap-2.5 sm:grid-cols-2 lg:grid-cols-1">
                      <div
                        className={`rounded-lg border p-3 ${softTileStyle}`}
                      >
                        <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                          Next step
                        </p>

                        <p
                          className={`mt-1 text-sm font-bold ${getActionTone(report.action_category).text}`}
                        >
                          {friendlyActionLabel(
                            report.action_category,
                          )}
                        </p>
                      </div>

                      <div
                        className={`rounded-lg border p-3 ${softTileStyle}`}
                      >
                        <p className="text-xs font-bold uppercase tracking-wider text-slate-400">
                          Evidence availability
                        </p>

                        <p className="mt-1 text-sm font-bold text-white">
                          {getCheckEvidenceLabel(report)}
                        </p>
                      </div>
                    </div>
                  </div>
                </div>

                <section className="rounded-xl border border-cyan-400/30 bg-cyan-400/10 p-4">
                  <h4 className="text-xs font-bold uppercase tracking-wider text-cyan-200">What to do next</h4>
                  <p className="mt-1.5 text-sm leading-relaxed text-slate-200">
                    {getCheckAdvice(report)}
                  </p>
                </section>

                <details className="rounded-xl border border-[var(--rigmd-border)] p-4">
                  <summary className="cursor-pointer font-semibold text-slate-200">Technical result details</summary>
                  <p className="mt-3 text-xs text-slate-400">Explanation source: {explanationSourceLabel(report.explanation_source, report.explanation_model)}</p>
                  <p className="mt-3 whitespace-pre-line text-sm leading-relaxed text-slate-300">
                    {report.diagnosed_category}: {report.primary_result}{'\n'}{report.ai_explanation || 'An explanation is not available for this result.'}
                  </p>
                </details>

                <CheckReadings check={report} />

                {!isNoActiveIssue(
                  report,
                ) && (
                  <div className="rounded-xl border border-cyan-500/20 bg-cyan-500/[0.045] p-4">
                    <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                      <div className="min-w-0">
                        <p className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-cyan-300">
                          <FileText
                            size={14}
                            className="text-cyan-400"
                          />
                          Windows Tool: {getVerificationTarget()}
                        </p>

                        <p className="mt-1 text-xs leading-relaxed text-slate-400">
                          {
                            getVerificationDescription()
                          }
                        </p>
                      </div>

                    </div>
                    <div className="mt-3"><WindowsToolGuide target={report.verification_target?.target || getVerificationTarget()} onOpen={handleVerifyLocation} busy={inspecting} /></div>
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

                        <p className="mt-1 text-xs text-slate-400">
                          Review each action before choosing whether to run it.
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

                                  <p className="mt-2 text-xs leading-relaxed text-slate-600">
                                    Risk:{' '}
                                    {
                                      action.risk
                                    }
                                  </p>

                                  {preview.postTarget && (
                                    <div className="mt-4"><WindowsToolGuide target={preview.postTarget} onOpen={() => handlePostExecutionInspect(action)} busy={inspecting} /></div>
                                  )}
                                </div>
                              </motion.div>
                            );
                          },
                        )}
                      </div>
                    </div>
                  )}

                </div>
                </ResultDetails>

                {report.session_id && onDiagnosisComplete && (
                  <div className="flex justify-end">
                    <motion.button
                      type="button"
                      whileTap={buttonTap}
                      onClick={() => onDiagnosisComplete(report.session_id!)}
                      className="rounded-lg border border-cyan-500/35 bg-cyan-500/10 px-4 py-2 text-xs font-bold text-cyan-300 transition hover:bg-cyan-500/20"
                    >
                      Open Saved Session in Past Checks →
                    </motion.button>
                  </div>
                )}

              </motion.section>
            )}
          </div>
        </div>
        <div className="pointer-events-none sticky bottom-0 z-20 mx-auto mt-3 w-full max-w-[1320px] pt-2">
          <div role="region" aria-label="Run check"
            className="pointer-events-auto flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--rigmd-border)] bg-[#101821] px-4 py-3 shadow-lg">
            <p role="status" className="min-w-0 flex-1 break-words text-sm font-semibold text-slate-200" title={selectedScopeLabel}>
              {!canRunDiagnosis ? selectionPrompt
                : diagnosisMode === 'component' && selectedComponentDetails.length > 2
                  ? selectedComponentDetails.length + ' parts selected'
                  : selectedScopeLabel}
            </p>
            <div className="flex flex-wrap items-center gap-2">
              {(diagnosisMode !== 'full' || diagnosisStage !== 'idle') && (
                <button type="button" disabled={diagnosisBusy} onClick={resetDiagnosis}
                  className="rounded-lg px-2.5 py-2 text-xs font-semibold text-slate-300 hover:bg-white/5 focus-visible:outline-2 focus-visible:outline-cyan-300 disabled:opacity-50">
                  Start over
                </button>
              )}
              <motion.button type="button" disabled={!canRunDiagnosis || diagnosisBusy}
                onClick={handleRunDiagnosis} whileTap={canRunDiagnosis && !diagnosisBusy ? buttonTap : undefined}
                className="inline-flex min-h-10 items-center justify-center gap-2 rounded-lg bg-[#2dd4bf] px-3 py-2 text-sm font-bold text-[#041014] transition hover:bg-[#5eead4] focus-visible:outline-2 focus-visible:outline-cyan-300 disabled:cursor-not-allowed disabled:bg-slate-700/35 disabled:text-slate-400">
                {diagnosisBusy ? <RefreshCw size={16} aria-hidden="true" className="animate-spin" /> : <Zap size={16} aria-hidden="true" />}
                {runButtonLabel}
              </motion.button>
            </div>
          </div>
        </div>
      </motion.div>
    </>
  );
}
