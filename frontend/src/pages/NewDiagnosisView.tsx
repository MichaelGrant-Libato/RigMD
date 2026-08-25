import { useMemo, useState, ChangeEvent } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import {
  Stethoscope,
  ArrowRight,
  ArrowLeft,
  CheckCircle2,
  AlertTriangle,
  Activity,
  Zap,
  ShieldCheck,
  FileText,
  Info,
  Search,
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
  type LucideIcon,
} from 'lucide-react';
import TopHeader from '../components/TopHeader';
import AutonomyRemediationPanel from '../components/AutonomyRemediationPanel';
import { buttonTap, cardFadeUp, cardTransition, hoverLift, pageFade, pageTransition } from '../lib/motion';
import { apiGet, apiPost } from '../lib/api';
import type { AutonomyResult } from '../services/autonomyService';

interface DiagnosticFormData {
  symptom_type: string;
  affected_activity: string;
  frequency: string;
  severity: string;
  warning_signs: string;
  recent_changes: string;
  system_state: string;
}

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

const CATEGORY_OPTIONS = [
  { value: 'thermal condition', label: 'Computer gets very hot / Loud fan noises' },
  { value: 'os performance issues', label: 'System runs slowly / Freezes / Stutters' },
  { value: 'driver-related issues', label: 'Blue screens / Random restarts / Glitches' },
  { value: 'storage os-level issues', label: 'Files loading slowly / Errors saving files' },
  { value: 'boot and startup issues', label: "Takes forever to turn on / Won't start up" },
  { value: 'display and rendering issues', label: 'Screen flickering / Strange visual lines' },
];

const SYMPTOM_OPTIONS = [
  {
    value: 'thermal condition',
    label: 'Loud Fan Noise or Heat',
    description: 'PC gets hot, fans get loud, or performance drops during heavier use.',
    category: 'Thermal',
    common: true,
  },
  {
    value: 'os performance issues',
    label: 'Stuttering or Lag',
    description: 'System feels slow, freezes, or struggles during normal desktop tasks.',
    category: 'Performance',
    common: true,
  },
  {
    value: 'driver-related issues',
    label: 'Blue Screen or Driver Issue',
    description: 'Blue screens, random restarts, glitches, or device-driver symptoms appear.',
    category: 'Drivers',
    common: true,
  },
  {
    value: 'storage os-level issues',
    label: 'Storage Warning',
    description: 'Files load slowly, saving fails, or disk/storage behavior seems unstable.',
    category: 'Storage',
    common: true,
  },
  {
    value: 'boot and startup issues',
    label: 'Slow Boot or Startup Loop',
    description: 'PC takes too long to start, loops during startup, or has boot trouble.',
    category: 'Boot',
    common: true,
  },
  {
    value: 'display and rendering issues',
    label: 'Display or Visual Issue',
    description: 'Screen flickers, shows visual artifacts, or has rendering problems.',
    category: 'Display',
    common: true,
  },
];

const SYMPTOM_FILTERS = ['All', 'Common', ...Array.from(new Set(SYMPTOM_OPTIONS.map((option) => option.category)))];

type DiagnosisMode = 'full' | 'component' | 'scenario';

const DIAGNOSIS_MODES: {
  id: DiagnosisMode;
  title: string;
  action: string;
  description: string;
  icon: LucideIcon;
}[] = [
  {
    id: 'full',
    title: 'Full Guided Diagnosis',
    action: 'Start Diagnosis',
    description: "Best when you're unsure what's wrong. Pick the closest symptom and RigMD guides the rest.",
    icon: Stethoscope,
  },
  {
    id: 'component',
    title: 'Diagnose by Component',
    action: 'Select Components',
    description: 'Choose specific subsystems to inspect. RigMD maps them into its diagnostic categories.',
    icon: Microchip,
  },
  {
    id: 'scenario',
    title: 'Diagnose by Scenario',
    action: 'Pick a Scenario',
    description: 'Quick-start from a familiar problem such as slow boot, overheating, or blue screens.',
    icon: Layers,
  },
];

const COMPONENT_GROUPS: {
  group: string;
  items: {
    id: string;
    title: string;
    description: string;
    symptomType: string;
    icon: LucideIcon;
  }[];
}[] = [
  {
    group: 'Core Hardware',
    items: [
      { id: 'cpu', title: 'CPU / Processor', description: 'Utilization, throttling, stability', symptomType: 'os performance issues', icon: Microchip },
      { id: 'memory', title: 'RAM / Memory', description: 'Usage, channel config, pressure', symptomType: 'os performance issues', icon: MemoryStick },
      { id: 'gpu', title: 'GPU / Graphics', description: 'Driver, load, output health', symptomType: 'display and rendering issues', icon: Monitor },
      { id: 'storage', title: 'Storage / SSD / HDD', description: 'Usage, latency, file errors', symptomType: 'storage os-level issues', icon: HardDrive },
    ],
  },
  {
    group: 'System & Boot',
    items: [
      { id: 'os', title: 'Operating System', description: 'Windows behavior, updates, system errors', symptomType: 'os performance issues', icon: Activity },
      { id: 'startup', title: 'Startup / Boot', description: 'Boot time, startup loops, black screens', symptomType: 'boot and startup issues', icon: Power },
      { id: 'drivers', title: 'Drivers', description: 'Conflicts, outdated drivers, device warnings', symptomType: 'driver-related issues', icon: AlertTriangle },
      { id: 'thermal', title: 'Thermals / Fan', description: 'Heat, fan noise, performance drops', symptomType: 'thermal condition', icon: Fan },
      { id: 'battery', title: 'Battery / Power', description: 'Power behavior, shutdown, charging', symptomType: 'boot and startup issues', icon: Battery },
    ],
  },
  {
    group: 'Connectivity & Devices',
    items: [
      { id: 'network', title: 'Network / Internet', description: 'Drops, slow connection, no internet', symptomType: 'driver-related issues', icon: Wifi },
      { id: 'display', title: 'Display', description: 'Flicker, no signal, visual glitches', symptomType: 'display and rendering issues', icon: Monitor },
      { id: 'peripherals', title: 'USB / Peripherals', description: 'Keyboard, mouse, ports, external devices', symptomType: 'driver-related issues', icon: Usb },
      { id: 'audio', title: 'Audio', description: 'Sound output or device issues', symptomType: 'driver-related issues', icon: Volume2 },
    ],
  },
];

const DIAGNOSIS_SCENARIOS: {
  id: string;
  value: string;
  title: string;
  description: string;
  icon: LucideIcon;
}[] = [
  {
    id: 'slow-system',
    value: 'os performance issues',
    title: 'Slow System',
    description: 'General sluggishness, lag, or heavy CPU and memory pressure.',
    icon: Gauge,
  },
  {
    id: 'slow-boot',
    value: 'boot and startup issues',
    title: 'Slow Boot',
    description: 'Long startup, restart delays, or stuck loading screens.',
    icon: Power,
  },
  {
    id: 'blue-screen-crash',
    value: 'driver-related issues',
    title: 'Blue Screen / Crash',
    description: 'Crashes, stop codes, device glitches, or restarts after driver changes.',
    icon: AlertTriangle,
  },
  {
    id: 'driver-error',
    value: 'driver-related issues',
    title: 'Driver Error',
    description: 'Device warnings, missing drivers, or hardware that stops responding.',
    icon: AlertTriangle,
  },
  {
    id: 'no-display',
    value: 'display and rendering issues',
    title: 'No Display',
    description: 'Black screen, no signal, flicker, or unstable display behavior.',
    icon: Monitor,
  },
  {
    id: 'overheating-loud-fan',
    value: 'thermal condition',
    title: 'Overheating / Loud Fan',
    description: 'High temperatures, fan ramping, and performance drops under load.',
    icon: Thermometer,
  },
  {
    id: 'network-problem',
    value: 'driver-related issues',
    title: 'Network Problem',
    description: 'Dropped connection, slow internet, or adapter problems.',
    icon: Wifi,
  },
  {
    id: 'app-crashes',
    value: 'storage os-level issues',
    title: 'App Crashes',
    description: 'Apps close unexpectedly, freeze, or fail while loading files.',
    icon: HardDrive,
  },
  {
    id: 'stuttering-freezing',
    value: 'os performance issues',
    title: 'Stuttering / Freezing',
    description: 'Mouse, audio, or apps hitch during normal use.',
    icon: Gauge,
  },
  {
    id: 'storage-problem',
    value: 'storage os-level issues',
    title: 'Storage Problem',
    description: 'Slow file access, save failures, or possible disk pressure.',
    icon: HardDrive,
  },
];

const SEVERITY_OPTIONS = [
  { value: 'Low', label: 'Low', description: 'Annoying, but the computer is still usable.' },
  { value: 'Medium', label: 'Medium', description: 'Noticeable slowdowns or interruptions.' },
  { value: 'High', label: 'High', description: 'Hard to use the computer normally.' },
];

const FREQUENCY_OPTIONS = [
  { value: 'rarely', label: 'Rarely', description: 'Only happened once or twice.' },
  { value: 'intermittent', label: 'Sometimes', description: 'Comes and goes during normal use.' },
  { value: 'frequent', label: 'Frequently', description: 'Happens almost every day.' },
  { value: 'always', label: 'Constantly', description: 'The issue keeps happening.' },
];

const DEFAULT_FULL_GUIDED_SYMPTOM = 'os performance issues';

const QUESTION_BRANCHES: Record<string, {
  step2Label: string;
  activities: { value: string; label: string }[];
  warningSignsLabel: string;
  warningSigns: { value: string; label: string }[];
  recentChangesLabel: string;
  recentChanges: { value: string; label: string }[];
}> = {
  'thermal condition': {
    step2Label: 'When does the desktop feel the hottest or sound the loudest?',
    activities: [
      { value: 'gaming', label: 'Playing high-end video games' },
      { value: 'browsing', label: 'Streaming HD videos / Multitasking online' },
      { value: 'working', label: 'Compiling code / Heavy school workloads' },
      { value: 'idle', label: 'Just sitting on the desktop doing nothing' },
    ],
    warningSignsLabel: 'What do you notice happening right before it overheats?',
    warningSigns: [
      { value: 'loud fan noise', label: 'Fans suddenly started spinning loudly' },
      { value: 'stuttering', label: 'The screen stuttered or lagged badly' },
      { value: 'none', label: 'Nothing unusual, it just builds up heat' },
    ],
    recentChangesLabel: 'Have there been any structural or software changes?',
    recentChanges: [
      { value: 'none', label: 'No recent updates or changes' },
      { value: 'hardware upgrade', label: 'Upgraded internal parts (RAM, Storage)' },
      { value: 'windows update', label: 'A recent Windows operating system update' },
    ],
  },
  'os performance issues': {
    step2Label: 'What usually triggers the slowdown?',
    activities: [
      { value: 'working', label: 'Opening multiple browser tabs or document apps' },
      { value: 'gaming', label: 'Running heavy apps or games' },
      { value: 'startup', label: 'Right after turning on the PC' },
      { value: 'browsing', label: 'Watching high-resolution videos' },
    ],
    warningSignsLabel: 'Did you notice any of these signs?',
    warningSigns: [
      { value: 'stuttering', label: 'The mouse cursor hitches or freezes for a second' },
      { value: 'error message', label: 'An application alert popped up on screen' },
      { value: 'none', label: 'No warning sign, everything just drops speed' },
    ],
    recentChangesLabel: 'Did anything change recently?',
    recentChanges: [
      { value: 'new software installed', label: 'Installed a new app' },
      { value: 'windows update', label: 'Windows installed automatic updates' },
      { value: 'none', label: 'Nothing changed' },
    ],
  },
  'driver-related issues': {
    step2Label: 'What triggers the crash or device problem?',
    activities: [
      { value: 'gaming', label: 'Launching games or graphics-heavy apps' },
      { value: 'idle', label: 'Randomly while the PC is idle' },
      { value: 'startup', label: 'During startup' },
      { value: 'working', label: 'Connecting external devices' },
    ],
    warningSignsLabel: 'Did you notice any of these signs?',
    warningSigns: [
      { value: 'error message', label: 'A blue screen with an error code' },
      { value: 'stuttering', label: 'Audio loops or stutters' },
      { value: 'black screen', label: 'The screen goes black' },
    ],
    recentChangesLabel: 'Did anything change recently?',
    recentChanges: [
      { value: 'driver update', label: 'Updated graphics driver' },
      { value: 'windows update', label: 'Installed a Windows update' },
      { value: 'none', label: 'Nothing was changed' },
    ],
  },
  'storage os-level issues': {
    step2Label: 'When do storage or file problems show up?',
    activities: [
      { value: 'working', label: 'Moving files, opening folders, or saving work' },
      { value: 'startup', label: 'Right after startup' },
      { value: 'gaming', label: 'Loading games or large files' },
    ],
    warningSignsLabel: 'Did you notice any of these signs?',
    warningSigns: [
      { value: 'error message', label: 'An "Access Denied" or "File Not Found" warning' },
      { value: 'stuttering', label: 'The PC freezes while opening files' },
      { value: 'none', label: 'No warning, saving just takes ages' },
    ],
    recentChangesLabel: 'Did anything change recently?',
    recentChanges: [
      { value: 'hardware upgrade', label: 'Installed or changed a drive' },
      { value: 'new software installed', label: 'Installed a large app or game' },
      { value: 'none', label: 'No changes' },
    ],
  },
  'boot and startup issues': {
    step2Label: 'When does the startup problem happen?',
    activities: [
      { value: 'startup', label: 'At the loading logo' },
      { value: 'idle', label: 'While desktop apps are loading' },
      { value: 'resume', label: 'When waking from sleep' },
    ],
    warningSignsLabel: 'Did you notice any of these signs?',
    warningSigns: [
      { value: 'error message', label: 'A "no boot device" or startup error' },
      { value: 'loud fan noise', label: 'Fans spin loudly right away' },
      { value: 'black screen', label: 'The screen stays black' },
    ],
    recentChangesLabel: 'Did anything change recently?',
    recentChanges: [
      { value: 'windows update', label: 'Installed a Windows update' },
      { value: 'hardware upgrade', label: 'Installed or changed hardware' },
      { value: 'none', label: 'No changes' },
    ],
  },
  'display and rendering issues': {
    step2Label: 'When do display problems show up?',
    activities: [
      { value: 'gaming', label: 'While playing games or using graphics-heavy apps' },
      { value: 'browsing', label: 'While watching videos' },
      { value: 'idle', label: 'Even when the PC is idle' },
    ],
    warningSignsLabel: 'Did you notice any of these signs?',
    warningSigns: [
      { value: 'stuttering', label: 'Screen tearing or visual blocks' },
      { value: 'error message', label: 'A notification saying "Display driver stopped responding"' },
      { value: 'screen flicker', label: 'The screen flickers off and back on randomly' },
    ],
    recentChangesLabel: 'Did anything change recently?',
    recentChanges: [
      { value: 'driver update', label: 'Updated graphics driver' },
      { value: 'hardware upgrade', label: 'Changed monitor or display cables' },
      { value: 'none', label: 'No changes' },
    ],
  },
};

function isNoActiveIssue(report: DiagnosticReport | null) {
  return report?.diagnosed_category.toLowerCase() === 'no active issue detected';
}

function normalizeActionCategory(action: string | undefined | null) {
  const value = (action ?? '').toLowerCase();

  if (value.includes('maintain')) return 'Maintain';
  if (value.includes('monitor')) return 'Monitor';
  if (value.includes('troubleshoot')) return 'Troubleshoot';
  if (value.includes('escalate')) return 'Escalate';

  return action || 'Action';
}

function getActionTone(action: string | undefined | null) {
  const normalized = normalizeActionCategory(action);

  if (normalized === 'Maintain') {
    return {
      badge: 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300',
      border: 'border-emerald-400/30',
      soft: 'bg-emerald-400/[0.055]',
      text: 'text-emerald-300',
      button: 'border-emerald-400/35 bg-emerald-400/10 text-emerald-200 hover:bg-emerald-400/15',
      accent: 'border-l-emerald-400',
    };
  }

  if (normalized === 'Monitor') {
    return {
      badge: 'border-cyan-400/35 bg-cyan-400/10 text-cyan-300',
      border: 'border-cyan-400/30',
      soft: 'bg-cyan-400/[0.055]',
      text: 'text-cyan-300',
      button: 'border-cyan-400/35 bg-cyan-400/10 text-cyan-200 hover:bg-cyan-400/15',
      accent: 'border-l-cyan-400',
    };
  }

  if (normalized === 'Troubleshoot') {
    return {
      badge: 'border-amber-400/35 bg-amber-400/10 text-amber-300',
      border: 'border-amber-400/30',
      soft: 'bg-amber-400/[0.055]',
      text: 'text-amber-300',
      button: 'border-amber-400/35 bg-amber-400/10 text-amber-200 hover:bg-amber-400/15',
      accent: 'border-l-amber-400',
    };
  }

  if (normalized === 'Escalate') {
    return {
      badge: 'border-red-400/35 bg-red-400/10 text-red-300',
      border: 'border-red-400/30',
      soft: 'bg-red-400/[0.055]',
      text: 'text-red-300',
      button: 'border-red-400/35 bg-red-400/10 text-red-200 hover:bg-red-400/15',
      accent: 'border-l-red-400',
    };
  }

  return {
    badge: 'border-slate-500/35 bg-slate-500/10 text-slate-300',
    border: 'border-slate-500/30',
    soft: 'bg-slate-500/[0.055]',
    text: 'text-slate-300',
    button: 'border-cyan-400/35 bg-cyan-400/10 text-cyan-200 hover:bg-cyan-400/15',
    accent: 'border-l-slate-400',
  };
}

function getProofCardStyle(action: string | undefined | null, status?: string) {
  const statusValue = (status ?? '').toLowerCase();

  if (normalizeActionCategory(action) === 'Escalate' || statusValue === 'critical') return 'border-red-500/30 bg-red-500/5';
  if (normalizeActionCategory(action) === 'Troubleshoot' || statusValue === 'high' || statusValue === 'elevated' || statusValue === 'detected') return 'border-amber-500/30 bg-amber-500/5';
  if (normalizeActionCategory(action) === 'Monitor') return 'border-cyan-500/30 bg-cyan-500/5';
  return 'border-emerald-500/25 bg-emerald-500/5';
}

function analyzeSystemStateNote(note: string) {
  const text = note.toLowerCase().trim();
  const hasAny = (words: string[]) => words.some((word) => text.includes(word));

  return {
    raw_note: note.slice(0, 500),
    mentions_black_screen: hasAny(['black screen', 'screen stays black', 'screen remains black', 'no display']),
    mentions_no_boot: hasAny(['no boot', "won't boot", 'wont boot', 'boot loop', 'stuck on logo', 'startup repair']),
    mentions_blue_screen: hasAny(['blue screen', 'bsod', 'stop code']),
    mentions_flicker: hasAny(['flicker', 'flickering', 'screen tearing', 'visual glitch', 'display glitch']),
    mentions_heat: hasAny(['hot', 'overheat', 'overheating', 'very warm', 'loud fan', 'fan loud']),
    mentions_slow: hasAny(['slow', 'lag', 'lagging', 'freeze', 'freezing', 'stutter', 'stuttering']),
    mentions_storage: hasAny(['file not found', 'access denied', 'saving', 'save file', 'drive', 'disk', 'storage']),
    mentions_recent_update: hasAny(['after update', 'windows update', 'driver update', 'updated']),
  };
}

function getActionPreview(action: RemediationAction, _report?: DiagnosticReport | null) {
  const previews: Record<string, {
    mode: 'automated' | 'assisted' | 'read-only';
    buttonLabel: string;
    title: string;
    plainSummary: string;
    before: string;
    during: string;
    after: string;
    included: string[];
    excluded: string[];
    proof: string[];
    postTarget?: string;
  }> = {
    clear_user_temp_files: {
      mode: 'automated',
      buttonLabel: 'Run Cleanup',
      title: 'Clear temporary files',
      plainSummary: 'RigMD will remove only temporary app files that Windows says are safe to delete.',
      before: 'RigMD checks your temporary folders.',
      during: 'It deletes only unlocked temporary files.',
      after: 'It reports how much space was cleared and how many locked files were skipped.',
      included: ['User TEMP folder', 'User TMP folder', 'Temporary files apps no longer need'],
      excluded: ['Documents', 'Downloads', 'Desktop', 'Pictures', 'Games', 'Program Files', 'Windows system files', 'Drivers', 'Registry'],
      proof: ['Space cleared', 'Items removed', 'Locked files skipped'],
      postTarget: 'storage_settings',
    },
    open_task_manager: {
      mode: 'assisted',
      buttonLabel: 'Open Tool',
      title: 'Open Task Manager',
      plainSummary: 'RigMD will open Task Manager so you can see which apps are using memory, CPU, or disk.',
      before: 'RigMD does not close anything.',
      during: 'It only opens the Windows tool.',
      after: 'You can inspect the running apps yourself.',
      included: ['Task Manager', 'Processes tab', 'CPU, memory, and disk usage'],
      excluded: ['No apps are closed automatically', 'No files are deleted', 'No settings are changed'],
      proof: ['Task Manager opened successfully'],
      postTarget: 'task_manager',
    },
    open_startup_apps: {
      mode: 'assisted',
      buttonLabel: 'Open Tool',
      title: 'Open Startup Apps',
      plainSummary: 'RigMD will open the Windows page that controls which apps start with your computer.',
      before: 'RigMD does not disable any app by itself.',
      during: 'It only opens Windows Startup Apps settings.',
      after: 'You choose what to turn off, if anything.',
      included: ['Windows Startup Apps settings'],
      excluded: ['No app is disabled automatically', 'No registry edits', 'No file deletion'],
      proof: ['Startup Apps settings opened successfully'],
      postTarget: 'startup_apps',
    },
    open_storage_settings: {
      mode: 'assisted',
      buttonLabel: 'Open Tool',
      title: 'Open Storage Settings',
      plainSummary: 'RigMD will open Windows Storage Settings so you can inspect what is using disk space.',
      before: 'RigMD does not delete anything.',
      during: 'It only opens the Windows storage screen.',
      after: 'You can review storage usage yourself.',
      included: ['Windows Storage Settings', 'Drive space overview'],
      excluded: ['No files are deleted automatically', 'No repair command is run'],
      proof: ['Storage Settings opened successfully'],
      postTarget: 'storage_settings',
    },
    chkdsk_readonly: {
      mode: 'read-only',
      buttonLabel: 'Run Read-Only Scan',
      title: 'Run read-only disk scan',
      plainSummary: 'RigMD will ask Windows to check the drive and report problems, without repairing or changing files.',
      before: 'RigMD starts a read-only Windows disk check.',
      during: 'Windows scans the C: drive.',
      after: 'RigMD reports whether Windows found obvious file system issues.',
      included: ['Read-only disk scan', 'Scan result text'],
      excluded: ['No repair flag', 'No bad-sector repair scan', 'No file deletion', 'No file movement'],
      proof: ['Scan completed or blocked', 'Whether errors were reported'],
      postTarget: 'storage_settings',
    },
    show_gpu_reset_shortcut: {
      mode: 'assisted',
      buttonLabel: 'Show Shortcut',
      title: 'Show display reset shortcut',
      plainSummary: 'RigMD will show the safe Windows keyboard shortcut for refreshing the display driver.',
      before: 'RigMD does not change your driver.',
      during: 'It shows the shortcut only.',
      after: 'You can choose whether to press the shortcut yourself.',
      included: ['Windows + Ctrl + Shift + B instruction'],
      excluded: ['No driver uninstall', 'No driver update', 'No automatic restart', 'No settings change'],
      proof: ['Shortcut instruction shown successfully'],
    },
    open_backup_settings: {
      mode: 'assisted',
      buttonLabel: 'Open Tool',
      title: 'Open Backup Settings',
      plainSummary: 'RigMD will open Windows Backup Settings so you can protect important files before deeper storage troubleshooting.',
      before: 'RigMD does not create a backup automatically.',
      during: 'It only opens the Windows backup page.',
      after: 'You choose what to back up.',
      included: ['Windows Backup Settings'],
      excluded: ['No files are moved', 'No files are deleted', 'No backup is created automatically'],
      proof: ['Backup Settings opened successfully'],
      postTarget: 'backup_settings',
    },
    open_device_manager: {
      mode: 'assisted',
      buttonLabel: 'Open Tool',
      title: 'Open Device Manager',
      plainSummary: 'RigMD will open Device Manager so you can look for warning icons or driver problems.',
      before: 'RigMD does not change drivers.',
      during: 'It only opens the Windows device list.',
      after: 'You can inspect device status yourself.',
      included: ['Device Manager', 'Device warning icons', 'Driver status view'],
      excluded: ['No driver update', 'No rollback', 'No uninstall'],
      proof: ['Device Manager opened successfully'],
      postTarget: 'device_manager',
    },
  };

  return previews[action.id] ?? {
    mode: 'assisted',
    buttonLabel: 'Open Tool',
    title: action.label,
    plainSummary: action.description,
    before: 'RigMD prepares the related Windows action.',
    during: 'RigMD performs only this selected action and nothing else.',
    after: 'RigMD reports the result.',
    included: [action.description],
    excluded: ['No unrelated system changes'],
    proof: ['Action result'],
  };
}

function getResolutionView(status?: string) {
  const value = (status ?? '').toLowerCase();

  if (value === 'resolved') {
    return {
      label: 'Resolved',
      className: 'border-emerald-500/30 bg-emerald-500/5 text-emerald-300',
      fallback: 'The issue is no longer detected in the latest live scan.',
    };
  }

  if (value === 'still_active' || value === 'unresolved') {
    return {
      label: 'Unresolved',
      className: 'border-amber-500/30 bg-amber-500/5 text-amber-300',
      fallback: 'The issue is still detected after checking the latest live scan.',
    };
  }

  if (value === 'needs_recheck') {
    return {
      label: 'Needs Recheck',
      className: 'border-cyan-500/30 bg-cyan-500/5 text-cyan-300',
      fallback: 'A safe action was recorded. Check the latest live scan to confirm whether the issue improved.',
    };
  }

  return {
    label: 'Not Checked',
    className: 'border-slate-500/30 bg-slate-500/5 text-slate-300',
    fallback: 'Use Check if Fixed after a safe action or manual inspection.',
  };
}

function getApiErrorMessage(err: any, fallback: string) {
  const detail = err?.response?.data?.detail;
  const message = (() => {
    if (typeof detail === 'string') return detail;

    if (Array.isArray(detail)) {
      return detail
        .map((item) => item?.msg || item?.message || JSON.stringify(item))
        .join(' ');
    }

    if (detail && typeof detail === 'object') {
      return detail.msg || detail.message || JSON.stringify(detail);
    }

    return err?.message || fallback;
  })();

  if (/at\s+\S+\s+\(|Traceback|System\./.test(message)) {
    return fallback;
  }

  return message;
}

export default function NewDiagnosisView() {
  const [step, setStep] = useState<number>(1);
  const [diagnosisMode, setDiagnosisMode] = useState<DiagnosisMode>('full');
  const [selectedComponents, setSelectedComponents] = useState<string[]>([]);
  const [selectedScenarioId, setSelectedScenarioId] = useState<string>('');
  const [symptomSearch, setSymptomSearch] = useState('');
  const [activeSymptomFilter, setActiveSymptomFilter] = useState('All');

  const [formData, setFormData] = useState<DiagnosticFormData>({
    symptom_type: DEFAULT_FULL_GUIDED_SYMPTOM,
    affected_activity: '',
    frequency: '',
    severity: '',
    warning_signs: '',
    recent_changes: '',
    system_state: '',
  });

  const [loading, setLoading] = useState<boolean>(false);
  const [checkingResolution, setCheckingResolution] = useState(false);
  const [inspecting, setInspecting] = useState<boolean>(false);
  const [report, setReport] = useState<DiagnosticReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [remediationActions, setRemediationActions] = useState<RemediationAction[]>([]);

  const currentBranch = QUESTION_BRANCHES[formData.symptom_type] || QUESTION_BRANCHES['thermal condition'];
  const allComponents = useMemo(() => COMPONENT_GROUPS.flatMap((group) => group.items), []);
  const activeMode = DIAGNOSIS_MODES.find((mode) => mode.id === diagnosisMode) ?? DIAGNOSIS_MODES[0];
  const ActiveModeIcon = activeMode.icon;
  const selectedComponentDetails = selectedComponents
    .map((componentId) => allComponents.find((component) => component.id === componentId))
    .filter(Boolean) as typeof allComponents;
  const selectedScenario = DIAGNOSIS_SCENARIOS.find((scenario) => scenario.id === selectedScenarioId);

  const selectedSymptom =
    SYMPTOM_OPTIONS.find((item) => item.value === formData.symptom_type)?.label ??
    CATEGORY_OPTIONS.find((item) => item.value === formData.symptom_type)?.label ??
    '';
  const selectedActivity = formData.affected_activity
    ? currentBranch.activities.find((item) => item.value === formData.affected_activity)?.label ?? formData.affected_activity
    : '';
  const selectedWarning = formData.warning_signs
    ? currentBranch.warningSigns.find((item) => item.value === formData.warning_signs)?.label ?? formData.warning_signs
    : '';
  const selectedChange = formData.recent_changes
    ? currentBranch.recentChanges.find((item) => item.value === formData.recent_changes)?.label ?? formData.recent_changes
    : '';

  const filteredSymptomOptions = useMemo(() => {
    const query = symptomSearch.trim().toLowerCase();

    return SYMPTOM_OPTIONS.filter((option) => {
      const matchesFilter =
        activeSymptomFilter === 'All' ||
        (activeSymptomFilter === 'Common' && option.common) ||
        option.category === activeSymptomFilter;

      if (!matchesFilter) return false;
      if (!query) return true;

      return (
        option.label.toLowerCase().includes(query) ||
        option.description.toLowerCase().includes(query) ||
        option.category.toLowerCase().includes(query) ||
        option.value.toLowerCase().includes(query)
      );
    });
  }, [activeSymptomFilter, symptomSearch]);

  const selectedDiagnosisLabel =
    diagnosisMode === 'component'
      ? selectedComponentDetails.map((component) => component.title).join(', ')
      : diagnosisMode === 'scenario'
        ? selectedScenario?.title ?? ''
        : selectedSymptom;
  const compactComponentSummary = (limit = 3) => {
    if (selectedComponentDetails.length === 0) return '';

    const names = selectedComponentDetails.slice(0, limit).map((component) => component.title).join(', ');
    const extra = selectedComponentDetails.length - limit;

    return extra > 0 ? `${names} + ${extra} more` : names;
  };

  const answerSummary = [
    { label: 'Mode', value: activeMode.title },
    { label: diagnosisMode === 'component' ? 'Component Focus' : diagnosisMode === 'scenario' ? 'Scenario' : 'Symptom', value: diagnosisMode === 'component' ? compactComponentSummary(3) : selectedDiagnosisLabel || selectedSymptom },
    { label: 'Trigger / Activity', value: selectedActivity },
    { label: 'Frequency', value: formData.frequency },
    { label: 'Severity', value: formData.severity },
    { label: 'Warning Sign', value: selectedWarning },
    { label: 'Recent Change', value: selectedChange },
    { label: 'Description', value: formData.system_state.trim() },
  ];
  const reviewSections = [
    {
      title: 'Diagnosis Scope',
      items: [
        { label: 'Mode', value: activeMode.title },
        {
          label: diagnosisMode === 'component' ? 'Component Focus' : diagnosisMode === 'scenario' ? 'Scenario' : 'Starting Symptom',
          value: diagnosisMode === 'component' ? compactComponentSummary(3) : selectedDiagnosisLabel || selectedSymptom,
        },
      ],
    },
    {
      title: 'Behavior Details',
      items: [
        { label: 'Trigger / Activity', value: selectedActivity },
        { label: 'Frequency', value: formData.frequency },
        { label: 'Severity', value: formData.severity },
      ],
    },
    {
      title: 'Context',
      items: [
        { label: 'Warning Sign', value: selectedWarning },
        { label: 'Recent Change', value: selectedChange },
        { label: 'Description', value: formData.system_state.trim() },
      ],
    },
  ];

  const completedAnswerCount = answerSummary.filter((item) => Boolean(item.value)).length;

  const canContinue = () => {
    if (step === 1) {
      if (diagnosisMode === 'component') return selectedComponents.length > 0;
      if (diagnosisMode === 'scenario') return Boolean(selectedScenarioId);
      return Boolean(formData.symptom_type);
    }
    if (step === 2) return Boolean(formData.severity && formData.frequency && formData.affected_activity);
    if (step === 3) return Boolean(formData.warning_signs && formData.recent_changes);
    if (step === 4) return Boolean(formData.symptom_type && formData.severity && formData.frequency && formData.affected_activity && formData.warning_signs && formData.recent_changes);
    return true;
  };

  const getDisabledReason = () => {
    if (canContinue()) return '';

    if (step === 1) {
      if (diagnosisMode === 'component') return 'Select at least one subsystem to continue.';
      if (diagnosisMode === 'scenario') return 'Pick one scenario to continue.';
      return 'Choose a starting symptom to continue.';
    }

    if (step === 2) {
      if (!formData.severity) return 'Choose how disruptive the issue is.';
      if (!formData.frequency) return 'Choose how often the issue happens.';
      if (!formData.affected_activity) return 'Select what usually triggers the issue.';
    }

    if (step === 3) {
      if (!formData.warning_signs) return 'Select a warning sign or choose "No warning sign."';
      if (!formData.recent_changes) return 'Select a recent change or choose "Nothing changed."';
    }

    if (step === 4) return 'Complete the required fields before running diagnosis.';

    return '';
  };

  const selectSymptomType = (nextCategory: string) => {
    if (nextCategory === formData.symptom_type) return;

    setFormData({
      symptom_type: nextCategory,
      affected_activity: '',
      frequency: '',
      severity: '',
      warning_signs: '',
      recent_changes: '',
      system_state: '',
    });
  };

  const selectDiagnosisMode = (mode: DiagnosisMode) => {
    setDiagnosisMode(mode);
    setSelectedComponents([]);
    setSelectedScenarioId('');
    setSymptomSearch('');
    setActiveSymptomFilter('All');
    setFormData({
      symptom_type: mode === 'full' ? DEFAULT_FULL_GUIDED_SYMPTOM : '',
      affected_activity: '',
      frequency: '',
      severity: '',
      warning_signs: '',
      recent_changes: '',
      system_state: '',
    });
  };

  const chooseScenario = (scenarioId: string) => {
    const scenario = DIAGNOSIS_SCENARIOS.find((item) => item.id === scenarioId);
    if (!scenario) return;

    setDiagnosisMode('scenario');
    setSelectedComponents([]);
    setSelectedScenarioId(scenario.id);
    selectSymptomType(scenario.value);
  };

  const toggleComponent = (componentId: string) => {
    const component = allComponents.find((item) => item.id === componentId);
    if (!component) return;

    setDiagnosisMode('component');
    setSelectedScenarioId('');
    setSelectedComponents((current) => {
      const exists = current.includes(componentId);
      const next = exists ? current.filter((item) => item !== componentId) : [...current, componentId];
      const nextComponents = next
        .map((item) => allComponents.find((candidate) => candidate.id === item))
        .filter(Boolean) as typeof allComponents;
      const primaryComponent = nextComponents[0];

      setFormData((prev) => ({
        ...prev,
        symptom_type: primaryComponent?.symptomType ?? '',
        affected_activity: '',
        frequency: '',
        severity: '',
        warning_signs: '',
        recent_changes: '',
      }));

      return next;
    });
  };

  const selectAllComponents = () => {
    const allIds = allComponents.map((component) => component.id);
    setDiagnosisMode('component');
    setSelectedScenarioId('');
    setSelectedComponents(allIds);
    setFormData((prev) => ({
      ...prev,
      symptom_type: 'os performance issues',
      affected_activity: '',
      frequency: '',
      severity: '',
      warning_signs: '',
      recent_changes: '',
    }));
  };

  const handleCheckResolution = async () => {
  if (!report?.session_id) return;

  setCheckingResolution(true);

  try {
    const response = await apiPost<any>(`/api/diagnosis/${report.session_id}/check-resolution`);

    setReport((prev) =>
      prev
        ? {
            ...prev,
            resolution_status: response.data.resolution_status,
            resolution_checked_at: response.data.resolution_checked_at,
            resolution_summary: response.data.resolution_summary,
            resolution_proof: response.data.resolution_proof,
          }
        : prev
    );
  } catch {
    setError('RigMD could not check whether this issue is fixed yet.');
  } finally {
    setCheckingResolution(false);
  }
};

  const handleInputChange = (e: ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const nextStep = () => {
    if (!canContinue()) return;
    setStep((prev) => Math.min(prev + 1, 5));
  };

  const prevStep = () => setStep((prev) => Math.max(prev - 1, 1));

  const handleFormSubmit = async () => {
    setLoading(true);
    setError(null);
    setReport(null);
    setRemediationActions([]);
    setStep(5);

    try {
      const payload = {
        ...formData,
        system_state: formData.system_state.trim().slice(0, 500),
        system_state_signals: analyzeSystemStateNote(formData.system_state),
        affected_activity_label: selectedActivity || formData.affected_activity,
        warning_signs_label: selectedWarning || formData.warning_signs,
        recent_changes_label: selectedChange || formData.recent_changes,
      };

      const response = await apiPost<DiagnosticReport>(`/api/diagnosis/submit`, payload, {
        headers: { 'Content-Type': 'application/json' },
      });

      const diagnosis = response.data;
      setReport(diagnosis);

      if (diagnosis.diagnosed_category.toLowerCase() === 'no active issue detected') {
        setRemediationActions([]);
        return;
      }

      const actionsResponse = await apiGet<{ actions?: RemediationAction[] }>(`/api/remediation/actions`, {
        params: { category: diagnosis.diagnosed_category },
      });

      setRemediationActions(actionsResponse.data?.actions ?? []);
    } catch (err: any) {
      console.error('Diagnostic error:', err);
      setError(getApiErrorMessage(err, 'The engine encountered an error parsing the issue.'));
      setStep(4);
    } finally {
      setLoading(false);
    }
  };

  const getVerificationTarget = () => {
    if (report?.verification_target?.label) return report.verification_target.label;

    const category = report?.diagnosed_category.toLowerCase() ?? '';

    if (category.includes('no active issue')) return 'No verification target needed';
    if (category.includes('driver')) return 'Windows Device Manager';
    if (category.includes('display')) return 'Device Manager - Display adapters';
    if (category.includes('storage')) return 'Windows Storage settings';
    if (category.includes('boot') || category.includes('startup')) return 'Windows Startup Apps settings';
    if (category.includes('thermal')) return 'Task Manager - Performance tab';
    if (category.includes('os performance')) return 'Task Manager - Processes tab';

    return 'Windows Reliability Monitor';
  };

  const getVerificationDescription = () => {
    return report?.verification_target?.description || 'This opens the closest Windows tool related to the selected diagnosis.';
  };

  const handleVerifyLocation = async () => {
    if (!report || isNoActiveIssue(report)) return;

    setInspecting(true);

    try {
      const category = report.diagnosed_category.toLowerCase();

      const targetParam =
        report.verification_target?.target ||
        (category.includes('display') ? 'device_manager'
          : category.includes('driver') ? 'device_manager'
            : category.includes('boot') || category.includes('startup') ? 'startup_apps'
              : category.includes('storage') ? 'storage_settings'
                : category.includes('thermal') ? 'task_manager'
                  : category.includes('os performance') ? 'task_manager'
                    : 'reliability_monitor');

      await apiPost(`/api/remediation/open-target`, { target: targetParam });
    } catch (err) {
      console.error('Inspection error:', err);
      setError('RigMD could not open the Windows verification tool. You can still open it manually from Windows Search.');
    } finally {
      setInspecting(false);
    }
  };

  const handleAutonomyExecutionComplete = async (result: AutonomyResult) => {
    if (!result.execution?.success || !report?.session_id) return;

    try {
      const statusResponse = await apiPost<{
        resolution_status?: string;
        last_action_status?: string | null;
        last_action_summary?: string;
      }>(`/api/diagnosis/${report.session_id}/needs-recheck`);

      setReport((prev) =>
        prev
          ? {
              ...prev,
              resolution_status: statusResponse.data?.resolution_status,
              last_action_status: statusResponse.data?.last_action_status,
              last_action_summary: statusResponse.data?.last_action_summary,
            }
          : prev
      );
    } catch (err) {
      console.error('Resolution status update error:', err);
      setError('RigMD completed the autonomy action, but could not mark this session for follow-up verification.');
    }
  };

  const handlePostExecutionInspect = async (action: RemediationAction) => {
    const preview = getActionPreview(action, report);
    const target = preview.postTarget || report?.verification_target?.target;

    if (!target) return;

    setInspecting(true);

    try {
      await apiPost(`/api/remediation/open-target`, { target });
    } catch (err) {
      console.error('Post-execution inspection error:', err);
      setError('RigMD could not open the related Windows location. You can still open it manually from Windows Search.');
    } finally {
      setInspecting(false);
    }
  };

  const resetDiagnosis = () => {
    setStep(1);
    setDiagnosisMode('full');
    setSelectedComponents([]);
    setSelectedScenarioId('');
    setSymptomSearch('');
    setActiveSymptomFilter('All');
    setReport(null);
    setError(null);
    setRemediationActions([]);
    setFormData({
      symptom_type: DEFAULT_FULL_GUIDED_SYMPTOM,
      affected_activity: '',
      frequency: '',
      severity: '',
      warning_signs: '',
      recent_changes: '',
      system_state: '',
    });
  };

  const inputStyle = 'w-full mt-2 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-5 py-3.5 text-[15px] text-gray-100 outline-none focus:border-cyan-500/50 transition-all';
  const selectedCardStyle = 'border-cyan-300/55 bg-[#12343a] text-teal-50 shadow-[inset_0_1px_0_rgba(34,211,238,0.07)]';
  const unselectedCardStyle = 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)] text-slate-300 hover:border-[#2b5261] hover:bg-[var(--rigmd-card-hover)]';
  const softPanelStyle = 'border-[var(--rigmd-border)] bg-[#101821]';
  const softTileStyle = 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)]';
  const iconBoxStyle = (selected: boolean) =>
    selected
      ? 'border-cyan-300/35 bg-cyan-300/12 text-cyan-200'
      : 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] text-slate-400';
  const primaryButtonEnabledStyle = 'bg-[#2dd4bf] text-[#041014] hover:bg-[#5eead4]';
  const primaryButtonDisabledStyle = 'cursor-not-allowed bg-slate-700/35 text-slate-500 opacity-70';
  const primaryButtonStyle = (enabled: boolean) =>
    `flex items-center justify-center gap-2 rounded-lg px-5 py-3 text-sm font-bold transition-colors ${
      enabled ? primaryButtonEnabledStyle : primaryButtonDisabledStyle
    }`;

  const diagnosticSteps = [
    { label: 'Choose Diagnosis Path', detail: 'Mode and starting point', num: 1 },
    { label: 'Behavior Details', detail: 'How serious and when it happens', num: 2 },
    { label: 'Context Clues', detail: 'Warnings and recent changes', num: 3 },
    { label: 'Review Answers', detail: 'Confirm details', num: 4 },
    { label: 'Diagnosis Result', detail: 'Result and next steps', num: 5 },
  ];

  const progressPercent = Math.round((step / diagnosticSteps.length) * 100);

  return (
    <>
      <TopHeader title="New Diagnosis" subtitle="Choose how you want to run the diagnostic: guided, by component, or by scenario" />

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
            <section className={`overflow-hidden rounded-2xl border ${softPanelStyle}`}>
              <div className="flex flex-col gap-3 border-b border-[var(--rigmd-border)] px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <div className="flex items-center gap-2 text-[11px] font-bold uppercase tracking-[0.2em] text-slate-500">
                    <Stethoscope size={14} className="text-cyan-400" />
                    Guided Diagnostic
                  </div>
                  <h3 className="mt-1 text-xl font-bold text-white">{diagnosticSteps[step - 1]?.label}</h3>
                  <p className="mt-1 text-sm text-slate-500">{diagnosticSteps[step - 1]?.detail}</p>
                </div>

                <div className="text-left sm:text-right">
                  <p className="text-xs font-semibold text-slate-500">
                    Step {step} of {diagnosticSteps.length}
                    <span className="ml-2 text-cyan-400">Progress: {progressPercent}%</span>
                  </p>
                  <p className="mt-1 text-[11px] font-semibold text-slate-600">
                    Answers Completed: {completedAnswerCount}/{answerSummary.length}
                  </p>
                  <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full bg-[var(--rigmd-card-soft)] sm:w-44">
                    <motion.div
                      className="h-full rounded-full bg-cyan-400"
                      initial={{ width: 0 }}
                      animate={{ width: `${progressPercent}%` }}
                      transition={{ duration: 0.3, ease: 'easeOut' }}
                    />
                  </div>
                </div>
              </div>
            </section>

            <div className={`flex flex-col rounded-2xl border p-6 ${softPanelStyle}`}>
              <AnimatePresence mode="wait">
              {step === 1 && (
                <motion.div key="step-1" variants={cardFadeUp} initial="hidden" animate="visible" exit="exit" transition={cardTransition}>
                  <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
                    {DIAGNOSIS_MODES.map((mode) => {
                      const selected = diagnosisMode === mode.id;
                      const Icon = mode.icon;

                      return (
                        <motion.button
                          key={mode.id}
                          type="button"
                          onClick={() => selectDiagnosisMode(mode.id)}
                          whileHover={hoverLift}
                          whileTap={buttonTap}
                          className={`flex min-h-[188px] flex-col items-start justify-between rounded-xl border p-5 text-left transition-colors ${
                            selected
                              ? selectedCardStyle
                              : unselectedCardStyle
                          }`}
                        >
                          <span className="flex w-full items-center justify-between gap-3">
                            <span className={`flex h-11 w-11 items-center justify-center rounded-lg border ${iconBoxStyle(selected)}`}>
                              <Icon size={21} />
                            </span>
                            <span className={`rounded-full border px-3 py-1 text-[11px] font-semibold ${
                              selected ? 'border-cyan-300/35 bg-cyan-300/10 text-cyan-200' : 'border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] text-slate-500'
                            }`}>
                              {mode.action}
                            </span>
                          </span>
                          <span>
                            <span className="block text-base font-bold text-white">{mode.title}</span>
                            <span className="mt-2 block text-sm leading-relaxed text-slate-400">{mode.description}</span>
                          </span>
                          {selected && (
                            <span className="mt-4 inline-flex items-center gap-1.5 rounded-md border border-cyan-300/30 bg-cyan-300/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-cyan-200">
                              <Check size={12} /> Selected
                            </span>
                          )}
                        </motion.button>
                      );
                    })}
                  </div>

                  <section className={`mt-6 rounded-xl border p-5 ${softPanelStyle}`}>
                    {diagnosisMode === 'full' && (
                      <>
                        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                          <div>
                            <h3 className="font-bold text-white">Select a starting symptom</h3>
                            <p className="mt-1 text-sm text-slate-400">Pick the closest issue so RigMD can route the diagnostic questions correctly.</p>
                          </div>
                          <label className="relative block lg:w-[300px]">
                            <Search size={16} className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-slate-500" />
                            <input
                              value={symptomSearch}
                              onChange={(event) => setSymptomSearch(event.target.value)}
                              placeholder="Search symptoms..."
                              className="h-10 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] pl-11 pr-4 text-sm text-white outline-none placeholder:text-slate-600 focus:border-cyan-400/45"
                            />
                          </label>
                        </div>

                        <div className="mt-4 flex flex-wrap gap-2">
                          {SYMPTOM_FILTERS.map((filter) => {
                            const active = activeSymptomFilter === filter;

                            return (
                              <button
                                key={filter}
                                type="button"
                                onClick={() => setActiveSymptomFilter(filter)}
                                className={`rounded-lg border px-3 py-1.5 text-[11px] font-bold uppercase transition ${
                                  active
                                    ? 'border-cyan-400/45 bg-cyan-400/10 text-cyan-300'
                                    : 'border-[var(--rigmd-border)] bg-[var(--rigmd-card)] text-slate-500 hover:border-[#2b5261] hover:bg-[var(--rigmd-card-hover)] hover:text-cyan-300'
                                }`}
                              >
                                {filter}
                              </button>
                            );
                          })}
                        </div>

                        <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
                          {filteredSymptomOptions.map((option) => {
                            const selected = formData.symptom_type === option.value;

                            return (
                              <motion.button
                                key={option.value}
                                type="button"
                                onClick={() => selectSymptomType(option.value)}
                                whileTap={buttonTap}
                                className={`flex min-h-[96px] items-start gap-3 rounded-lg border p-4 text-left transition ${
                                  selected
                                    ? selectedCardStyle
                                    : unselectedCardStyle
                                }`}
                              >
                                <span
                                  className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border ${
                                    selected
                                      ? 'border-cyan-300 bg-cyan-300 text-[#041014]'
                                      : 'border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] text-transparent'
                                  }`}
                                >
                                  <CheckCircle2 size={13} />
                                </span>

                                <span className="min-w-0 flex-1">
                                  <span className="flex flex-wrap items-center gap-2">
                                    <span className="font-bold text-white">{option.label}</span>
                                    {option.common && (
                                      <span className="rounded-lg border border-teal-300/25 bg-teal-300/10 px-2 py-0.5 text-[10px] font-bold uppercase text-teal-200">
                                        Common
                                      </span>
                                    )}
                                  </span>
                                  <span className="mt-1 block text-sm leading-relaxed text-slate-500">{option.description}</span>
                                  <span className="mt-3 inline-flex rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] px-2.5 py-1 text-[10px] font-bold uppercase text-slate-400">
                                    {option.category}
                                  </span>
                                </span>
                              </motion.button>
                            );
                          })}
                        </div>
                      </>
                    )}

                    {diagnosisMode === 'component' && (
                      <>
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                          <div>
                            <h3 className="font-bold text-white">Select the subsystems you want to diagnose</h3>
                            <p className="mt-1 text-sm text-slate-400">Choose the parts of the PC that match the problem you are seeing.</p>
                          </div>
                          <button type="button" onClick={selectAllComponents} className="rounded-lg border border-teal-300/30 bg-teal-300/10 px-3 py-2 text-xs font-bold text-teal-200 hover:bg-teal-300/15">
                            Select all
                          </button>
                        </div>

                        <div className="mt-5 space-y-5">
                          {COMPONENT_GROUPS.map((group) => (
                            <div key={group.group}>
                              <p className="mb-3 text-[11px] font-bold uppercase tracking-[0.2em] text-slate-500">{group.group}</p>
                              <div className="grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-3">
                                {group.items.map((component) => {
                                  const selected = selectedComponents.includes(component.id);
                                  const Icon = component.icon;

                                  return (
                                    <motion.button
                                      key={component.id}
                                      type="button"
                                      onClick={() => toggleComponent(component.id)}
                                      whileTap={buttonTap}
                                      className={`flex min-h-[112px] items-start gap-3 rounded-lg border p-4 text-left transition-colors ${
                                        selected
                                          ? selectedCardStyle
                                          : unselectedCardStyle
                                      }`}
                                    >
                                      <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border ${iconBoxStyle(selected)}`}>
                                        <Icon size={18} />
                                      </span>
                                      <span className="min-w-0 flex-1">
                                        <span className="flex items-start justify-between gap-2">
                                          <span className="font-bold text-white">{component.title}</span>
                                          {selected && <Check size={15} className="shrink-0 text-teal-300" />}
                                        </span>
                                        <span className="mt-1 block text-xs leading-relaxed text-slate-500">{component.description}</span>
                                      </span>
                                    </motion.button>
                                  );
                                })}
                              </div>
                            </div>
                          ))}
                        </div>
                      </>
                    )}

                    {diagnosisMode === 'scenario' && (
                      <>
                        <div>
                          <h3 className="font-bold text-white">Pick a common problem</h3>
                          <p className="mt-1 text-sm text-slate-400">Choose the scenario that feels closest to what is happening.</p>
                        </div>

                        <div className="mt-5 grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-3">
                          {DIAGNOSIS_SCENARIOS.map((scenario) => {
                            const selected = selectedScenarioId === scenario.id;
                            const Icon = scenario.icon;

                            return (
                              <motion.button
                                key={scenario.id}
                                type="button"
                                onClick={() => chooseScenario(scenario.id)}
                                whileTap={buttonTap}
                                className={`group flex min-h-[112px] items-start gap-3 rounded-lg border p-4 text-left transition-colors ${
                                  selected
                                    ? selectedCardStyle
                                    : unselectedCardStyle
                                }`}
                              >
                                <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border ${iconBoxStyle(selected)}`}>
                                  <Icon size={18} />
                                </span>
                                <span className="min-w-0 flex-1">
                                  <span className="flex items-start justify-between gap-2">
                                    <span className="font-bold text-white">{scenario.title}</span>
                                    {selected ? <Check size={15} className="shrink-0 text-teal-300" /> : <ArrowRight size={15} className="shrink-0 text-slate-600 group-hover:text-cyan-300" />}
                                  </span>
                                  <span className="mt-1 block text-xs leading-relaxed text-slate-500">{scenario.description}</span>
                                </span>
                              </motion.button>
                            );
                          })}
                        </div>
                      </>
                    )}
                  </section>
                </motion.div>
              )}

              {step === 2 && (
                <motion.div key="step-2" variants={cardFadeUp} initial="hidden" animate="visible" exit="exit" transition={cardTransition} className="space-y-6">
                  <div className="flex items-start gap-3">
                    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-cyan-400/24 bg-cyan-400/10 text-cyan-300">
                      <Activity size={22} />
                    </div>
                    <div>
                      <h3 className="text-xl font-bold text-white">How does it behave?</h3>
                      <p className="mt-1 text-sm text-slate-500">
                        Tell RigMD how serious the issue is and when it happens.
                      </p>
                    </div>
                  </div>

                  <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
                    <div>
                      <p className="mb-3 text-xs font-bold uppercase tracking-wider text-gray-400">How disruptive is this issue? <span className="text-slate-600">Required</span></p>
                      <div className="grid grid-cols-1 gap-3">
                        {SEVERITY_OPTIONS.map((option) => {
                          const selected = formData.severity === option.value;

                          return (
                            <motion.button
                              key={option.value}
                              type="button"
                              onClick={() => setFormData((prev) => ({ ...prev, severity: option.value }))}
                              whileTap={buttonTap}
                              className={`flex min-h-[74px] items-center gap-3 rounded-lg border p-3 text-left transition ${
                                selected
                                  ? selectedCardStyle
                                  : unselectedCardStyle
                              }`}
                            >
                              <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border text-xs font-bold ${
                                selected ? 'border-cyan-300 bg-cyan-300 text-[#041014]' : 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] text-slate-500'
                              }`}>
                                {selected ? <CheckCircle2 size={15} /> : option.label.charAt(0)}
                              </span>
                              <span>
                                <span className="block text-sm font-bold text-white">{option.label}</span>
                                <span className="mt-0.5 block text-xs leading-relaxed text-slate-500">{option.description}</span>
                              </span>
                            </motion.button>
                          );
                        })}
                      </div>
                    </div>

                    <div>
                      <p className="mb-3 text-xs font-bold uppercase tracking-wider text-gray-400">How often does it happen? <span className="text-slate-600">Required</span></p>
                      <div className="grid grid-cols-1 gap-3">
                        {FREQUENCY_OPTIONS.map((option) => {
                          const selected = formData.frequency === option.value;

                          return (
                            <motion.button
                              key={option.value}
                              type="button"
                              onClick={() => setFormData((prev) => ({ ...prev, frequency: option.value }))}
                              whileTap={buttonTap}
                              className={`flex min-h-[74px] items-center gap-3 rounded-lg border p-3 text-left transition ${
                                selected
                                  ? selectedCardStyle
                                  : unselectedCardStyle
                              }`}
                            >
                              <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border ${
                                selected ? 'border-cyan-300 bg-cyan-300 text-[#041014]' : 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] text-slate-500'
                              }`}>
                                {selected ? <CheckCircle2 size={15} /> : <Activity size={14} />}
                              </span>
                              <span>
                                <span className="block text-sm font-bold text-white">{option.label}</span>
                                <span className="mt-0.5 block text-xs leading-relaxed text-slate-500">{option.description}</span>
                              </span>
                            </motion.button>
                          );
                        })}
                      </div>
                    </div>
                  </div>

                  <div>
                    <p className="mb-3 text-xs font-bold uppercase tracking-wider text-gray-400">{currentBranch.step2Label} <span className="text-slate-600">Required</span></p>
                    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                      {currentBranch.activities.map((act) => {
                        const selected = formData.affected_activity === act.value;

                        return (
                          <motion.button
                            key={act.value}
                            type="button"
                            onClick={() => setFormData((prev) => ({ ...prev, affected_activity: act.value }))}
                              whileTap={buttonTap}
                              className={`flex min-h-[64px] items-center justify-between gap-3 rounded-lg border px-4 py-3 text-left transition ${
                                selected
                                ? selectedCardStyle
                                : unselectedCardStyle
                            }`}
                          >
                            <span className="text-sm font-semibold leading-relaxed">{act.label}</span>
                            {selected && <CheckCircle2 size={16} className="shrink-0 text-cyan-300" />}
                          </motion.button>
                        );
                      })}
                    </div>
                  </div>
                </motion.div>
              )}

              {step === 3 && (
                <motion.div key="step-3" variants={cardFadeUp} initial="hidden" animate="visible" exit="exit" transition={cardTransition} className="space-y-6">
                  <div className="flex items-start gap-3">
                    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-amber-400/24 bg-amber-400/10 text-amber-300">
                      <AlertTriangle size={22} />
                    </div>
                    <div>
                      <h3 className="text-xl font-bold text-white">Any other clues?</h3>
                      <p className="mt-1 text-sm text-slate-500">
                        These details help RigMD understand what may have changed.
                      </p>
                    </div>
                  </div>

                  <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
                    <div>
                      <p className="mb-3 text-xs font-bold uppercase tracking-wider text-gray-400">Warning signs observed</p>
                      <p className="-mt-2 mb-3 text-xs text-slate-500">{currentBranch.warningSignsLabel}</p>
                      <div className="space-y-3">
                        {currentBranch.warningSigns.map((ws) => {
                          const selected = formData.warning_signs === ws.value;

                          return (
                            <motion.button
                              key={ws.value}
                              type="button"
                              onClick={() => setFormData((prev) => ({ ...prev, warning_signs: ws.value }))}
                              whileTap={buttonTap}
                              className={`flex min-h-[68px] w-full items-center justify-between gap-3 rounded-lg border px-4 py-3 text-left transition ${
                                selected
                                  ? selectedCardStyle
                                  : unselectedCardStyle
                              }`}
                            >
                              <span className="text-sm font-semibold leading-relaxed">{ws.label}</span>
                              {selected && <CheckCircle2 size={16} className="shrink-0 text-cyan-300" />}
                            </motion.button>
                          );
                        })}
                      </div>
                    </div>

                    <div>
                      <p className="mb-3 text-xs font-bold uppercase tracking-wider text-gray-400">Recent changes</p>
                      <p className="-mt-2 mb-3 text-xs text-slate-500">{currentBranch.recentChangesLabel}</p>
                      <div className="space-y-3">
                        {currentBranch.recentChanges.map((rc) => {
                          const selected = formData.recent_changes === rc.value;

                          return (
                            <motion.button
                              key={rc.value}
                              type="button"
                              onClick={() => setFormData((prev) => ({ ...prev, recent_changes: rc.value }))}
                              whileTap={buttonTap}
                              className={`flex min-h-[68px] w-full items-center justify-between gap-3 rounded-lg border px-4 py-3 text-left transition ${
                                selected
                                  ? selectedCardStyle
                                  : unselectedCardStyle
                              }`}
                            >
                              <span className="text-sm font-semibold leading-relaxed">{rc.label}</span>
                              {selected && <CheckCircle2 size={16} className="shrink-0 text-cyan-300" />}
                            </motion.button>
                          );
                        })}
                      </div>
                    </div>
                  </div>

                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">Add extra details if needed</label>
                    <textarea
                      name="system_state"
                      value={formData.system_state}
                      onChange={handleInputChange}
                      placeholder="e.g., My desktop gets hot near the rear vents and gameplay starts lagging."
                      className={`${inputStyle} min-h-[96px] resize-none`}
                    />
                  </div>
                </motion.div>
              )}

              {step === 4 && (
                <motion.div key="step-4" variants={cardFadeUp} initial="hidden" animate="visible" exit="exit" transition={cardTransition} className="space-y-4">
                  <div className="flex items-start gap-3">
                    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-emerald-400/24 bg-emerald-400/10 text-emerald-300">
                      <CheckCircle2 size={22} />
                    </div>
                    <div>
                      <h3 className="text-xl font-bold text-white">Review your checkup</h3>
                      <p className="mt-1 text-sm text-slate-500">
                        Confirm the intake before RigMD runs the diagnostic engine.
                      </p>
                    </div>
                  </div>

                  <div className={`space-y-4 rounded-lg border p-4 ${softPanelStyle}`}>
                    {reviewSections.map((section) => (
                      <div key={section.title}>
                        <p className="mb-2 text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">{section.title}</p>
                        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                          {section.items.map((item) => {
                            const hasValue = Boolean(item.value);

                            return (
                              <div key={`${section.title}-${item.label}`} className="rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-3">
                                <span className="block text-[10px] font-bold uppercase tracking-wider text-gray-500">{item.label}</span>
                                <span className={`mt-1 block text-sm font-semibold leading-relaxed ${hasValue ? 'text-slate-100' : 'text-slate-600'}`}>
                                  {hasValue ? item.value : 'Not provided'}
                                </span>
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    ))}

                    <div className="rounded-lg border border-emerald-400/18 bg-emerald-400/[0.045] p-4">
                      <div className="mb-1 flex items-center gap-2">
                        <ShieldCheck size={15} className="text-emerald-300" />
                        <p className="text-xs font-bold uppercase tracking-wider text-emerald-300">Ready to Run</p>
                      </div>
                      <p className="text-sm leading-relaxed text-slate-400">
                        The next step submits this intake to RigMD and returns a probable category, confidence, proof, and safe next actions.
                      </p>
                    </div>
                  </div>
                </motion.div>
              )}

              {step === 5 && (
                <motion.div key="step-5" variants={cardFadeUp} initial="hidden" animate="visible" exit="exit" transition={cardTransition} className="flex flex-1 flex-col justify-center py-4">
                  {loading && (
                    <div className="space-y-4 py-12 text-center">
                      <div className="mx-auto h-10 w-10 animate-spin rounded-full border-4 border-cyan-500 border-t-transparent" />
                      <p className="text-sm font-semibold uppercase tracking-widest text-cyan-400">Running Diagnostic Check...</p>
                    </div>
                  )}

                  {error && (
                    <div className="mb-4 rounded-xl border border-red-500/20 bg-red-500/10 p-4 text-sm text-red-400">
                      {error}
                    </div>
                  )}

                  {report && (
                    <div className="space-y-6">
                      <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={cardTransition} className={`rounded-xl border p-5 ${getActionTone(report.action_category).border} ${getActionTone(report.action_category).soft}`}>
                        <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                          <div className="min-w-0">
                            <div className="mb-3 flex flex-wrap items-center gap-2">
                              <span className="rounded border border-cyan-400/25 bg-cyan-400/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-cyan-300">
                                Result Ready
                              </span>
                              <span className={`rounded border px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider ${getActionTone(report.action_category).badge}`}>
                                {normalizeActionCategory(report.action_category)}
                              </span>
                            </div>

                            <p className="text-[10px] font-bold uppercase tracking-[0.18em] text-slate-500">Probable Cause</p>
                            <h4 className="mt-1 text-2xl font-bold leading-tight text-white">{report.diagnosed_category}</h4>
                            {report.confidence_label.toLowerCase().includes('low') && (
                              <p className="mt-2 text-sm text-slate-400">Treat this as a starting clue, not a final diagnosis.</p>
                            )}
                          </div>

                          <div className="grid min-w-[240px] grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-1">
                            <div className={`rounded-lg border p-3 ${softTileStyle}`}>
                              <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Recommended Action</p>
                              <p className={`mt-1 text-sm font-bold ${getActionTone(report.action_category).text}`}>
                                {normalizeActionCategory(report.action_category)}
                              </p>
                            </div>
                            <div className={`rounded-lg border p-3 ${softTileStyle}`}>
                              <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Confidence</p>
                              <p className="mt-1 text-sm font-bold text-white">{report.confidence_label || 'Not provided'}</p>
                            </div>
                          </div>
                        </div>
                      </motion.section>

                      <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={{ ...cardTransition, delay: 0.03 }}>
                        <h5 className="mb-2 text-xs font-bold uppercase tracking-wider text-gray-500">Why RigMD Suggested This</h5>
                        <p className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4 text-sm leading-relaxed text-gray-300">
                          {report.ai_explanation || 'RigMD did not return a diagnostic explanation for this result.'}
                        </p>
                      </motion.section>

                      {report.proof && report.proof.length > 0 && (
                        <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={{ ...cardTransition, delay: 0.06 }}>
                          <h5 className="mb-2 text-xs font-bold uppercase tracking-wider text-gray-500">Evidence / Live Scan Proof</h5>
                          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                            {report.proof.map((item) => (
                              <motion.div key={item.label} variants={cardFadeUp} whileHover={hoverLift} transition={cardTransition} className={`rounded-xl border p-4 ${getProofCardStyle(report.action_category, item.status)}`}>
                                <p className="text-[10px] font-bold uppercase tracking-wider text-gray-500">{item.label}</p>
                                <p className="mt-1 text-sm font-bold text-white">{item.value}</p>
                                <p className="mt-2 text-xs leading-relaxed text-gray-400">{item.meaning}</p>
                                {item.status && <p className="mt-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Source: Live scan</p>}
                              </motion.div>
                            ))}
                          </div>
                        </motion.section>
                      )}

                      {!isNoActiveIssue(report) && (
                        <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={{ ...cardTransition, delay: 0.09 }} className="rounded-xl border border-cyan-500/20 bg-cyan-500/[0.045] p-4">
                          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                            <div className="min-w-0">
                              <h5 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-white">
                                <FileText size={14} className="text-cyan-400" /> Targeted Verification
                              </h5>
                              <p className="mt-3 text-[10px] font-bold uppercase tracking-wider text-slate-500">Verification Target</p>
                              <p className="mt-1 text-sm font-bold text-cyan-200">{getVerificationTarget()}</p>
                              <p className="mt-2 text-xs leading-relaxed text-slate-400">{getVerificationDescription()}</p>
                            </div>
                            <motion.button type="button" disabled={inspecting} onClick={handleVerifyLocation} whileTap={buttonTap} className="w-full shrink-0 rounded-lg border border-cyan-500/40 bg-cyan-500/10 px-4 py-2.5 text-center text-xs font-bold text-cyan-200 transition-all hover:bg-cyan-500/15 disabled:cursor-not-allowed disabled:opacity-50 md:w-auto">
                              {inspecting ? 'Opening...' : 'Verify & Inspect'}
                            </motion.button>
                          </div>
                        </motion.section>
                      )}

                      <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={{ ...cardTransition, delay: 0.12 }} className={`rounded-xl border border-l-4 p-4 ${getActionTone(report.action_category).border} ${getActionTone(report.action_category).soft} ${getActionTone(report.action_category).accent}`}>
                        <h5 className="mb-1 text-xs font-bold uppercase tracking-wider text-white">Recommended Next Step</h5>
                        <p className="text-sm leading-relaxed text-gray-300">
                          {report.recommended_next_step || 'RigMD did not return a recommended next step for this result.'}
                        </p>
                      </motion.section>

                      {isNoActiveIssue(report) && (
                        <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-4 text-xs text-emerald-300">
                          No safe fix is needed right now because the live scan did not detect active system pressure.
                        </div>
                      )}

                      {!isNoActiveIssue(report) && remediationActions.length > 0 && (
                        <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={{ ...cardTransition, delay: 0.15 }} className="space-y-4">
                          <div>
                            <h5 className="text-xs font-bold uppercase tracking-wider text-white">Safe Actions Available</h5>
                            <p className="mt-1 text-xs text-slate-500">RigMD separates assisted tools from automated cleanup so the risk level is clear before you run anything.</p>
                          </div>

                          {(['assisted', 'automated', 'read-only'] as const).map((group) => {
                            const groupActions = remediationActions.filter((action) => getActionPreview(action, report).mode === group);
                            if (groupActions.length === 0) return null;

                            const title = group === 'automated' ? 'Automated Cleanup' : group === 'read-only' ? 'Read-Only Checks' : 'Assisted Tools';
                            const note = group === 'automated'
                              ? 'RigMD may make the specific backend-approved cleanup change after confirmation.'
                              : group === 'read-only'
                                ? 'RigMD checks and reports without repair flags or file changes.'
                                : 'RigMD opens Windows tools. You stay in control of what changes, if anything.';

                            return (
                              <div key={group} className="space-y-2">
                                <div className="flex items-center justify-between gap-3">
                                  <p className="text-[10px] font-bold uppercase tracking-[0.18em] text-slate-500">{title}</p>
                                  <p className="text-[11px] text-slate-600">{note}</p>
                                </div>

                                <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
                                  {groupActions.map((action) => {
                                    const preview = getActionPreview(action, report);

                                    return (
                                      <motion.div key={action.id} variants={cardFadeUp} whileHover={hoverLift} transition={cardTransition} className="rounded-xl border border-cyan-500/18 bg-[var(--rigmd-card)] p-4">
                                        <div className="flex min-h-full flex-col">
                                          <div className="mb-3 flex items-start gap-2 text-sm font-bold text-cyan-300">
                                            <ShieldCheck size={16} className="mt-0.5 shrink-0" />
                                            <h5>{action.label}</h5>
                                          </div>
                                          <p className="text-xs leading-relaxed text-gray-400">{action.description}</p>
                                          <p className="mt-2 text-[11px] leading-relaxed text-slate-500">
                                            {preview.mode === 'automated'
                                              ? 'Automated cleanup. Personal files and system files are not included.'
                                              : preview.mode === 'read-only'
                                                ? 'Read-only action. RigMD reports what Windows returns.'
                                                : 'Assisted action. RigMD only opens the Windows tool.'}
                                          </p>
                                          <p className="mt-1 text-[11px] leading-relaxed text-slate-600">{action.risk}</p>

                                          <div className="mt-4 flex flex-wrap gap-2">
                                            <span className={`rounded border px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider ${getActionTone(report.action_category).badge}`}>
                                              {preview.mode === 'automated' ? 'Automation candidate' : preview.mode === 'read-only' ? 'Read-only candidate' : 'Assisted candidate'}
                                            </span>

                                            {preview.postTarget && (
                                              <motion.button type="button" disabled={inspecting} onClick={() => handlePostExecutionInspect(action)} whileTap={buttonTap} className="rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-3 py-2 text-[11px] font-bold text-cyan-400 transition-colors hover:bg-cyan-500/10 disabled:opacity-50">
                                                {inspecting ? 'Opening...' : 'Open Related Tool'}
                                              </motion.button>
                                            )}
                                          </div>
                                        </div>
                                      </motion.div>
                                    );
                                  })}
                                </div>
                              </div>
                            );
                          })}
                        </motion.section>
                      )}
                      {!isNoActiveIssue(report) &&
                        report.session_id && (
                          <AutonomyRemediationPanel
                            sessionId={report.session_id}
                            diagnosedCategory={report.diagnosed_category}
                            onExecutionComplete={handleAutonomyExecutionComplete}
                          />
                        )}

                      {!isNoActiveIssue(report) && remediationActions.length === 0 && (
                        <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4 text-xs text-gray-500">
                          No automated fix is available for this diagnosis. RigMD will keep this case in guided mode to avoid unsafe changes.
                        </div>
                      )}

                      {!isNoActiveIssue(report) && report.session_id && (
                        <motion.section variants={cardFadeUp} initial="hidden" animate="visible" transition={{ ...cardTransition, delay: 0.18 }} className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4">
                          {(() => {
                            const resolutionView = getResolutionView(report.resolution_status);

                            return (
                              <>
                                <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                                  <div>
                                    <div className="mb-2 flex flex-wrap items-center gap-2">
                                      <h5 className="text-xs font-bold uppercase tracking-wider text-white">Resolution Status</h5>
                                      <span className={`rounded border px-2 py-1 text-[10px] font-bold uppercase tracking-wider ${resolutionView.className}`}>
                                        {resolutionView.label}
                                      </span>
                                    </div>
                                    <p className="text-xs leading-relaxed text-gray-400">
                                      {report.resolution_summary || report.last_action_summary || resolutionView.fallback}
                                    </p>
                                  </div>

                                  <motion.button
                                    type="button"
                                    disabled={checkingResolution}
                                    onClick={handleCheckResolution}
                                    whileTap={buttonTap}
                                    className="rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-4 py-2 text-xs font-bold text-cyan-400 hover:bg-cyan-500/10 disabled:cursor-not-allowed disabled:opacity-50"
                                  >
                                    {checkingResolution ? 'Checking if fixed...' : 'Check if Fixed'}
                                  </motion.button>
                                </div>

                                {report.resolution_proof && report.resolution_proof.length > 0 && (
                                  <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-2">
                                    {report.resolution_proof.map((item) => (
                                      <div key={item.label} className={`rounded-lg border p-3 ${getProofCardStyle(report.action_category, item.status)}`}>
                                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{item.label}</p>
                                        <p className="mt-1 text-xs font-semibold text-white">{item.value}</p>
                                        <p className="mt-1 text-[11px] text-slate-500">{item.meaning}</p>
                                      </div>
                                    ))}
                                  </div>
                                )}
                              </>
                            );
                          })()}
                        </motion.section>
                      )}
                    </div>
                  )}
                </motion.div>
              )}
              </AnimatePresence>

              {error && step !== 5 && (
                <motion.div
                  variants={cardFadeUp}
                  initial="hidden"
                  animate="visible"
                  transition={cardTransition}
                  className="mt-5 rounded-xl border border-red-400/25 bg-red-400/10 p-4 text-sm text-red-200"
                >
                  <p className="font-bold text-red-100">Diagnosis could not be completed.</p>
                  <p className="mt-1 leading-relaxed text-red-200/80">{error}</p>
                </motion.div>
              )}

              {step > 1 && (
              <div className="mt-6 flex items-center justify-between border-t border-[var(--rigmd-border)] pt-4">
                {step < 5 ? (
                  <motion.button type="button" onClick={prevStep} whileTap={buttonTap} className="flex items-center gap-2 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-5 py-3 text-sm font-semibold text-gray-300 transition-colors hover:bg-[var(--rigmd-card-hover)]">
                    <ArrowLeft size={16} /> Back
                  </motion.button>
                ) : <div />}

                {step > 1 && step < 4 ? (
                  <div className="ml-auto flex flex-col items-end gap-2">
                    <motion.button type="button" disabled={!canContinue()} onClick={nextStep} whileTap={canContinue() ? buttonTap : undefined} className={primaryButtonStyle(canContinue())}>
                      Next <ArrowRight size={16} />
                    </motion.button>
                    {!canContinue() && <p className="max-w-xs text-right text-xs text-slate-500">{getDisabledReason()}</p>}
                  </div>
                ) : step === 4 ? (
                  <div className="ml-auto flex flex-col items-end gap-2">
                    <motion.button type="button" disabled={!canContinue() || loading} onClick={handleFormSubmit} whileHover={canContinue() ? hoverLift : undefined} whileTap={canContinue() ? buttonTap : undefined} className={primaryButtonStyle(canContinue() && !loading)}>
                      <Zap size={16} /> Run Diagnosis
                    </motion.button>
                    {!canContinue() && <p className="max-w-xs text-right text-xs text-slate-500">{getDisabledReason()}</p>}
                  </div>
                ) : step === 5 && !loading ? (
                  <motion.button type="button" onClick={resetDiagnosis} whileTap={buttonTap} className="ml-auto flex items-center gap-2 rounded-xl border border-cyan-500/30 bg-cyan-500/5 px-6 py-3 text-sm font-bold text-cyan-400 transition-colors hover:bg-cyan-500/10">
                    <Stethoscope size={16} /> Check Another Issue
                  </motion.button>
                ) : <div />}
              </div>
              )}
            </div>
          </div>

          <aside className="self-start overflow-hidden rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] xl:block">
            <div className="border-b border-[var(--rigmd-border-soft)] p-4">
              <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                {report ? 'Diagnosis Summary' : 'Selected Diagnosis'}
              </p>
              <div className="mt-4 flex items-center gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-cyan-400/22 bg-cyan-400/10 text-cyan-300">
                  <ActiveModeIcon size={20} />
                </div>
                <div className="min-w-0">
                  <h3 className="truncate text-sm font-bold text-white">{activeMode.title}</h3>
                  <p className="text-xs text-slate-500">Mode</p>
                </div>
              </div>
            </div>

            <div className="space-y-3 p-4">
              <div>
                <p className="mb-2 text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">
                  {diagnosisMode === 'component' ? `Components (${selectedComponents.length})` : diagnosisMode === 'scenario' ? 'Scenario' : 'Scope'}
                </p>
                {diagnosisMode === 'component' ? (
                  selectedComponentDetails.length > 0 ? (
                    <div className="space-y-2">
                      {selectedComponentDetails.slice(0, 4).map((component) => (
                        <div key={component.id} className="flex items-center gap-2 rounded-lg border border-teal-300/18 bg-teal-300/[0.045] px-3 py-2 text-xs font-semibold text-slate-200">
                          <Check size={13} className="text-teal-300" />
                          {component.title}
                        </div>
                      ))}
                      {selectedComponentDetails.length > 4 && (
                        <p className="text-xs text-slate-500">+{selectedComponentDetails.length - 4} more selected</p>
                      )}
                    </div>
                  ) : (
                    <p className="text-sm text-slate-500">None selected yet.</p>
                  )
                ) : diagnosisMode === 'full' ? (
                  <div className="space-y-2">
                    <div className="flex items-center gap-2 rounded-lg border border-cyan-300/18 bg-cyan-300/[0.055] px-3 py-3 text-sm font-semibold text-slate-100">
                      <Check size={15} className="text-cyan-300" />
                      All major subsystems
                    </div>
                    {selectedSymptom && (
                      <div className="rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] px-3 py-3">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Starting symptom</p>
                        <p className="mt-1 text-sm font-semibold text-slate-100">{selectedSymptom}</p>
                      </div>
                    )}
                  </div>
                ) : (
                  <div className={`flex items-center gap-2 rounded-lg border px-3 py-3 text-sm font-semibold ${
                    selectedDiagnosisLabel
                      ? 'border-cyan-300/18 bg-cyan-300/[0.055] text-slate-100'
                      : 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] text-slate-500'
                  }`}>
                    {selectedDiagnosisLabel ? <Check size={15} className="text-cyan-300" /> : <Info size={15} className="text-slate-500" />}
                    {selectedDiagnosisLabel || 'No scenario selected yet.'}
                  </div>
                )}
              </div>

              <div className={`rounded-lg border p-3.5 ${softTileStyle}`}>
                {report ? (
                  <div className="space-y-3">
                    <div>
                      <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">Probable Cause</p>
                      <p className="mt-1 text-sm font-semibold leading-relaxed text-white">{report.diagnosed_category}</p>
                    </div>
                    <div className="grid grid-cols-2 gap-2">
                      <div className="rounded-md border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-2">
                        <p className="text-[9px] font-bold uppercase tracking-wider text-slate-500">Action</p>
                        <p className={`mt-1 text-xs font-bold ${getActionTone(report.action_category).text}`}>
                          {normalizeActionCategory(report.action_category)}
                        </p>
                      </div>
                      <div className="rounded-md border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)] p-2">
                        <p className="text-[9px] font-bold uppercase tracking-wider text-slate-500">Confidence</p>
                        <p className="mt-1 text-xs font-bold text-white">{report.confidence_label}</p>
                      </div>
                    </div>
                    <span className={`inline-flex rounded border px-2 py-1 text-[10px] font-bold uppercase tracking-wider ${getResolutionView(report.resolution_status).className}`}>
                      {getResolutionView(report.resolution_status).label}
                    </span>
                  </div>
                ) : (
                  <>
                    <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">What Happens Next</p>
                    <p className="mt-2 text-xs leading-relaxed text-slate-400">
                      {step === 1
                        ? canContinue()
                          ? 'Continue to describe how the issue behaves.'
                          : diagnosisMode === 'component'
                            ? 'Select at least one subsystem to diagnose.'
                            : diagnosisMode === 'scenario'
                              ? 'Pick a scenario card to start.'
                              : 'Select the closest symptom to begin.'
                        : step < 4
                          ? 'Complete the remaining diagnostic details.'
                          : step === 4
                            ? 'Run diagnostics to submit this intake and receive probable causes and safe next actions.'
                            : 'Review results, proof, verification targets, and safe actions from the backend.'}
                    </p>
                  </>
                )}
              </div>

              {!report && (
                <div className={`rounded-lg border p-3.5 ${softTileStyle}`}>
                <div className="mb-2 flex items-center justify-between">
                  <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">Progress</p>
                  <span className="text-xs font-semibold text-cyan-300">{completedAnswerCount}/{answerSummary.length}</span>
                </div>
                <div className="h-1.5 overflow-hidden rounded-full bg-[var(--rigmd-card-soft)]">
                  <div className="h-full rounded-full bg-cyan-400" style={{ width: `${Math.round((completedAnswerCount / answerSummary.length) * 100)}%` }} />
                </div>
              </div>
              )}

              {report ? (
                <motion.button
                  type="button"
                  onClick={resetDiagnosis}
                  whileTap={buttonTap}
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-4 py-3 text-sm font-bold text-cyan-300 transition hover:bg-cyan-500/10"
                >
                  <Stethoscope size={16} />
                  Run Another Diagnosis
                </motion.button>
              ) : step === 1 ? (
                <motion.button
                  type="button"
                  disabled={!canContinue()}
                  onClick={nextStep}
                  whileTap={canContinue() ? buttonTap : undefined}
                  className={`w-full ${primaryButtonStyle(canContinue())}`}
                >
                  Continue
                  <ArrowRight size={16} />
                </motion.button>
              ) : (
                <div className={`rounded-lg border p-3 text-xs leading-relaxed text-slate-500 ${softTileStyle}`}>
                  Use the action buttons below the intake screen to move through this step.
                </div>
              )}

              {!report && !canContinue() && (
                <p className="text-center text-xs text-slate-500">{getDisabledReason()}</p>
              )}
            </div>
          </aside>
        </div>
      </motion.div>
    </>
  );
}
