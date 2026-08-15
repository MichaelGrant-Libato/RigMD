//HardwareDashboard.tsx

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Stethoscope } from 'lucide-react';
import axios from 'axios';
import {
  Activity,
  AlertTriangle,
  Calendar,
  CheckCircle2,
  ChevronRight,
  Clock3,
  Cpu,
  FileText,
  HardDrive,
  History,
  Info,
  MemoryStick,
  Monitor,
  RefreshCw,
  ShieldAlert,
  Wrench,
  Zap,
  type LucideIcon,
} from 'lucide-react';

import AppSidebar from '../components/AppSidebar';
import TopHeader, { HeaderStatusProvider, type LiveDataStatus } from '../components/TopHeader';
import SystemProfileView from './SystemProfileView';
import DiagnosticHistoryView from './DiagnosticHistoryView';
import RecurringPatternsView from './RecurringPatternsView';
import WarningSignsView from './WarningSignsView';
import NewDiagnosisView from './NewDiagnosisView'; // Imported the separated module view
import HelpScopeView from './HelpScopeView';
import DiagnosticSessionDetailView from './DiagnosticSessionDetailView';

import type { DashboardSummary, HardwareStats, PageKey } from '../types/rigmd';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ??
  'http://localhost:5273';

interface DashboardMetricCardProps {
  icon: LucideIcon;
  title: string;
  value: string;
  subtitle: string;
  footer: string;
  tag: string;
  tagColor: string;
  iconColor: string;
}

interface QuickActionProps {
  icon: LucideIcon;
  title: string;
  description: string;
  primary?: boolean;
  onClick: () => void;
}

const emptyDashboard: DashboardSummary = {
  server_time: new Date().toISOString(),
  totals: {
    total_sessions: 0,
    this_month_count: 0,
    escalated_count: 0,
  },
  last_diagnosis: null,
  current_action_status: null,
  recurring_issues_count: 0,
  warning_signs_active_count: 0,
  action_distribution: [
    { label: 'Monitor', count: 0 },
    { label: 'Maintain', count: 0 },
    { label: 'Troubleshoot', count: 0 },
    { label: 'Escalate', count: 0 },
  ],
  session_frequency: [],
  recent_warning_signs: [],
  last_saved_session: null,
};

function formatTodayLabel() {
  return new Intl.DateTimeFormat('en-US', {
    month: 'long',
    day: '2-digit',
    year: 'numeric',
  }).format(new Date());
}

function formatLastUpdated(value: Date | null) {
  if (!value) {
    return 'Waiting for live hardware data';
  }

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(value);
}

function cleanValue(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') {
    return 'Detecting...';
  }

  if (String(value).toLowerCase() === 'unknown') {
    return 'Detecting...';
  }

  return String(value);
}

function isDetected(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') {
    return false;
  }

  return String(value).toLowerCase() !== 'unknown';
}

function getActionColor(action: string | undefined | null) {
  const value = (action ?? '').toLowerCase();

  if (value.includes('monitor')) {
    return 'border-blue-500/60 text-blue-400 bg-blue-500/10';
  }

  if (value.includes('maintain')) {
    return 'border-emerald-500/60 text-emerald-400 bg-emerald-500/10';
  }

  if (value.includes('troubleshoot')) {
    return 'border-orange-500/60 text-orange-400 bg-orange-500/10';
  }

  if (value.includes('escalate')) {
    return 'border-red-500/60 text-red-400 bg-red-500/10';
  }

  return 'border-cyan-500/60 text-cyan-400 bg-cyan-500/10';
}

function getWarningDotColor(index: number) {
  if (index === 0) {
    return 'bg-red-500';
  }

  if (index === 1) {
    return 'bg-orange-500';
  }

  return 'bg-yellow-500';
}

function QuickActionButton({ icon: Icon, title, description, primary = false, onClick }: QuickActionProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`group flex w-full items-center justify-between rounded-xl border p-4 text-left transition-all ${
        primary
          ? 'border-cyan-500/40 bg-cyan-500/90 text-[#041014] hover:bg-cyan-400'
          : 'border-[#30363d] bg-[#1f2937]/60 text-gray-200 hover:border-cyan-500/40 hover:bg-[#1f2937]'
      }`}
    >
      <div className="flex items-center gap-4">
        <div
          className={`rounded-lg p-2 ${
            primary ? 'bg-[#041014]/10 text-[#041014]' : 'bg-[#0d1117] text-cyan-400'
          }`}
        >
          <Icon size={20} />
        </div>

        <div>
          <p className={`text-sm font-bold ${primary ? 'text-[#041014]' : 'text-white'}`}>{title}</p>
          <p className={`text-xs ${primary ? 'text-[#062029]' : 'text-gray-500'}`}>{description}</p>
        </div>
      </div>

      <ChevronRight size={18} className={primary ? 'text-[#041014]' : 'text-gray-500 group-hover:text-cyan-400'} />
    </button>
  );
}

function QuickActionPanel({ setActivePage }: { setActivePage: (page: PageKey) => void }) {
  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
      <div className="mb-4 flex items-center gap-2">
        <span className="h-1.5 w-1.5 rounded-full bg-cyan-400" />
        <h3 className="font-semibold text-white">Quick Actions</h3>
      </div>

      <div className="space-y-3">
        <QuickActionButton
          icon={Stethoscope}
          title="Start Full Guided Diagnosis"
          description="Answer guided questions for a complete analysis"
          primary
          onClick={() => setActivePage('newDiagnosis')}
        />

        <QuickActionButton
          icon={History}
          title="View Diagnostic History"
          description="Review past sessions and trends"
          onClick={() => setActivePage('diagnosticHistory')}
        />
      </div>

      <div className="mt-10 rounded-xl border border-[#30363d] bg-[#0d1117]/80 p-4 text-xs leading-relaxed text-gray-500">
        RigMD provides probable diagnostic advisory only. Results do not replace professional hardware inspection.
      </div>
    </section>
  );
}

function ProfileField({
  icon: Icon,
  label,
  value,
  warning = false,
}: {
  icon: LucideIcon;
  label: string;
  value: string;
  warning?: boolean;
}) {
  return (
    <div className="flex min-w-0 items-center justify-between border-b border-[#26303a] py-3 last:border-b-0">
      <div className="flex min-w-0 items-start gap-3">
        <div className="rounded-lg bg-[#0d1117] p-2 text-cyan-400 shrink-0 mt-1">
          <Icon size={16} />
        </div>

        <div className="min-w-0">
          <p className="text-[11px] uppercase tracking-wider text-gray-500">{label}</p>
          <p className="whitespace-pre-line text-sm font-semibold text-white">{value}</p>
        </div>
      </div>

      {warning ? (
        <Info size={16} className="shrink-0 text-orange-400" />
      ) : (
        <CheckCircle2 size={16} className="shrink-0 text-emerald-400" />
      )}
    </div>
  );
}

function SystemStatusSummaryCard({
  stats,
  hardwareUpdatedAt,
}: {
  stats: HardwareStats | null;
  hardwareUpdatedAt: Date | null;
}) {
  const profile = useMemo(() => {
    const fields = [
      stats?.cpu?.name,
      stats?.ram?.total_gb,
      stats?.disk?.total_gb,
      stats?.os_version,
      stats?.gpu?.driver,
      stats?.chipset_driver,
      stats?.system_age,
    ];

    const detectedFields = fields.filter(isDetected).length;
    const profileComplete = Math.round((detectedFields / fields.length) * 100);

    return {
      profileName: 'Detected Desktop Profile',
      lastUpdated: formatLastUpdated(hardwareUpdatedAt),
      profileComplete,
      processor: cleanValue(stats?.cpu?.name),
      memory: stats?.ram?.total_gb ? `${stats.ram.total_gb} GB` : 'Detecting...',
      storage:
        stats?.storage_drives && stats.storage_drives.length > 0
          ? stats.storage_drives
              .map((drive) => {
                const size = drive.size_gb >= 1000 
                  ? `${(drive.size_gb / 1024).toFixed(0)}TB` 
                  : `${drive.size_gb}GB`;
                return `${size} ${drive.type}`;
              })
              .join('\n')
          : stats?.disk?.total_gb && stats?.storage_type
            ? `${stats.disk.total_gb} GB ${stats.storage_type}`
            : 'Detecting...',
      operatingSystem: cleanValue(stats?.os_version),
      gpuDriver: cleanValue(stats?.gpu?.driver),
      chipsetDriver: cleanValue(stats?.chipset_driver),
      systemAge: cleanValue(stats?.system_age),
    };
  }, [stats, hardwareUpdatedAt]);

  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6">
      <div className="mb-6 flex items-start justify-between gap-4">
        <div>
          <div className="mb-2 flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-emerald-400">
            <span className="h-2 w-2 rounded-full bg-emerald-400" />
            System Profile Active
          </div>

          <h3 className="text-xl font-bold text-white">{profile.profileName}</h3>
          <p className="mt-1 text-sm text-gray-500">Last updated: {profile.lastUpdated}</p>
        </div>

        <div className="flex flex-col items-center">
          <div className="flex h-16 w-16 items-center justify-center rounded-full border-4 border-cyan-400 text-lg font-bold text-cyan-400">
            {profile.profileComplete}%
          </div>

          <p className="mt-1 text-center text-[11px] text-gray-500">Profile Complete</p>
        </div>
      </div>

      <div className="mb-5 h-px w-full bg-cyan-400/60" />

      <div className="grid w-full grid-cols-1 gap-x-8 md:grid-cols-2">
        <ProfileField icon={Cpu} label="Processor" value={profile.processor} />
        <ProfileField icon={MemoryStick} label="Memory" value={profile.memory} />
        <ProfileField icon={HardDrive} label="Storage" value={profile.storage} />
        <ProfileField icon={Monitor} label="Operating System" value={profile.operatingSystem} />
        <ProfileField icon={Zap} label="GPU Driver" value={profile.gpuDriver} />
        <ProfileField icon={Zap} label="Chipset Driver" value={profile.chipsetDriver} warning />
        <ProfileField icon={Calendar} label="System Age" value={profile.systemAge} />
      </div>
    </section>
  );
}


function DashboardMetricCard({
  icon: Icon,
  title,
  value,
  subtitle,
  footer,
  tag,
  tagColor,
  iconColor,
}: DashboardMetricCardProps) {
  return (
    <section className="min-w-0 rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
      <div className="mb-6 flex items-start justify-between gap-3">
        <Icon size={20} className={iconColor} />
        <span className={`rounded border px-2 py-1 text-xs font-bold uppercase ${tagColor}`}>{tag}</span>
      </div>

      <p className="text-xs uppercase tracking-wider text-gray-500">{title}</p>
      <h3 className="mt-1 text-2xl font-bold text-white">{value}</h3>
      <p className="text-sm text-gray-500">{subtitle}</p>

      <div className="my-3 h-px w-full bg-cyan-400/40" />

      <p className="truncate text-sm text-gray-300">{footer}</p>
    </section>
  );
}

function RecentActivityCard({ dashboard }: { dashboard: DashboardSummary }) {
  const maxCount = Math.max(...dashboard.action_distribution.map((item) => item.count), 1);

  const colorMap: Record<string, string> = {
    Monitor: 'bg-blue-500',
    Maintain: 'bg-emerald-500',
    Troubleshoot: 'bg-orange-500',
    Escalate: 'bg-red-500',
  };

  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h3 className="font-semibold text-white">Action Category Distribution</h3>
          <p className="text-sm text-gray-500">How your diagnoses have been categorized over time</p>
        </div>

        <p className="text-xs text-gray-500">All-time · {dashboard.totals.total_sessions} sessions</p>
      </div>

      <div className="flex h-52 items-end gap-10 border-b border-[#30363d] px-8 pb-0">
        {dashboard.action_distribution.map((item) => (
          <div key={item.label} className="flex flex-1 flex-col items-center justify-end gap-2">
            <div
              className={`w-full max-w-[76px] rounded-t ${colorMap[item.label] ?? 'bg-cyan-500'}`}
              style={{
                height: item.count === 0 ? '6px' : `${Math.max((item.count / maxCount) * 150, 12)}px`,
              }}
            />

            <p className="text-xs text-gray-500">{item.label}</p>
          </div>
        ))}
      </div>

      <div className="mt-5 flex flex-wrap gap-4 text-sm text-gray-400">
        {dashboard.action_distribution.map((item) => (
          <div key={item.label} className="flex items-center gap-2">
            <span className={`h-2.5 w-2.5 rounded-full ${colorMap[item.label] ?? 'bg-cyan-500'}`} />
            <span>
              {item.label} <strong className="text-white">{item.count}</strong>
            </span>
          </div>
        ))}
      </div>
    </section>
  );
}

function SessionFrequencyCard({ dashboard }: { dashboard: DashboardSummary }) {
  const points = dashboard.session_frequency.slice(-8);
  const maxCount = Math.max(...points.map((item) => item.count), 1);

  const pathData =
    points.length > 0
      ? points
          .map((item, index) => {
            const x = 10 + index * (280 / Math.max(points.length - 1, 1));
            const y = 140 - (item.count / maxCount) * 110;

            return `${index === 0 ? 'M' : 'L'} ${x} ${y}`;
          })
          .join(' ')
      : 'M 10 140 L 290 140';

  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
      <h3 className="font-semibold text-white">Session Frequency</h3>
      <p className="text-sm text-gray-500">Last 30 days</p>

      <div className="mt-6 h-48">
        <svg viewBox="0 0 300 170" className="h-full w-full">
          <path
            d={pathData}
            fill="none"
            stroke="#22d3ee"
            strokeWidth="3"
            strokeLinecap="round"
            strokeLinejoin="round"
          />

          <line x1="10" y1="140" x2="290" y2="140" stroke="#30363d" />
          <line x1="10" y1="100" x2="290" y2="100" stroke="#1f2937" strokeDasharray="4 4" />
          <line x1="10" y1="60" x2="290" y2="60" stroke="#1f2937" strokeDasharray="4 4" />
        </svg>
      </div>

      <div className="mt-4 flex items-end justify-between">
        <div>
          <p className="text-xs text-gray-500">Total sessions</p>
          <p className="text-xl font-bold text-white">{dashboard.totals.total_sessions}</p>
        </div>

        <div className="text-right">
          <p className="text-xs text-gray-500">This month</p>
          <p className="text-xl font-bold text-cyan-400">{dashboard.totals.this_month_count}</p>
        </div>
      </div>
    </section>
  );
}

function ActiveWarningSignsCard({
  dashboard,
  setActivePage,
}: {
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
}) {
  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
      <div className="mb-5 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <AlertTriangle size={16} className="text-orange-400" />
          <h3 className="font-semibold text-white">Recent Warning Signs</h3>
        </div>

        <button
          type="button"
          onClick={() => setActivePage('warningSigns')}
          className="text-xs font-semibold text-cyan-400 hover:text-cyan-300"
        >
          View all
        </button>
      </div>

      {dashboard.recent_warning_signs.length === 0 ? (
        <p className="text-sm text-gray-500">No warning signs recorded yet.</p>
      ) : (
        <div className="space-y-5">
          {dashboard.recent_warning_signs.map((warning, index) => (
            <div key={warning.id} className="flex items-start gap-3">
              <span className={`mt-1.5 h-2 w-2 rounded-full ${getWarningDotColor(index)}`} />

              <div>
                <p className="text-sm font-semibold text-white">{warning.warning_sign}</p>
                <p className="text-xs text-gray-500">{warning.display_date ?? 'No date available'}</p>
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function LastSavedSessionCard({
  dashboard,
  setActivePage,
}: {
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
}) {
  const session = dashboard.last_saved_session;

  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-5">
      <div className="mb-5 flex items-center gap-2">
        <FileText size={16} className="text-cyan-400" />
        <h3 className="font-semibold text-white">Last Saved Session</h3>
      </div>

      {!session ? (
        <p className="text-sm text-gray-500">No saved diagnostic session yet.</p>
      ) : (
        <div className="space-y-4">
          <div>
            <p className="text-[11px] uppercase tracking-wider text-gray-500">Symptom</p>
            <p className="font-semibold text-white">{session.symptom_type}</p>
          </div>

          <div>
            <p className="text-[11px] uppercase tracking-wider text-gray-500">Probable Cause</p>
            <p className="font-semibold text-white">{session.diagnosed_category}</p>
          </div>

          <div className="flex gap-2">
            <span className={`rounded border px-2 py-1 text-xs font-bold uppercase ${getActionColor(session.action_category)}`}>
              {session.action_category}
            </span>

            <span className="rounded border border-orange-500/50 bg-orange-500/10 px-2 py-1 text-xs font-bold uppercase text-orange-400">
              {session.confidence_label}
            </span>
          </div>

          <div className="flex items-center gap-2 text-xs text-gray-500">
            <Clock3 size={13} />
            {session.display_date ?? 'No date available'}
          </div>

          <button
            type="button"
            onClick={() => setActivePage('diagnosticHistory')}
            className="flex w-full items-center justify-between rounded-lg border border-[#30363d] bg-[#0d1117] px-4 py-2.5 text-sm font-semibold text-cyan-400 hover:border-cyan-500/40"
          >
            View Full Result
            <ChevronRight size={16} />
          </button>
        </div>
      )}
    </section>
  );
}

function HomeDashboardView({
  stats,
  dashboard,
  hardwareUpdatedAt,
  setActivePage,
}: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  setActivePage: (page: PageKey) => void;
}) {
  const lastDiagnosis = dashboard.last_diagnosis;
  const currentAction = dashboard.current_action_status;

  return (
    <>
      <TopHeader title="Home Dashboard" subtitle={`System overview and diagnostic status — ${formatTodayLabel()}`} />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        {dashboard.database_warning && (
          <div className="mb-5 rounded-xl border border-orange-500/30 bg-orange-500/10 px-4 py-3 text-sm text-orange-300">
            Database dashboard data is not available yet. Check your Supabase connection and tables.
          </div>
        )}

        <div className="grid w-full grid-cols-1 gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div className="min-w-0 space-y-5">
            <SystemStatusSummaryCard stats={stats} hardwareUpdatedAt={hardwareUpdatedAt} />

            <div className="grid w-full grid-cols-1 gap-4 lg:grid-cols-2 2xl:grid-cols-4">
              <DashboardMetricCard
                icon={Clock3}
                title="Last Diagnosis"
                value={
                  lastDiagnosis?.days_ago !== null && lastDiagnosis?.days_ago !== undefined
                    ? `${lastDiagnosis.days_ago} days ago`
                    : 'No sessions'
                }
                subtitle={lastDiagnosis?.display_date ?? 'No saved diagnosis yet'}
                footer={lastDiagnosis ? `${lastDiagnosis.symptom_type} → ${lastDiagnosis.diagnosed_category}` : 'Complete a diagnostic session first'}
                tag={lastDiagnosis?.action_category ?? 'None'}
                tagColor={getActionColor(lastDiagnosis?.action_category)}
                iconColor="text-cyan-400"
              />

              <DashboardMetricCard
                icon={Activity}
                title="Current Action Status"
                value={currentAction?.action_category ?? 'No action'}
                subtitle={currentAction ? 'From last session' : 'No saved session yet'}
                footer={currentAction?.diagnosed_category ?? 'Action status will appear after diagnosis'}
                tag={currentAction?.action_category ?? 'None'}
                tagColor={getActionColor(currentAction?.action_category)}
                iconColor="text-emerald-400"
              />

              <DashboardMetricCard
                icon={RefreshCw}
                title="Recurring Issues"
                value={String(dashboard.recurring_issues_count)}
                subtitle="Detected patterns"
                footer={dashboard.recurring_issues_count > 0 ? 'Recurring symptoms found in saved sessions' : 'No repeated symptoms detected'}
                tag="Dynamic"
                tagColor="border-orange-500/60 text-orange-400 bg-orange-500/10"
                iconColor="text-orange-400"
              />

              <DashboardMetricCard
                icon={ShieldAlert}
                title="Warning Signs Active"
                value={String(dashboard.warning_signs_active_count)}
                subtitle="Recorded warnings"
                footer={dashboard.warning_signs_active_count > 0 ? 'Warning signs exist in session records' : 'No warning signs recorded'}
                tag={dashboard.warning_signs_active_count > 0 ? 'Review' : 'Clear'}
                tagColor={
                  dashboard.warning_signs_active_count > 0
                    ? 'border-red-500/60 text-red-400 bg-red-500/10'
                    : 'border-emerald-500/60 text-emerald-400 bg-emerald-500/10'
                }
                iconColor={dashboard.warning_signs_active_count > 0 ? 'text-red-400' : 'text-emerald-400'}
              />
            </div>

            <div className="grid w-full grid-cols-1 gap-5 2xl:grid-cols-[minmax(0,2fr)_minmax(320px,1fr)]">
              <RecentActivityCard dashboard={dashboard} />
              <SessionFrequencyCard dashboard={dashboard} />
            </div>
          </div>

          <aside className="min-w-0 space-y-5">
            <QuickActionPanel setActivePage={setActivePage} />
            <ActiveWarningSignsCard dashboard={dashboard} setActivePage={setActivePage} />
            <LastSavedSessionCard dashboard={dashboard} setActivePage={setActivePage} />
          </aside>
        </div>
      </div>
    </>
  );
}

function PlaceholderView({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <>
      <TopHeader title={title} subtitle={subtitle} />

      <div className="custom-scrollbar flex-1 overflow-y-auto p-8">
        <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-8">
          <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-xl border border-cyan-500/30 bg-cyan-500/10 text-cyan-400">
            <Wrench size={24} />
          </div>

          <h3 className="text-xl font-bold text-white">{title}</h3>

          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-gray-400">
            This screen is ready for frontend implementation. The layout shell, sidebar, theme, and routing state are already prepared.
          </p>
        </section>
      </div>
    </>
  );
}

export default function HardwareDashboard() {
  const [stats, setStats] = useState<HardwareStats | null>(null);
  const [dashboard, setDashboard] = useState<DashboardSummary>(emptyDashboard);
  const [error, setError] = useState<string | null>(null);
  const [activePage, setActivePage] = useState<PageKey>('home');
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [hardwareUpdatedAt, setHardwareUpdatedAt] = useState<Date | null>(null);
  const [isRefreshingHardware, setIsRefreshingHardware] = useState(false);

  const liveStatus: LiveDataStatus = useMemo(() => {
    if (isRefreshingHardware || (!stats && !error)) {
      return 'syncing';
    }

    if (error) {
      return 'offline';
    }

    if (!hardwareUpdatedAt || Date.now() - hardwareUpdatedAt.getTime() > 30000) {
      return 'stale';
    }

    return 'live';
  }, [error, hardwareUpdatedAt, isRefreshingHardware, stats]);

  const deviceName = stats?.device_name?.trim() || 'Detecting PC';

  const fetchHardware = useCallback(async () => {
    try {
      const response = await axios.get(`${API_BASE_URL}/api/hardware/live`);

      setStats(response.data);
      setHardwareUpdatedAt(new Date());
      setError(null);
    } catch {
      setError('Connection lost. Ensure the C# backend (dotnet run) is running on port 5273.');
    }
  }, []);

  const refreshHardware = useCallback(async () => {
    setIsRefreshingHardware(true);

    try {
      await axios.post(`${API_BASE_URL}/api/hardware/refresh`);
    } catch {
      // Still fetch live hardware data even if cache refresh fails.
    }

    await fetchHardware();
    setIsRefreshingHardware(false);
  }, [fetchHardware]);

  useEffect(() => {
    fetchHardware();

    const interval = window.setInterval(fetchHardware, 1500);

    return () => window.clearInterval(interval);
  }, [fetchHardware]);

  useEffect(() => {
    const fetchDashboard = async () => {
      try {
        const response = await axios.get(`${API_BASE_URL}/api/dashboard/summary`);

        setDashboard(response.data);
      } catch {
        setDashboard(emptyDashboard);
      }
    };

    fetchDashboard();

    const interval = window.setInterval(fetchDashboard, 5000);

    return () => window.clearInterval(interval);
  }, []);

  const handleSetActivePage = (page: PageKey) => {
  if (page === 'diagnosticHistory') {
    setSelectedSessionId(null);
  }

  setActivePage(page);
};

  const renderPage = () => {
    switch (activePage) {
      case 'home':
        return (
          <HomeDashboardView
            stats={stats}
            dashboard={dashboard}
            hardwareUpdatedAt={hardwareUpdatedAt}
            setActivePage={setActivePage}
          />
        );

      case 'systemProfile':
        return (
          <SystemProfileView
            stats={stats}
            error={error}
            hardwareUpdatedAt={hardwareUpdatedAt}
            isRefreshingHardware={isRefreshingHardware}
            onRefreshHardware={refreshHardware}
          />
        );

    case 'diagnosticHistory':
      return selectedSessionId ? (
        <DiagnosticSessionDetailView
          sessionId={selectedSessionId}
          onBack={() => setSelectedSessionId(null)}
        />
      ) : (
        <DiagnosticHistoryView
          onViewSession={(sessionId) => setSelectedSessionId(sessionId)}
        />
      );

      case 'recurringPatterns':
        return <RecurringPatternsView />;

      case 'warningSigns':
        return <WarningSignsView />;

      case 'newDiagnosis':
        return <NewDiagnosisView />; // TARGET SWAP: Safely maps the new modular view file component

      case 'reports':
        return (
          <PlaceholderView
            title="Reports"
            subtitle="Technician-ready diagnostic report output"
          />
        );

      case 'settings':
        return <PlaceholderView title="Settings" subtitle="Application preferences and configuration" />;

      case 'help':
        return <HelpScopeView />;

      default:
        return (
          <HomeDashboardView
            stats={stats}
            dashboard={dashboard}
            hardwareUpdatedAt={hardwareUpdatedAt}
            setActivePage={setActivePage}
          />
        );
    }
  };

  return (
    <div className="flex h-screen overflow-hidden bg-[#0b1017] font-sans text-gray-200">
      <AppSidebar
        activePage={activePage}
        setActivePage={handleSetActivePage}
        dashboard={dashboard}
        liveStatus={liveStatus}
        hardwareUpdatedAt={hardwareUpdatedAt}
      />

      <main className="flex h-full min-w-0 flex-1 flex-col overflow-hidden bg-[#0b1017]">
        <HeaderStatusProvider deviceName={deviceName} liveStatus={liveStatus}>
          {renderPage()}
        </HeaderStatusProvider>
      </main>
    </div>
  );
}
