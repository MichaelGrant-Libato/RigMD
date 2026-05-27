import { useState, ChangeEvent, FormEvent } from 'react';
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

interface DiagnosticReport {
  diagnosed_category: string;
  action_category: string;
  confidence_label: string;
  ai_explanation: string;
  recommended_next_step: string;
  proof?: DiagnosticProof[];
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;
}

interface ActionResult {
  success: boolean;
  summary: string;
  cleared?: string;
  cleared_bytes?: number;
  skipped_errors?: number;
}

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
      { value: 'none', label: 'The screen goes black without any sign' },
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
      { value: 'working', label: 'When returning from sleep or hibernation states' },
    ],
    warningSignsLabel: 'What warning is visible on startup blocks?',
    warningSigns: [
      { value: 'error message', label: 'An alert indicating no boot device found' },
      { value: 'loud fan noise', label: 'Fans spinning at full throttle immediately' },
      { value: 'none', label: 'The desktop layout just remains black' },
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
      { value: 'none', label: 'The screen flickers off and back on randomly' },
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

export default function NewDiagnosisView() {
  const [step, setStep] = useState<number>(1);
  const [isModalOpen, setIsModalOpen] = useState<boolean>(false);

  const [formData, setFormData] = useState<DiagnosticFormData>({
    symptom_type: 'thermal condition',
    affected_activity: 'gaming',
    frequency: 'intermittent',
    severity: 'Medium',
    warning_signs: 'none',
    recent_changes: 'none',
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

  const handleCategoryChange = (e: ChangeEvent<HTMLSelectElement>) => {
    const nextCategory = e.target.value;
    const fallbackConfig = QUESTION_BRANCHES[nextCategory];

    setFormData({
      symptom_type: nextCategory,
      affected_activity: fallbackConfig?.activities[0]?.value || 'gaming',
      frequency: 'intermittent',
      severity: 'Medium',
      warning_signs: fallbackConfig?.warningSigns[0]?.value || 'none',
      recent_changes: fallbackConfig?.recentChanges[0]?.value || 'none',
      system_state: '',
    });
  };

  const handleInputChange = (e: ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const nextStep = () => setStep((prev) => Math.min(prev + 1, 5));
  const prevStep = () => setStep((prev) => Math.max(prev - 1, 1));

  const handleFormSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError(null);
    setReport(null);
    setRemediationActions([]);
    setActionResults({});
    setSelectedAction(null);
    nextStep();

    try {
      const response = await axios.post<DiagnosticReport>(`${API_BASE_URL}/api/diagnosis/submit`, formData, {
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
    const category = report?.diagnosed_category.toLowerCase() ?? '';

    if (category.includes('no active issue')) return 'No verification target needed';
    if (category.includes('driver')) return 'Windows Device Manager';
    if (category.includes('storage')) return 'Read-only Windows disk scan';
    if (category.includes('boot') || category.includes('startup')) return 'Windows Startup Apps settings';
    if (category.includes('thermal')) return 'Cooling, fan behavior, and system load checks';
    if (category.includes('os performance')) return '%TEMP% and %TMP% temporary folders';

    return 'Windows diagnostic tools';
  };

  const handleVerifyLocation = async () => {
    if (!report || isNoActiveIssue(report)) return;

    setInspecting(true);
    try {
      const category = report.diagnosed_category.toLowerCase();
      const targetParam =
        category.includes('driver') ? 'device_manager'
          : category.includes('boot') || category.includes('startup') ? 'startup_apps'
            : category.includes('thermal') ? 'power'
              : 'temp';

      await axios.post(`${API_BASE_URL}/api/remediation/open-target`, { target: targetParam });
    } catch (err) {
      console.error('Inspection error:', err);
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

  const resetDiagnosis = () => {
    setStep(1);
    setReport(null);
    setError(null);
    setRemediationActions([]);
    setSelectedAction(null);
    setActionResults({});
  };

  const getActionStyles = (action: string | undefined | null) => {
    const value = (action ?? '').toLowerCase();
    if (value.includes('monitor')) return { badge: 'border-blue-500/30 text-blue-400 bg-blue-500/10', border: 'border-l-blue-400' };
    if (value.includes('maintain')) return { badge: 'border-emerald-500/30 text-emerald-400 bg-emerald-500/10', border: 'border-l-emerald-400' };
    if (value.includes('troubleshoot')) return { badge: 'border-orange-500/30 text-orange-400 bg-orange-500/10', border: 'border-l-orange-400' };
    if (value.includes('escalate')) return { badge: 'border-red-500/30 text-red-400 bg-red-500/10', border: 'border-l-red-400' };
    return { badge: 'border-cyan-500/30 text-cyan-400 bg-cyan-500/10', border: 'border-l-cyan-400' };
  };

  const selectStyle = 'w-full mt-2 rounded-xl border border-[#30363d] bg-[#0d1117] px-4 py-3 text-sm text-gray-200 outline-none focus:border-cyan-500/50 transition-all cursor-pointer';
  const inputStyle = 'w-full mt-2 rounded-xl border border-[#30363d] bg-[#0d1117] px-4 py-3 text-sm text-gray-200 outline-none focus:border-cyan-500/50 transition-all';

  return (
    <>
      <TopHeader title="Guided Checkup" subtitle="Let's identify what's slowing down your computer" />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="max-w-3xl mx-auto">
          <nav className="mb-8 flex items-center justify-between rounded-xl border border-[#30363d] bg-[#161b22] px-6 py-4">
            {[
              { label: 'General Problem', num: 1 },
              { label: 'Behaviors', num: 2 },
              { label: 'Context', num: 3 },
              { label: 'Review', num: 4 },
              { label: 'Diagnosis', num: 5 },
            ].map((s) => (
              <div key={s.num} className="flex items-center gap-2">
                <span className={`flex h-7 w-7 items-center justify-center rounded-full text-xs font-bold transition-all ${
                  step === s.num
                    ? 'bg-cyan-500 text-[#041014] shadow-[0_0_12px_rgba(34,211,238,0.3)]'
                    : step > s.num ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/40' : 'bg-[#0d1117] text-gray-500 border border-[#30363d]'
                }`}>
                  {step > s.num ? '✓' : s.num}
                </span>
                <span className={`hidden text-xs font-semibold md:inline ${step === s.num ? 'text-white' : 'text-gray-500'}`}>{s.label}</span>
                {s.num < 5 && <span className="hidden text-gray-600 md:inline mx-2">→</span>}
              </div>
            ))}
          </nav>

          <div className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6 min-h-[380px] flex flex-col justify-between">
            {step === 1 && (
              <div className="animate-fadeIn">
                <div className="mb-6 flex items-center gap-3">
                  <HelpCircle className="text-cyan-400" size={24} />
                  <h3 className="text-lg font-bold text-white">What seems to be the main issue?</h3>
                </div>
                <label className="text-xs font-bold uppercase tracking-wider text-gray-400">Select a category</label>
                <select name="symptom_type" value={formData.symptom_type} onChange={handleCategoryChange} className={selectStyle}>
                  <option value="thermal condition">Computer gets very hot / Loud fan noises</option>
                  <option value="os performance issues">System runs slowly / Freezes / Stutters</option>
                  <option value="driver-related issues">Blue screens / Random restarts / Glitches</option>
                  <option value="storage os-level issues">Files loading slowly / Errors saving files</option>
                  <option value="boot and startup issues">Takes forever to turn on / Won't start up</option>
                  <option value="display and rendering issues">Screen flickering / Strange visual lines</option>
                </select>
              </div>
            )}

            {step === 2 && (
              <div className="animate-fadeIn space-y-6">
                <div className="flex items-center gap-3">
                  <Activity className="text-cyan-400" size={24} />
                  <h3 className="text-lg font-bold text-white">How does it behave?</h3>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">How disruptive is this issue?</label>
                    <select name="severity" value={formData.severity} onChange={handleInputChange} className={selectStyle}>
                      <option value="Low">Low - Annoying but manageable</option>
                      <option value="Medium">Medium - Noticeable performance drops</option>
                      <option value="High">High - Hard to use the computer at all</option>
                    </select>
                  </div>
                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">How often does it happen?</label>
                    <select name="frequency" value={formData.frequency} onChange={handleInputChange} className={selectStyle}>
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
                  <h3 className="text-lg font-bold text-white">Any other clues?</h3>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">{currentBranch.warningSignsLabel}</label>
                    <select name="warning_signs" value={formData.warning_signs} onChange={handleInputChange} className={selectStyle}>
                      {currentBranch.warningSigns.map((ws) => (
                        <option key={ws.value} value={ws.value}>{ws.label}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="text-xs font-bold uppercase tracking-wider text-gray-400">{currentBranch.recentChangesLabel}</label>
                    <select name="recent_changes" value={formData.recent_changes} onChange={handleInputChange} className={selectStyle}>
                      {currentBranch.recentChanges.map((rc) => (
                        <option key={rc.value} value={rc.value}>{rc.label}</option>
                      ))}
                    </select>
                  </div>
                </div>
                <div>
                  <label className="text-xs font-bold uppercase tracking-wider text-gray-400">Describe the state in your own words (Optional)</label>
                  <textarea name="system_state" value={formData.system_state} onChange={handleInputChange} placeholder="e.g., My desktop gets hot near the rear vents and gameplay starts lagging." className={`${inputStyle} min-h-[80px] resize-none`} />
                </div>
              </div>
            )}

            {step === 4 && (
              <div className="animate-fadeIn space-y-4">
                <div className="flex items-center gap-3">
                  <CheckCircle2 className="text-emerald-400" size={24} />
                  <h3 className="text-lg font-bold text-white">Review Your Information</h3>
                </div>
                <div className="rounded-xl bg-[#0d1117] p-4 border border-[#253041] grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
                  <div><span className="text-gray-500 block text-xs uppercase font-bold">Selected Issue Group</span><span className="text-white font-medium capitalize">{formData.symptom_type}</span></div>
                  <div><span className="text-gray-500 block text-xs uppercase font-bold">Disruption Level</span><span className="text-white font-medium">{formData.severity}</span></div>
                  <div><span className="text-gray-500 block text-xs uppercase font-bold">How Frequently</span><span className="text-white font-medium capitalize">{formData.frequency}</span></div>
                  <div><span className="text-gray-500 block text-xs uppercase font-bold">Active Activity</span><span className="text-white font-medium capitalize">{formData.affected_activity}</span></div>
                  <div className="md:col-span-2 border-t border-[#253041] pt-3"><span className="text-gray-500 block text-xs uppercase font-bold mb-1">Your Description</span><span className="text-gray-300 italic text-xs">{formData.system_state || 'No additional description added.'}</span></div>
                </div>
              </div>
            )}

            {step === 5 && (
              <div className="animate-fadeIn flex-1 flex flex-col justify-center py-4">
                {loading && (
                  <div className="text-center py-12 space-y-4">
                    <div className="animate-spin h-10 w-10 border-4 border-cyan-500 border-t-transparent rounded-full mx-auto" />
                    <p className="text-sm font-semibold text-cyan-400 uppercase tracking-widest">Running Diagnostic Check...</p>
                  </div>
                )}

                {error && (
                  <div className="rounded-xl border border-red-500/20 bg-red-500/10 p-4 text-sm text-red-400 mb-4">
                    {error}
                  </div>
                )}

                {report && (
                  <div className="space-y-6">
                    <div className="flex items-center justify-between border-b border-[#30363d] pb-4">
                      <div>
                        <span className={`text-[10px] font-bold uppercase tracking-wider px-2.5 py-1 rounded-md border ${getActionStyles(report.action_category).badge}`}>
                          {report.action_category}
                        </span>
                        <h4 className="text-2xl font-bold text-white mt-3">{report.diagnosed_category}</h4>
                      </div>
                      <div className="text-right">
                        <span className="text-xs uppercase text-gray-500 block tracking-wider font-bold">Analysis Certainty</span>
                        <span className="text-sm font-bold text-cyan-400">{report.confidence_label}</span>
                      </div>
                    </div>

                    <div>
                      <h5 className="text-xs font-bold uppercase tracking-wider text-gray-500 mb-2">
                        What's Happening Under the Hood
                      </h5>
                      <p className="text-sm leading-relaxed text-gray-300 bg-[#0d1117] p-4 rounded-xl border border-[#253041] shadow-inner">
                        {report.ai_explanation}
                      </p>
                    </div>

                    {report.proof && report.proof.length > 0 && (
                      <div>
                        <h5 className="text-xs font-bold uppercase tracking-wider text-gray-500 mb-2">
                          Live Scan Proof
                        </h5>
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
                        <h5 className="text-xs font-bold uppercase tracking-wider text-gray-500 mb-3 flex items-center gap-2">
                          <FileText size={14} className="text-cyan-400" /> Targeted Verification Area
                        </h5>
                        <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-3 bg-[#161b22] border border-[#253041] p-3 rounded-lg">
                          <div className="font-mono text-xs text-cyan-400 break-all bg-[#0d1117] px-3 py-2 rounded border border-[#30363d] w-full md:w-auto flex-1 select-all">
                            {getVerificationTarget()}
                          </div>
                          <button type="button" disabled={inspecting} onClick={handleVerifyLocation} className="px-4 py-2 text-xs font-bold rounded-lg border border-cyan-500/40 text-cyan-400 bg-cyan-500/5 hover:bg-cyan-500/10 cursor-pointer shrink-0 transition-all w-full md:w-auto text-center disabled:opacity-50">
                            {inspecting ? 'Opening...' : 'Verify & Inspect'}
                          </button>
                        </div>
                        <p className="text-[11px] text-gray-500 mt-2 italic">
                          This opens the closest Windows tool or location related to the selected diagnosis.
                        </p>
                      </div>
                    )}

                    <div className={`rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-4 border-l-4 ${getActionStyles(report.action_category).border}`}>
                      <h5 className="text-xs font-bold uppercase tracking-wider text-white mb-1">Recommended Next Steps</h5>
                      <p className="text-xs leading-relaxed text-gray-400">{report.recommended_next_step}</p>
                    </div>

                    {isNoActiveIssue(report) && (
                      <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-4 text-xs text-emerald-300">
                        No safe fix is needed right now because the live scan did not detect active system pressure.
                      </div>
                    )}

                    {!isNoActiveIssue(report) && remediationActions.length > 0 && (
                      <div className="space-y-3">
                        <h5 className="text-xs font-bold uppercase tracking-wider text-white">Safe Assisted Fixes Available</h5>
                        {remediationActions.map((action) => {
                          const result = actionResults[action.id];

                          return (
                            <div key={action.id} className="rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-5 flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
                              <div className="min-w-0 flex-1">
                                <div className="flex items-center gap-2 text-cyan-400 font-bold text-sm mb-1">
                                  <ShieldCheck size={16} />
                                  <h5>{action.label}</h5>
                                </div>
                                <p className="text-xs text-gray-400 mb-2">{action.description}</p>
                                <p className="text-[11px] text-gray-500">{action.risk}</p>

                                {result && (
                                  <div className={`mt-3 rounded-lg border p-3 text-xs ${
                                    result.success
                                      ? 'border-emerald-500/20 bg-emerald-500/5 text-emerald-300'
                                      : 'border-red-500/20 bg-red-500/5 text-red-300'
                                  }`}>
                                    {result.summary}
                                    {result.cleared && <span className="ml-2 font-bold">({result.cleared} freed)</span>}
                                  </div>
                                )}
                              </div>

                              {!result && (
                                <button type="button" disabled={remediating} onClick={() => { setSelectedAction(action); setIsModalOpen(true); }} className="w-full md:w-auto px-5 py-2.5 text-xs font-bold rounded-xl bg-gradient-to-r from-cyan-500 to-blue-500 text-white hover:opacity-90 shadow-lg cursor-pointer shrink-0 disabled:opacity-50 transition-all text-center">
                                  {remediating ? 'Applying...' : 'Apply Safe Fix'}
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

            <div className="mt-8 pt-4 border-t border-[#30363d] flex items-center justify-between">
              {step > 1 && step < 5 ? (
                <button type="button" onClick={prevStep} className="flex items-center gap-2 px-4 py-2 text-sm font-semibold rounded-xl border border-[#30363d] text-gray-300 bg-[#0d1117] hover:bg-[#161b22] transition-colors cursor-pointer">
                  <ArrowLeft size={16} /> Back
                </button>
              ) : <div />}

              {step < 4 ? (
                <button type="button" onClick={nextStep} className="flex items-center gap-2 px-5 py-2.5 text-sm font-bold rounded-xl bg-cyan-500 text-[#041014] hover:bg-cyan-400 transition-colors shadow-[0_0_12px_rgba(6,182,212,0.2)] ml-auto cursor-pointer">
                  Next <ArrowRight size={16} />
                </button>
              ) : step === 4 ? (
                <button type="button" onClick={handleFormSubmit} className="flex items-center gap-2 px-6 py-3 text-sm font-bold rounded-xl bg-gradient-to-r from-cyan-500 to-blue-500 text-white hover:opacity-90 transition-all shadow-[0_0_16px_rgba(6,182,212,0.3)] ml-auto cursor-pointer">
                  <Zap size={16} /> Run Diagnostics
                </button>
              ) : step === 5 && !loading ? (
                <button type="button" onClick={resetDiagnosis} className="flex items-center gap-2 px-5 py-2.5 text-sm font-bold rounded-xl border border-cyan-500/30 text-cyan-400 bg-cyan-500/5 hover:bg-cyan-500/10 transition-colors ml-auto cursor-pointer">
                  <Stethoscope size={16} /> Check Another Issue
                </button>
              ) : <div />}
            </div>
          </div>
        </div>
      </div>

      {isModalOpen && selectedAction && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4 animate-fadeIn">
          <div className="w-full max-w-lg rounded-2xl border border-[#30363d] bg-[#161b22] p-6 shadow-2xl relative">
            <div className="flex items-center justify-between border-b border-[#30363d] pb-3 mb-4">
              <div className="flex items-center gap-2.5 text-orange-400">
                <ShieldCheck size={22} className="text-cyan-400" />
                <h4 className="font-bold text-white text-base">Pre-Execution Safety Verification</h4>
              </div>
              <button type="button" onClick={() => { setIsModalOpen(false); setSelectedAction(null); }} className="text-gray-500 hover:text-white transition-colors cursor-pointer">
                <X size={18} />
              </button>
            </div>

            <p className="text-xs text-gray-400 mb-4 leading-relaxed">
              RigMD is preparing to run this user-confirmed action: <strong className="text-cyan-400 uppercase">{selectedAction.label}</strong>.
            </p>

            <div className="space-y-3 mb-5">
              <div className="rounded-xl bg-[#0d1117] border border-[#253041] p-3.5 space-y-2 text-xs">
                <p className="font-bold text-gray-300 uppercase tracking-wider text-[10px] mb-1">Selected Action Scope</p>
                <p className="text-gray-400">{selectedAction.description}</p>
                <p className="text-gray-500">{selectedAction.risk}</p>
              </div>

              <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-3 flex items-start gap-3 border-l-4 border-l-emerald-500">
                <Info size={16} className="text-emerald-400 shrink-0 mt-0.5" />
                <div>
                  <h5 className="text-xs font-bold text-emerald-400 uppercase tracking-wider text-[10px] mb-0.5">Low-Risk Maintenance Scope</h5>
                  <p className="text-[11px] leading-relaxed text-gray-400">
                    RigMD only runs the selected maintenance action. For temp cleanup, it targets temporary folders only and skips locked files. Personal folders such as Documents, Desktop, Downloads, Photos, and Games are not part of the cleanup scope.
                  </p>
                </div>
              </div>
            </div>

            <div className="flex items-center justify-end gap-3 pt-3 border-t border-[#30363d]">
              <button type="button" onClick={() => { setIsModalOpen(false); setSelectedAction(null); }} className="px-4 py-2 text-xs font-semibold rounded-xl border border-[#30363d] text-gray-300 bg-[#0d1117] hover:bg-[#161b22] transition-colors cursor-pointer">
                Cancel
              </button>
              <button type="button" onClick={handleApplyFix} className="px-5 py-2.5 text-xs font-bold rounded-xl bg-gradient-to-r from-cyan-500 to-blue-500 text-white hover:opacity-90 shadow-lg cursor-pointer">
                Confirm & Run
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}