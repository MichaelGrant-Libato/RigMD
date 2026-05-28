import { useState, ChangeEvent } from 'react';
import axios from 'axios';
import {
  Stethoscope,
  ArrowRight,
  ArrowLeft,
  HelpCircle,
  CheckCircle2,
  AlertTriangle,
  Activity,
  Zap,
  ShieldCheck,
  X,
  FileText,
  Info,
} from 'lucide-react';
import TopHeader from '../components/TopHeader';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8000';

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
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;
}

interface ActionImpactProof {
  label: string;
  before?: string | number;
  after?: string | number;
  change?: string;
  status?: string;
  meaning?: string;
}

interface ActionResult {
  success: boolean;
  summary: string;
  action?: string;
  cleared?: string;
  cleared_bytes?: number;
  deleted_items?: number;
  skipped_errors?: number;
  needs_admin?: boolean;
  output?: string;
  proof?: ActionImpactProof[];
}

const CATEGORY_OPTIONS = [
  { value: 'thermal condition', label: 'Computer gets very hot / Loud fan noises' },
  { value: 'os performance issues', label: 'System runs slowly / Freezes / Stutters' },
  { value: 'driver-related issues', label: 'Blue screens / Random restarts / Glitches' },
  { value: 'storage os-level issues', label: 'Files loading slowly / Errors saving files' },
  { value: 'boot and startup issues', label: "Takes forever to turn on / Won't start up" },
  { value: 'display and rendering issues', label: 'Screen flickering / Strange visual lines' },
];

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
    step2Label: 'When do you experience the system freezes or stuttering bottlenecks?',
    activities: [
      { value: 'working', label: 'Opening multiple browser tabs or document apps' },
      { value: 'gaming', label: 'Running intensive background application loads' },
      { value: 'startup', label: 'Right after turning on the desktop layout' },
      { value: 'browsing', label: 'Watching high-resolution video content streams' },
    ],
    warningSignsLabel: 'What happens immediately before a performance lockup?',
    warningSigns: [
      { value: 'stuttering', label: 'The mouse cursor hitches or freezes for a second' },
      { value: 'error message', label: 'An application alert popped up on screen' },
      { value: 'none', label: 'No warning sign, everything just drops speed' },
    ],
    recentChangesLabel: 'Did you download or apply anything right before the slowdowns?',
    recentChanges: [
      { value: 'new software installed', label: 'Installed a new heavy background application' },
      { value: 'windows update', label: 'Windows installed automatic updates' },
      { value: 'none', label: 'No alterations were completed' },
    ],
  },
  'driver-related issues': {
    step2Label: 'What triggers the blue screens, random restarts, or component glitches?',
    activities: [
      { value: 'gaming', label: 'Launching games or graphical elements' },
      { value: 'idle', label: 'Randomly while sitting completely idle' },
      { value: 'startup', label: 'During the boot process loop' },
      { value: 'working', label: 'Connecting external hardware components' },
    ],
    warningSignsLabel: 'What visual anomaly appears before a system crash?',
    warningSigns: [
      { value: 'error message', label: 'A blue screen with an error code layout' },
      { value: 'stuttering', label: 'The audio loops or stutters aggressively' },
      { value: 'black screen', label: 'The screen goes black without any sign' },
    ],
    recentChangesLabel: 'Have you updated any system parameters lately?',
    recentChanges: [
      { value: 'driver update', label: 'Updated the graphics card or driver assets' },
      { value: 'windows update', label: 'A recent Windows package deployment update' },
      { value: 'none', label: 'Nothing was changed' },
    ],
  },
  'storage os-level issues': {
    step2Label: 'When do you notice file access slowdowns or saving blockades?',
    activities: [
      { value: 'working', label: 'Moving files, opening folders, or saving work' },
      { value: 'startup', label: 'Booting up files during workspace initializations' },
      { value: 'gaming', label: 'Loading maps or textures inside game assets' },
    ],
    warningSignsLabel: 'What indicates a storage access failure?',
    warningSigns: [
      { value: 'error message', label: 'An "Access Denied" or "File Not Found" warning' },
      { value: 'stuttering', label: 'The machine freezes while searching for elements' },
      { value: 'none', label: 'No warning, saving just takes ages' },
    ],
    recentChangesLabel: 'Any disk or structural parameters altered?',
    recentChanges: [
      { value: 'hardware upgrade', label: 'Installed or configured an internal drive card' },
      { value: 'new software installed', label: 'Downloaded huge program configuration archives' },
      { value: 'none', label: 'No changes performed' },
    ],
  },
  'boot and startup issues': {
    step2Label: 'At what stage does the boot delay freeze or loop up?',
    activities: [
      { value: 'startup', label: 'Right at the loading logo animation framework' },
      { value: 'idle', label: 'Loading desktop app taskbar items' },
      { value: 'resume', label: 'When returning from sleep or hibernation states' },
    ],
    warningSignsLabel: 'What warning is visible on startup blocks?',
    warningSigns: [
      { value: 'error message', label: 'An alert indicating no boot device found' },
      { value: 'loud fan noise', label: 'Fans spinning at full throttle immediately' },
      { value: 'black screen', label: 'The desktop layout just remains black' },
    ],
    recentChangesLabel: 'Any global environmental changes before the boot crash?',
    recentChanges: [
      { value: 'windows update', label: 'A core Windows update layout was processed' },
      { value: 'hardware upgrade', label: 'A modular hardware upgrade card was inserted' },
      { value: 'none', label: 'No changes performed' },
    ],
  },
  'display and rendering issues': {
    step2Label: 'When do screen lines, flickers, or glitches show up?',
    activities: [
      { value: 'gaming', label: 'While rendering 3D textures or games' },
      { value: 'browsing', label: 'Watching streaming media inside browser blocks' },
      { value: 'idle', label: 'Constantly while viewing any blank space background' },
    ],
    warningSignsLabel: 'What specific visual distortion pops up?',
    warningSigns: [
      { value: 'stuttering', label: 'Screen tearing or visual blocks glitching out' },
      { value: 'error message', label: 'A notification saying "Display driver stopped responding"' },
      { value: 'screen flicker', label: 'The screen flickers off and back on randomly' },
    ],
    recentChangesLabel: 'Have you adjusted graphics infrastructure?',
    recentChanges: [
      { value: 'driver update', label: 'Performed a GPU display driver software refresh' },
      { value: 'hardware upgrade', label: 'Disconnected or adjusted monitor cords' },
      { value: 'none', label: 'No updates completed' },
    ],
  },
};

function isNoActiveIssue(report: DiagnosticReport | null) {
  return report?.diagnosed_category.toLowerCase() === 'no active issue detected';
}

function getProofCardStyle(status: string) {
  const value = status.toLowerCase();

  if (value === 'high') return 'border-red-500/30 bg-red-500/5';
  if (value === 'elevated' || value === 'detected') return 'border-orange-500/30 bg-orange-500/5';
  return 'border-emerald-500/20 bg-emerald-500/5';
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

function getActionPreview(action: RemediationAction, report: DiagnosticReport | null) {
  const category = report?.diagnosed_category ?? 'this diagnosis';

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

function buildActionProof(result: ActionResult) {
  if (result.proof && result.proof.length > 0) {
    return result.proof;
  }

  const proof: ActionImpactProof[] = [];

  if (typeof result.cleared === 'string') {
    proof.push({
      label: 'Temporary storage cleared',
      change: result.cleared,
      status: result.success ? 'changed' : 'not changed',
      meaning: 'This is the amount of temporary file space RigMD removed.',
    });
  }

  if (typeof result.deleted_items === 'number') {
    proof.push({
      label: 'Items removed',
      after: result.deleted_items,
      status: 'observed',
      meaning: 'Temporary files or folders removed from the approved cleanup scope.',
    });
  }

  if (typeof result.skipped_errors === 'number') {
    proof.push({
      label: 'Locked items skipped',
      after: result.skipped_errors,
      status: 'safe skip',
      meaning: 'Windows was using these files, so RigMD left them alone.',
    });
  }

  if (result.needs_admin) {
    proof.push({
      label: 'Administrator permission',
      status: 'blocked',
      meaning: 'Windows blocked this action because the backend is not running as Administrator. No changes were made.',
    });
  }

  if (proof.length === 0) {
    proof.push({
      label: result.success ? 'Execution status' : 'Action blocked',
      status: result.success ? 'completed' : 'not completed',
      meaning: result.summary,
    });
  }

  return proof;
}

export default function NewDiagnosisView() {
  const [step, setStep] = useState<number>(1);
  const [isModalOpen, setIsModalOpen] = useState<boolean>(false);

  const [formData, setFormData] = useState<DiagnosticFormData>({
    symptom_type: '',
    affected_activity: '',
    frequency: '',
    severity: '',
    warning_signs: '',
    recent_changes: '',
    system_state: '',
  });

  const [loading, setLoading] = useState<boolean>(false);
  const [remediating, setRemediating] = useState<boolean>(false);
  const [inspecting, setInspecting] = useState<boolean>(false);
  const [report, setReport] = useState<DiagnosticReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [remediationActions, setRemediationActions] = useState<RemediationAction[]>([]);
  const [selectedAction, setSelectedAction] = useState<RemediationAction | null>(null);
  const [actionResults, setActionResults] = useState<Record<string, ActionResult>>({});

  const currentBranch = QUESTION_BRANCHES[formData.symptom_type] || QUESTION_BRANCHES['thermal condition'];

  const selectedSymptom = CATEGORY_OPTIONS.find((item) => item.value === formData.symptom_type)?.label ?? '';
  const selectedActivity = formData.affected_activity
    ? currentBranch.activities.find((item) => item.value === formData.affected_activity)?.label ?? formData.affected_activity
    : '';
  const selectedWarning = formData.warning_signs
    ? currentBranch.warningSigns.find((item) => item.value === formData.warning_signs)?.label ?? formData.warning_signs
    : '';
  const selectedChange = formData.recent_changes
    ? currentBranch.recentChanges.find((item) => item.value === formData.recent_changes)?.label ?? formData.recent_changes
    : '';

  const answerSummary = [
    { label: 'Symptom', value: selectedSymptom },
    { label: 'Activity', value: selectedActivity },
    { label: 'Frequency', value: formData.frequency },
    { label: 'Severity', value: formData.severity },
    { label: 'Warning Sign', value: selectedWarning },
    { label: 'Recent Change', value: selectedChange },
    { label: 'Description', value: formData.system_state.trim() },
  ];

  const completedAnswerCount = answerSummary.filter((item) => Boolean(item.value)).length;

  const canContinue = () => {
    if (step === 1) return Boolean(formData.symptom_type);
    if (step === 2) return Boolean(formData.severity && formData.frequency && formData.affected_activity);
    if (step === 3) return Boolean(formData.warning_signs && formData.recent_changes);
    return true;
  };

  const handleCategoryChange = (e: ChangeEvent<HTMLSelectElement>) => {
    const nextCategory = e.target.value;

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
    setActionResults({});
    setSelectedAction(null);
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

      const response = await axios.post<DiagnosticReport>(`${API_BASE_URL}/api/diagnosis/submit`, payload, {
        headers: { 'Content-Type': 'application/json' },
      });

      const diagnosis = response.data;
      setReport(diagnosis);

      if (diagnosis.diagnosed_category.toLowerCase() === 'no active issue detected') {
        setRemediationActions([]);
        return;
      }

      const actionsResponse = await axios.get(`${API_BASE_URL}/api/remediation/actions`, {
        params: { category: diagnosis.diagnosed_category },
      });

      setRemediationActions(actionsResponse.data.actions ?? []);
    } catch (err: any) {
      console.error('Diagnostic error:', err);
      setError(err.response?.data?.detail || err.message || 'The engine encountered an error parsing the issue.');
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

      await axios.post(`${API_BASE_URL}/api/remediation/open-target`, { target: targetParam });
    } catch (err) {
      console.error('Inspection error:', err);
      setError('RigMD could not open the Windows verification tool. You can still open it manually from Windows Search.');
    } finally {
      setInspecting(false);
    }
  };

  const handleApplyFix = async () => {
    if (!selectedAction) return;

    setIsModalOpen(false);
    setRemediating(true);

    try {
      const response = await axios.post(`${API_BASE_URL}/api/remediation/execute`, {
        action_id: selectedAction.id,
      });

      setActionResults((prev) => ({
        ...prev,
        [selectedAction.id]: response.data,
      }));
    } catch (err: any) {
      setActionResults((prev) => ({
        ...prev,
        [selectedAction.id]: {
          success: false,
          summary: err.response?.data?.detail || 'The selected action could not be completed.',
        },
      }));
    } finally {
      setRemediating(false);
      setSelectedAction(null);
    }
  };

  const handlePostExecutionInspect = async (action: RemediationAction) => {
    const preview = getActionPreview(action, report);
    const target = preview.postTarget || report?.verification_target?.target;

    if (!target) return;

    setInspecting(true);

    try {
      await axios.post(`${API_BASE_URL}/api/remediation/open-target`, { target });
    } catch (err) {
      console.error('Post-execution inspection error:', err);
      setError('RigMD could not open the related Windows location. You can still open it manually from Windows Search.');
    } finally {
      setInspecting(false);
    }
  };

  const resetDiagnosis = () => {
    setStep(1);
    setReport(null);
    setError(null);
    setRemediationActions([]);
    setSelectedAction(null);
    setActionResults({});
    setFormData({
      symptom_type: '',
      affected_activity: '',
      frequency: '',
      severity: '',
      warning_signs: '',
      recent_changes: '',
      system_state: '',
    });
  };

  const getActionStyles = (action: string | undefined | null) => {
    const value = (action ?? '').toLowerCase();
    if (value.includes('monitor')) return { badge: 'border-blue-500/30 text-blue-400 bg-blue-500/10', border: 'border-l-blue-400' };
    if (value.includes('maintain')) return { badge: 'border-emerald-500/30 text-emerald-400 bg-emerald-500/10', border: 'border-l-emerald-400' };
    if (value.includes('troubleshoot')) return { badge: 'border-orange-500/30 text-orange-400 bg-orange-500/10', border: 'border-l-orange-400' };
    if (value.includes('escalate')) return { badge: 'border-red-500/30 text-red-400 bg-red-500/10', border: 'border-l-red-400' };
    return { badge: 'border-cyan-500/30 text-cyan-400 bg-cyan-500/10', border: 'border-l-cyan-400' };
  };

  const selectStyle = 'w-full mt-2 rounded-xl border border-[#30363d] bg-[#0d1117] px-5 py-3.5 text-[15px] text-gray-100 outline-none focus:border-cyan-500/50 transition-all cursor-pointer disabled:cursor-not-allowed disabled:opacity-50';
  const inputStyle = 'w-full mt-2 rounded-xl border border-[#30363d] bg-[#0d1117] px-5 py-3.5 text-[15px] text-gray-100 outline-none focus:border-cyan-500/50 transition-all';

  const diagnosticSteps = [
    { label: 'Symptom Type', detail: 'General Problem', num: 1 },
    { label: 'Behavior Details', detail: 'Severity and frequency', num: 2 },
    { label: 'Context Clues', detail: 'Warnings and changes', num: 3 },
    { label: 'Review Answers', detail: 'Confirm details', num: 4 },
    { label: 'Diagnosis', detail: 'Result and next steps', num: 5 },
  ];

  const progressPercent = Math.round((step / diagnosticSteps.length) * 100);

  return (
    <>
      <TopHeader title="Guided Checkup" subtitle="Let's identify what's slowing down your computer" />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-5 py-5 lg:px-6">
        <div className="grid w-full grid-cols-1 gap-5 xl:grid-cols-[250px_minmax(0,1fr)_292px]">
          <aside className="hidden self-start overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22] xl:block">
            <div className="border-b border-[#30363d] px-4 py-4">
              <div className="flex items-center gap-2">
                <span className="h-4 w-1 rounded-full bg-cyan-400" />
                <h3 className="text-[13px] font-bold uppercase tracking-[0.18em] text-white">Diagnostic Steps</h3>
              </div>
            </div>

            <div className="space-y-2 p-3">
              {diagnosticSteps.map((item) => {
                const completed = step > item.num;
                const active = step === item.num;

                return (
                  <div key={item.num} className={`flex items-center gap-3 rounded-xl border px-3.5 py-3.5 transition ${
                    active ? 'border-cyan-500/40 bg-cyan-500/10'
                      : completed ? 'border-emerald-500/20 bg-emerald-500/5'
                        : 'border-transparent bg-transparent opacity-55'
                  }`}>
                    <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full border text-xs font-bold ${
                      active ? 'border-cyan-400 bg-cyan-400 text-[#041014] shadow-[0_0_14px_rgba(34,211,238,0.35)]'
                        : completed ? 'border-emerald-500/50 bg-emerald-500/10 text-emerald-400'
                          : 'border-[#30363d] bg-[#0d1117] text-slate-600'
                    }`}>
                      {completed ? <CheckCircle2 size={14} /> : item.num}
                    </span>

                    <div className="min-w-0">
                      <p className={`truncate text-[13px] font-bold ${active ? 'text-white' : completed ? 'text-slate-200' : 'text-slate-500'}`}>
                        {item.label}
                      </p>
                      <p className={`mt-0.5 truncate text-xs ${completed ? 'text-emerald-400' : active ? 'text-cyan-400' : 'text-slate-600'}`}>
                        {completed ? 'Completed' : active ? 'In progress' : item.detail}
                      </p>
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="m-3 rounded-xl border border-[#30363d] bg-[#0d1117] p-3 text-[11px] leading-relaxed text-slate-500">
              Answers update the diagnostic result only after you run the final check.
            </div>
          </aside>

          <div className="min-w-0 space-y-5">
            <section className="overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22]">
              <div className="flex flex-col gap-3 border-b border-[#30363d] px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <div className="flex items-center gap-2 text-[11px] font-bold uppercase tracking-[0.2em] text-slate-500">
                    <Stethoscope size={14} className="text-cyan-400" />
                    Guided Diagnostic
                  </div>
                  <h3 className="mt-1 text-xl font-bold text-white">{diagnosticSteps[step - 1]?.label}</h3>
                </div>

                <div className="text-left sm:text-right">
                  <p className="text-xs font-semibold text-slate-500">
                    Step {step} of {diagnosticSteps.length}
                    <span className="ml-2 text-cyan-400">{progressPercent}%</span>
                  </p>
                  <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full bg-[#0d1117] sm:w-44">
                    <div className="h-full rounded-full bg-cyan-400 transition-all" style={{ width: `${progressPercent}%` }} />
                  </div>
                </div>
              </div>
            </section>

            <div className="flex min-h-[520px] flex-col justify-between rounded-2xl border border-[#30363d] bg-[#161b22] p-7">
              {step === 1 && (
                <div className="animate-fadeIn">
                  <div className="mb-6 flex items-center gap-3">
                    <HelpCircle className="text-cyan-400" size={24} />
                    <h3 className="text-xl font-bold text-white">What seems to be the main issue?</h3>
                  </div>

                  <label className="text-xs font-bold uppercase tracking-wider text-gray-400">Select a category</label>
                  <select name="symptom_type" value={formData.symptom_type} onChange={handleCategoryChange} className={selectStyle}>
                    <option value="" disabled>Select the main issue</option>
                    {CATEGORY_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </select>
                </div>
              )}

              {step === 2 && (
                <div className="animate-fadeIn space-y-6">
                  <div className="flex items-center gap-3">
                    <Activity className="text-cyan-400" size={24} />
                    <h3 className="text-xl font-bold text-white">How does it behave?</h3>
                  </div>

                  <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                    <div>
                      <label className="text-xs font-bold uppercase tracking-wider text-gray-400">How disruptive is this issue?</label>
                      <select name="severity" value={formData.severity} onChange={handleInputChange} className={selectStyle}>
                        <option value="" disabled>Select disruption level</option>
                        <option value="Low">Low - Annoying but manageable</option>
                        <option value="Medium">Medium - Noticeable performance drops</option>
                        <option value="High">High - Hard to use the computer at all</option>
                      </select>
                    </div>

                    <div>
                      <label className="text-xs font-bold uppercase tracking-wider text-gray-400">How often does it happen?</label>
                      <select name="frequency" value={formData.frequency} onChange={handleInputChange} className={selectStyle}>
                        <option value="" disabled>Select frequency</option>
                        <option value="rarely">Rarely - Only happened once or twice</option>
                        <option value="intermittent">Sometimes - It comes and goes</option>
                        <option value="frequent">Frequently - Happens almost every day</option>
                        <option value="always">Constantly - It never stops happening</option>
                      </select>
                    </div>
                  </div>

                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">{currentBranch.step2Label}</label>
                    <select name="affected_activity" value={formData.affected_activity} onChange={handleInputChange} className={selectStyle}>
                      <option value="" disabled>Select when it happens</option>
                      {currentBranch.activities.map((act) => (
                        <option key={act.value} value={act.value}>{act.label}</option>
                      ))}
                    </select>
                  </div>
                </div>
              )}

              {step === 3 && (
                <div className="animate-fadeIn space-y-6">
                  <div className="flex items-center gap-3">
                    <AlertTriangle className="text-cyan-400" size={24} />
                    <h3 className="text-xl font-bold text-white">Any other clues?</h3>
                  </div>

                  <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                    <div>
                      <label className="text-xs font-bold uppercase tracking-wider text-gray-400">{currentBranch.warningSignsLabel}</label>
                      <select name="warning_signs" value={formData.warning_signs} onChange={handleInputChange} className={selectStyle}>
                        <option value="" disabled>Select warning sign</option>
                        {currentBranch.warningSigns.map((ws) => (
                          <option key={ws.value} value={ws.value}>{ws.label}</option>
                        ))}
                      </select>
                    </div>

                    <div>
                      <label className="text-xs font-bold uppercase tracking-wider text-gray-400">{currentBranch.recentChangesLabel}</label>
                      <select name="recent_changes" value={formData.recent_changes} onChange={handleInputChange} className={selectStyle}>
                        <option value="" disabled>Select recent change</option>
                        {currentBranch.recentChanges.map((rc) => (
                          <option key={rc.value} value={rc.value}>{rc.label}</option>
                        ))}
                      </select>
                    </div>
                  </div>

                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">Describe the state in your own words (Optional)</label>
                    <textarea
                      name="system_state"
                      value={formData.system_state}
                      onChange={handleInputChange}
                      placeholder="e.g., My desktop gets hot near the rear vents and gameplay starts lagging."
                      className={`${inputStyle} min-h-[80px] resize-none`}
                    />
                  </div>
                </div>
              )}

              {step === 4 && (
                <div className="animate-fadeIn space-y-4">
                  <div className="flex items-center gap-3">
                    <CheckCircle2 className="text-emerald-400" size={24} />
                    <h3 className="text-xl font-bold text-white">Review Your Information</h3>
                  </div>

                  <div className="grid grid-cols-1 gap-4 rounded-xl border border-[#253041] bg-[#0d1117] p-4 text-sm md:grid-cols-2">
                    <div><span className="block text-xs font-bold uppercase text-gray-500">Selected Issue Group</span><span className="font-medium text-white">{selectedSymptom}</span></div>
                    <div><span className="block text-xs font-bold uppercase text-gray-500">Disruption Level</span><span className="font-medium text-white">{formData.severity}</span></div>
                    <div><span className="block text-xs font-bold uppercase text-gray-500">How Frequently</span><span className="font-medium capitalize text-white">{formData.frequency}</span></div>
                    <div><span className="block text-xs font-bold uppercase text-gray-500">Active Activity</span><span className="font-medium text-white">{selectedActivity}</span></div>
                    <div className="border-t border-[#253041] pt-3 md:col-span-2">
                      <span className="mb-1 block text-xs font-bold uppercase text-gray-500">Your Description</span>
                      <span className="text-xs italic text-gray-300">{formData.system_state || 'No additional description added.'}</span>
                    </div>
                  </div>
                </div>
              )}

              {step === 5 && (
                <div className="animate-fadeIn flex flex-1 flex-col justify-center py-4">
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
                      <div className="flex items-center justify-between border-b border-[#30363d] pb-4">
                        <div>
                          <span className={`rounded-md border px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider ${getActionStyles(report.action_category).badge}`}>
                            {report.action_category}
                          </span>
                          <h4 className="mt-3 text-2xl font-bold text-white">{report.diagnosed_category}</h4>
                        </div>
                        <div className="text-right">
                          <span className="block text-xs font-bold uppercase tracking-wider text-gray-500">Analysis Certainty</span>
                          <span className="text-sm font-bold text-cyan-400">{report.confidence_label}</span>
                        </div>
                      </div>

                      <div>
                        <h5 className="mb-2 text-xs font-bold uppercase tracking-wider text-gray-500">What's Happening Under the Hood</h5>
                        <p className="rounded-xl border border-[#253041] bg-[#0d1117] p-4 text-sm leading-relaxed text-gray-300 shadow-inner">
                          {report.ai_explanation}
                        </p>
                      </div>

                      {report.proof && report.proof.length > 0 && (
                        <div>
                          <h5 className="mb-2 text-xs font-bold uppercase tracking-wider text-gray-500">Live Scan Proof</h5>
                          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                            {report.proof.map((item) => (
                              <div key={item.label} className={`rounded-xl border p-4 ${getProofCardStyle(item.status)}`}>
                                <p className="text-xs uppercase tracking-wider text-gray-500">{item.label}</p>
                                <p className="mt-1 text-sm font-bold text-white">{item.value}</p>
                                <p className="mt-2 text-xs leading-relaxed text-gray-400">{item.meaning}</p>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      {!isNoActiveIssue(report) && (
                        <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
                          <h5 className="mb-3 flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-gray-500">
                            <FileText size={14} className="text-cyan-400" /> Targeted Verification Area
                          </h5>
                          <div className="flex flex-col items-start justify-between gap-3 rounded-lg border border-[#253041] bg-[#161b22] p-3 md:flex-row md:items-center">
                            <div className="w-full flex-1 select-all break-all rounded border border-[#30363d] bg-[#0d1117] px-3 py-2 font-mono text-xs text-cyan-400 md:w-auto">
                              {getVerificationTarget()}
                            </div>
                            <button type="button" disabled={inspecting} onClick={handleVerifyLocation} className="w-full shrink-0 rounded-lg border border-cyan-500/40 bg-cyan-500/5 px-4 py-2 text-center text-xs font-bold text-cyan-400 transition-all hover:bg-cyan-500/10 disabled:opacity-50 md:w-auto">
                              {inspecting ? 'Opening...' : 'Verify & Inspect'}
                            </button>
                          </div>
                          <p className="mt-2 text-[11px] italic text-gray-500">{getVerificationDescription()}</p>
                        </div>
                      )}

                      <div className={`rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4 border-l-4 ${getActionStyles(report.action_category).border}`}>
                        <h5 className="mb-1 text-xs font-bold uppercase tracking-wider text-white">Recommended Next Steps</h5>
                        <p className="text-xs leading-relaxed text-gray-400">{report.recommended_next_step}</p>
                      </div>

                      {isNoActiveIssue(report) && (
                        <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-4 text-xs text-emerald-300">
                          No safe fix is needed right now because the live scan did not detect active system pressure.
                        </div>
                      )}

                      {!isNoActiveIssue(report) && remediationActions.length > 0 && (
                        <div className="space-y-3">
                          <h5 className="text-xs font-bold uppercase tracking-wider text-white">Safe Actions Available</h5>
                          {remediationActions.map((action) => {
                            const result = actionResults[action.id];

                            return (
                              <div key={action.id} className="flex flex-col items-start justify-between gap-4 rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-5 md:flex-row md:items-center">
                                <div className="min-w-0 flex-1">
                                  <div className="mb-1 flex items-center gap-2 text-sm font-bold text-cyan-400">
                                    <ShieldCheck size={16} />
                                    <h5>{action.label}</h5>
                                  </div>
                                  <p className="mb-2 text-xs text-gray-400">{action.description}</p>
                                  <p className="text-[11px] text-gray-500">{action.risk}</p>

                                  {result && (
                                    <div className={`mt-3 rounded-lg border p-3 text-xs ${
                                      result.success ? 'border-emerald-500/20 bg-emerald-500/5 text-emerald-300' : 'border-red-500/20 bg-red-500/5 text-red-300'
                                    }`}>
                                      <p className="font-semibold">{result.summary}</p>

                                      <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-2">
                                        {buildActionProof(result).map((proofItem) => (
                                          <div key={`${action.id}-${proofItem.label}`} className="rounded-lg border border-[#253041] bg-[#0d1117] p-3">
                                            <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{proofItem.label}</p>
                                            <p className="mt-1 text-xs font-semibold text-white">
                                              {proofItem.change ?? proofItem.after ?? proofItem.status ?? 'Checked'}
                                            </p>
                                            {proofItem.meaning && <p className="mt-1 text-[11px] leading-relaxed text-slate-500">{proofItem.meaning}</p>}
                                          </div>
                                        ))}
                                      </div>

                                      {getActionPreview(action, report).postTarget && (
                                        <button type="button" disabled={inspecting} onClick={() => handlePostExecutionInspect(action)} className="mt-3 rounded-lg border border-cyan-500/30 bg-cyan-500/5 px-3 py-2 text-[11px] font-bold text-cyan-400 transition-colors hover:bg-cyan-500/10 disabled:opacity-50">
                                          {inspecting ? 'Opening...' : getActionPreview(action, report).mode === 'automated' ? 'Inspect Change' : 'Open Again'}
                                        </button>
                                      )}
                                    </div>
                                  )}
                                </div>

                                {!result && (
                                  <button type="button" disabled={remediating} onClick={() => { setSelectedAction(action); setIsModalOpen(true); }} className="w-full shrink-0 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-500 px-5 py-2.5 text-center text-xs font-bold text-white shadow-lg transition-all hover:opacity-90 disabled:opacity-50 md:w-auto">
                                    {remediating ? 'Running...' : getActionPreview(action, report).buttonLabel}
                                  </button>
                                )}
                              </div>
                            );
                          })}
                        </div>
                      )}

                      {!isNoActiveIssue(report) && remediationActions.length === 0 && (
                        <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4 text-xs text-gray-500">
                          No automated fix is available for this diagnosis. RigMD will keep this case in guided mode to avoid unsafe changes.
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              <div className="mt-9 flex items-center justify-between border-t border-[#30363d] pt-5">
                {step > 1 && step < 5 ? (
                  <button type="button" onClick={prevStep} className="flex items-center gap-2 rounded-xl border border-[#30363d] bg-[#0d1117] px-5 py-3 text-sm font-semibold text-gray-300 transition-colors hover:bg-[#161b22]">
                    <ArrowLeft size={16} /> Back
                  </button>
                ) : <div />}

                {step < 4 ? (
                  <button type="button" disabled={!canContinue()} onClick={nextStep} className="ml-auto flex items-center gap-2 rounded-xl bg-cyan-500 px-6 py-3 text-sm font-bold text-[#041014] shadow-[0_0_12px_rgba(6,182,212,0.2)] transition-colors hover:bg-cyan-400 disabled:cursor-not-allowed disabled:opacity-40">
                    Next <ArrowRight size={16} />
                  </button>
                ) : step === 4 ? (
                  <button type="button" onClick={handleFormSubmit} className="ml-auto flex items-center gap-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-500 px-7 py-3.5 text-sm font-bold text-white shadow-[0_0_16px_rgba(6,182,212,0.3)] transition-all hover:opacity-90">
                    <Zap size={16} /> Run Diagnostics
                  </button>
                ) : step === 5 && !loading ? (
                  <button type="button" onClick={resetDiagnosis} className="ml-auto flex items-center gap-2 rounded-xl border border-cyan-500/30 bg-cyan-500/5 px-6 py-3 text-sm font-bold text-cyan-400 transition-colors hover:bg-cyan-500/10">
                    <Stethoscope size={16} /> Check Another Issue
                  </button>
                ) : <div />}
              </div>
            </div>
          </div>

          <aside className="hidden self-start overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22] xl:block">
            <div className="flex items-center justify-between border-b border-[#30363d] px-4 py-4">
              <div className="flex items-center gap-2">
                <FileText size={14} className="text-cyan-400" />
                <h3 className="text-[13px] font-bold uppercase tracking-[0.18em] text-white">Your Answers</h3>
              </div>
              <span className="text-[11px] font-semibold text-slate-500">{completedAnswerCount}/{answerSummary.length}</span>
            </div>

            <div className="space-y-2 p-3">
              {answerSummary.map((item) => {
                const isAnswered = Boolean(item.value);

                return (
                  <div key={item.label} className="rounded-xl border border-[#253041] bg-[#0d1117] px-3.5 py-3">
                    <div className="mb-1 flex items-center justify-between gap-2">
                      <span className="text-[10px] font-bold uppercase tracking-[0.18em] text-slate-600">{item.label}</span>
                      {isAnswered ? <CheckCircle2 size={12} className="text-emerald-400" /> : <span className="h-3 w-3 rounded-full border border-slate-600" />}
                    </div>
                    <p className={`line-clamp-2 text-[13px] font-semibold ${isAnswered ? 'text-slate-200' : 'text-slate-600'}`}>
                      {isAnswered ? item.value : 'Not answered'}
                    </p>
                  </div>
                );
              })}
            </div>

            <div className="m-3 rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-3">
              <div className="mb-2 flex items-center gap-2">
                <Info size={14} className="text-cyan-400" />
                <p className="text-[11px] font-bold uppercase tracking-wider text-white">Session Note</p>
              </div>
              <p className="text-[11px] leading-relaxed text-slate-500">
                Keep answers specific. The final diagnosis uses these selections together with live system signals.
              </p>
            </div>
          </aside>
        </div>
      </div>

      {isModalOpen && selectedAction && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4 backdrop-blur-sm animate-fadeIn">
          <div className="relative w-full max-w-lg rounded-2xl border border-[#30363d] bg-[#161b22] p-6 shadow-2xl">
            <div className="mb-4 flex items-center justify-between border-b border-[#30363d] pb-3">
              <div className="flex items-center gap-2.5 text-orange-400">
                <ShieldCheck size={22} className="text-cyan-400" />
                <h4 className="text-base font-bold text-white">Before RigMD Runs This</h4>
              </div>
              <button type="button" onClick={() => { setIsModalOpen(false); setSelectedAction(null); }} className="text-gray-500 transition-colors hover:text-white">
                <X size={18} />
              </button>
            </div>

            <p className="mb-4 text-xs leading-relaxed text-gray-400">
              Review what RigMD will do before running: <strong className="text-cyan-400 uppercase">{selectedAction.label}</strong>.
            </p>

            <div className="mb-5 space-y-4">
              {(() => {
                const preview = getActionPreview(selectedAction, report);

                return (
                  <>
                    <div className="rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4">
                      <div className="mb-2 flex items-center gap-2">
                        <span className="rounded-md border border-cyan-500/30 bg-cyan-500/10 px-2 py-1 text-[10px] font-bold uppercase text-cyan-300">
                          {preview.mode === 'automated' ? 'Automated Cleanup' : preview.mode === 'read-only' ? 'Read-Only Check' : 'Assisted Tool'}
                        </span>
                      </div>
                      <h5 className="text-sm font-bold text-white">{preview.title}</h5>
                      <p className="mt-2 text-xs leading-relaxed text-slate-400">{preview.plainSummary}</p>
                    </div>

                    <div className="grid grid-cols-1 gap-3 md:grid-cols-[1fr_auto_1fr_auto_1fr] md:items-stretch">
                      <div className="rounded-xl border border-[#253041] bg-[#0d1117] p-3">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Before</p>
                        <p className="mt-2 text-xs leading-relaxed text-slate-300">{preview.before}</p>
                      </div>
                      <div className="hidden items-center text-cyan-400 md:flex"><ArrowRight size={18} /></div>
                      <div className="rounded-xl border border-cyan-500/20 bg-[#0d1117] p-3">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-cyan-400">RigMD Does</p>
                        <p className="mt-2 text-xs leading-relaxed text-slate-300">{preview.during}</p>
                      </div>
                      <div className="hidden items-center text-cyan-400 md:flex"><ArrowRight size={18} /></div>
                      <div className="rounded-xl border border-emerald-500/20 bg-[#0d1117] p-3">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-emerald-400">After</p>
                        <p className="mt-2 text-xs leading-relaxed text-slate-300">{preview.after}</p>
                      </div>
                    </div>

                    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                      <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-3">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-emerald-400">Included</p>
                        <ul className="mt-2 space-y-1 text-[11px] text-slate-400">
                          {preview.included.map((item) => <li key={item}>- {item}</li>)}
                        </ul>
                      </div>
                      <div className="rounded-xl border border-red-500/20 bg-red-500/5 p-3">
                        <p className="text-[10px] font-bold uppercase tracking-wider text-red-300">Not Included</p>
                        <ul className="mt-2 space-y-1 text-[11px] text-slate-400">
                          {preview.excluded.map((item) => <li key={item}>- {item}</li>)}
                        </ul>
                      </div>
                    </div>

                    <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-3">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Proof RigMD Will Show After</p>
                      <ul className="mt-2 space-y-1 text-[11px] text-slate-400">
                        {preview.proof.map((item) => <li key={item}>- {item}</li>)}
                      </ul>
                    </div>
                  </>
                );
              })()}
            </div>

            <div className="flex items-center justify-end gap-3 border-t border-[#30363d] pt-3">
              <button type="button" onClick={() => { setIsModalOpen(false); setSelectedAction(null); }} className="rounded-xl border border-[#30363d] bg-[#0d1117] px-4 py-2 text-xs font-semibold text-gray-300 transition-colors hover:bg-[#161b22]">
                Cancel
              </button>
              <button type="button" onClick={handleApplyFix} className="rounded-xl bg-gradient-to-r from-cyan-500 to-blue-500 px-5 py-2.5 text-xs font-bold text-white shadow-lg hover:opacity-90">
                Confirm & Run
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}