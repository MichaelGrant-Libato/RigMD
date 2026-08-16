//HardwareDashboard.tsx

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Stethoscope } from 'lucide-react';
import axios from 'axios';
import { AnimatePresence, motion } from 'motion/react';
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  ChevronRight,
  Clock3,
  FileText,
  History,
  RefreshCw,
  Server,
  ShieldAlert,
  Wrench,
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

import {
  buttonTap,
  cardFadeUp,
  cardTransition,
  hoverLift,
  pageFade,
  pageTransition,
} from '../lib/motion';
import type { DashboardSummary, HardwareStats, PageKey } from '../types/rigmd';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ??
  'http://localhost:5273';

interface DashboardMetricCardProps {
  icon: LucideIcon;
  title: string;
  value: string;
  tag: string;
  tagColor: string;
  iconColor: string;
  description: string;
  onViewDetails: () => void;
  index?: number;
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

function isDetected(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') {
    return false;
  }

  return String(value).toLowerCase() !== 'unknown';
}

function getProfileCompletion(stats: HardwareStats | null) {
  const fields = [
    stats?.cpu?.name,
    stats?.ram?.total_gb,
    stats?.disk?.total_gb,
    stats?.os_version,
    stats?.gpu?.driver,
    stats?.chipset_driver,
    stats?.system_age,
  ];

  return Math.round((fields.filter(isDetected).length / fields.length) * 100);
}

function normalizeAction(action: string | undefined | null) {
  const value = (action ?? '').toLowerCase();

  if (value.includes('monitor')) return 'Monitor';
  if (value.includes('maintain')) return 'Maintain';
  if (value.includes('troubleshoot')) return 'Troubleshoot';
  if (value.includes('escalate')) return 'Escalate';

  return '';
}

function getActionColor(action: string | undefined | null) {
  const value = normalizeAction(action).toLowerCase();

  if (value.includes('monitor')) {
    return 'border-cyan-400/45 text-cyan-300 bg-cyan-400/10';
  }

  if (value.includes('maintain')) {
    return 'border-emerald-400/45 text-emerald-300 bg-emerald-400/10';
  }

  if (value.includes('troubleshoot')) {
    return 'border-amber-400/45 text-amber-300 bg-amber-400/10';
  }

  if (value.includes('escalate')) {
    return 'border-red-400/45 text-red-300 bg-red-400/10';
  }

  return 'border-slate-500/45 text-slate-300 bg-slate-500/10';
}

function getActionTextColor(action: string | undefined | null) {
  const value = normalizeAction(action);

  if (value === 'Monitor') return 'text-cyan-300';
  if (value === 'Maintain') return 'text-emerald-300';
  if (value === 'Troubleshoot') return 'text-amber-300';
  if (value === 'Escalate') return 'text-red-300';

  return 'text-cyan-400';
}

function getActionBorderColor(action: string | undefined | null) {
  const value = normalizeAction(action);
  const rawValue = (action ?? '').toLowerCase();

  if (value === 'Monitor') return 'border-cyan-400/24 hover:border-cyan-400/45';
  if (value === 'Maintain') return 'border-emerald-400/24 hover:border-emerald-400/45';
  if (value === 'Troubleshoot') return 'border-amber-400/24 hover:border-amber-400/45';
  if (value === 'Escalate') return 'border-red-400/24 hover:border-red-400/45';
  if (rawValue.includes('dynamic') || rawValue.includes('detected')) return 'border-amber-400/24 hover:border-amber-400/45';
  if (rawValue.includes('review') || rawValue.includes('warning')) return 'border-red-400/24 hover:border-red-400/45';
  if (rawValue.includes('clear')) return 'border-emerald-400/24 hover:border-emerald-400/45';
  if (rawValue.includes('none')) return 'border-slate-700 hover:border-slate-600';

  return 'border-cyan-400/24 hover:border-cyan-400/45';
}

function getConfidenceColor(confidence: string | undefined | null) {
  const value = (confidence ?? '').toLowerCase();

  if (value.includes('high')) return 'border-emerald-400/45 bg-emerald-400/10 text-emerald-300';
  if (value.includes('moderate') || value.includes('medium')) return 'border-amber-400/45 bg-amber-400/10 text-amber-300';
  if (value.includes('low')) return 'border-slate-500/50 bg-slate-500/10 text-slate-400';

  return 'border-cyan-400/45 bg-cyan-400/10 text-cyan-300';
}

function getConfidenceTextColor(confidence: string | undefined | null) {
  const value = (confidence ?? '').toLowerCase();

  if (value.includes('high')) return 'text-emerald-300';
  if (value.includes('moderate') || value.includes('medium')) return 'text-amber-300';
  if (value.includes('low')) return 'text-slate-400';

  return 'text-cyan-400';
}

function getLiveStatusLabel(status: LiveDataStatus) {
  if (status === 'syncing') return 'Syncing';
  if (status === 'offline') return 'Offline';
  if (status === 'stale') return 'Stale';

  return 'Active';
}

function getProfileStatusLabel(profileComplete: number, stats: HardwareStats | null) {
  if (!stats) return 'Waiting';
  if (profileComplete >= 90) return 'Complete';
  if (profileComplete >= 60) return 'Partial';

  return 'Incomplete';
}

function getHealthStatusLabel(action: string, hasSavedSession: boolean) {
  if (!hasSavedSession) return 'No Diagnosis Yet';
  if (action === 'Monitor') return 'Stable';
  if (action === 'Maintain') return 'Needs Attention';
  if (action === 'Troubleshoot') return 'Needs Attention';
  if (action === 'Escalate') return 'Critical Attention';

  return 'No Diagnosis Yet';
}

function getHealthStatusColor(status: string) {
  if (status === 'Stable') return 'text-emerald-300';
  if (status === 'Needs Attention') return 'text-amber-300';
  if (status === 'Critical Attention') return 'text-red-300';

  return 'text-cyan-400';
}

function getRecommendedNextStep(action: string, hasSavedSession: boolean) {
  if (!hasSavedSession) {
    return 'Start a guided diagnosis to identify possible PC issues.';
  }

  if (action === 'Monitor') {
    return 'Continue observing the issue. Run another diagnosis if the symptom becomes frequent or severe.';
  }

  if (action === 'Maintain') {
    return 'Run the recommended maintenance steps from your latest diagnosis. If the same symptom keeps appearing, review recurring issues next.';
  }

  if (action === 'Troubleshoot') {
    return 'Review the latest diagnosis and follow the suggested troubleshooting steps.';
  }

  if (action === 'Escalate') {
    return 'Stop repeated troubleshooting attempts and consider professional inspection.';
  }

  return 'Start a guided diagnosis to identify possible PC issues.';
}

function getWarningDotColor(index: number) {
  if (index === 0) {
    return 'bg-red-400';
  }

  if (index === 1) {
    return 'bg-amber-400';
  }

  return 'bg-amber-300';
}

function QuickActionButton({ icon: Icon, title, description, primary = false, onClick }: QuickActionProps) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileHover={hoverLift}
      whileTap={buttonTap}
      transition={{ duration: 0.2 }}
      className={`group flex w-full items-center justify-between rounded-lg border p-3.5 text-left transition-colors ${
        primary
          ? 'border-cyan-300/35 bg-[#1fb6c9] text-[#041014] hover:bg-[#38c7d7]'
          : 'border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)]/55 text-slate-200 hover:border-cyan-400/30 hover:bg-[var(--rigmd-card-soft)]'
      }`}
    >
      <div className="flex items-center gap-3">
        <div
          className={`rounded-md p-2 ${
            primary ? 'bg-[#041014]/10 text-[#041014]' : 'bg-black/20 text-cyan-300'
          }`}
        >
          <Icon size={20} />
        </div>

        <div>
          <p className={`text-sm font-bold ${primary ? 'text-[#041014]' : 'text-white'}`}>{title}</p>
          <p className={`text-xs leading-relaxed ${primary ? 'text-[#062029]' : 'text-slate-500'}`}>{description}</p>
        </div>
      </div>

      <ChevronRight size={18} className={primary ? 'text-[#041014]' : 'text-gray-500 group-hover:text-cyan-400'} />
    </motion.button>
  );
}

function QuickActionPanel({ setActivePage }: { setActivePage: (page: PageKey) => void }) {
  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={{ duration: 0.28, delay: 0.08 }}
      className="rigmd-side-card rounded-lg border p-4"
    >
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

        <QuickActionButton
          icon={Server}
          title="View System Profile"
          description="Inspect automatically detected hardware details"
          onClick={() => setActivePage('systemProfile')}
        />
      </div>

      <div className="mt-5 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)]/45 p-3.5 text-xs leading-relaxed text-[var(--rigmd-text-faint)]">
        RigMD provides probable diagnostic advisory only. Results do not replace professional hardware inspection.
      </div>
    </motion.section>
  );
}

function HealthDetailRow({
  icon: Icon,
  label,
  value,
  valueClassName = 'text-white',
}: {
  icon: LucideIcon;
  label: string;
  value: string;
  valueClassName?: string;
}) {
  return (
    <div className="grid min-w-0 grid-cols-[minmax(124px,0.8fr)_minmax(0,1fr)] items-center gap-3 border-b border-[var(--rigmd-border-soft)] py-2.5 last:border-b-0">
      <div className="flex min-w-0 items-center gap-3 text-[var(--rigmd-text-muted)]">
        <Icon size={15} className="shrink-0 text-[var(--rigmd-text-faint)]" />
        <span className="truncate text-sm">{label}</span>
      </div>
      <p className={`truncate text-right text-sm font-semibold ${valueClassName}`}>{value}</p>
    </div>
  );
}

function PcHealthSummaryCard({
  stats,
  dashboard,
  hardwareUpdatedAt,
  liveStatus,
  setActivePage,
}: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  liveStatus: LiveDataStatus;
  setActivePage: (page: PageKey) => void;
}) {
  const latestSession = dashboard.last_saved_session ?? dashboard.last_diagnosis ?? dashboard.current_action_status;
  const currentAction = normalizeAction(dashboard.current_action_status?.action_category ?? latestSession?.action_category);
  const hasSavedSession = Boolean(latestSession);
  const profileComplete = getProfileCompletion(stats);
  const healthStatus = getHealthStatusLabel(currentAction, hasSavedSession);
  const latestIssue = latestSession?.diagnosed_category ?? 'No saved diagnosis yet';
  const actionLabel = currentAction || 'No action';
  const confidence = latestSession?.confidence_label ?? 'Not available';
  const profileStatus = getProfileStatusLabel(profileComplete, stats);
  const nextStep = getRecommendedNextStep(currentAction, hasSavedSession);
  const healthStatusColor = getHealthStatusColor(healthStatus);
  const liveStatusColor =
    liveStatus === 'live'
      ? 'text-emerald-300'
      : liveStatus === 'offline'
        ? 'text-red-300'
        : liveStatus === 'stale'
          ? 'text-amber-300'
          : 'text-cyan-300';

  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={cardTransition}
      className="rigmd-card-surface overflow-hidden rounded-lg border"
    >
      <div className="grid gap-5 p-5 sm:p-6 xl:grid-cols-[minmax(0,1fr)_132px]">
        <div className="min-w-0">
          <div className="mb-3 flex items-center gap-2 text-xs font-bold uppercase tracking-[0.16em] text-cyan-400">
            <ShieldAlert size={14} />
            PC Health Summary
          </div>

          <h3 className="text-xl font-bold text-white">
            Status: <span className={healthStatusColor}>{healthStatus}</span>
          </h3>
          <p className="mt-1 max-w-2xl text-sm text-[var(--rigmd-text-muted)]">
            At-a-glance read of your machine&apos;s current condition.
          </p>

          <div className="mt-5 grid grid-cols-1 gap-x-8 md:grid-cols-2">
            <div className="min-w-0">
              <HealthDetailRow icon={Activity} label="Latest Issue" value={latestIssue} />
              <HealthDetailRow icon={Wrench} label="Current Action" value={actionLabel} valueClassName={getActionTextColor(actionLabel)} />
              <HealthDetailRow icon={CheckCircle2} label="Confidence" value={confidence} valueClassName={getConfidenceTextColor(confidence)} />
            </div>

            <div className="min-w-0">
              <HealthDetailRow icon={Server} label="System Profile" value={profileStatus} valueClassName={profileStatus === 'Complete' ? 'text-emerald-300' : 'text-amber-300'} />
              <HealthDetailRow icon={Activity} label="Live Scan" value={getLiveStatusLabel(liveStatus)} valueClassName={liveStatusColor} />
              <HealthDetailRow icon={Clock3} label="Last Scan" value={formatLastUpdated(hardwareUpdatedAt)} />
            </div>
          </div>
        </div>

        <div className="flex flex-col items-center justify-center text-center">
          <div
            className="flex h-24 w-24 items-center justify-center rounded-full p-2"
            style={{
              background: `conic-gradient(#22d3ee ${profileComplete * 3.6}deg, rgba(51, 65, 85, 0.75) 0deg)`,
            }}
          >
            <div className="flex h-full w-full flex-col items-center justify-center rounded-full bg-[var(--rigmd-header)] shadow-[inset_0_1px_0_rgba(255,255,255,0.05)]">
              <span className="text-xl font-bold text-white">{profileComplete}%</span>
              <span className="text-[10px] text-slate-400">Profile</span>
            </div>
          </div>
        </div>
      </div>

      <div className="mx-5 mb-5 rounded-lg border border-[#164e63]/55 bg-[#0b3340]/35 p-5 shadow-[inset_0_1px_0_rgba(248,250,252,0.025)] sm:mx-6 sm:mb-6">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0">
            <div className="mb-2 flex items-center gap-2">
              <CheckCircle2 size={16} className="text-teal-300" />
              <h4 className="font-semibold text-white">Recommended Next Step</h4>
            </div>
            <p className="max-w-3xl text-sm leading-relaxed text-slate-400">{nextStep}</p>
          </div>

          <div className="flex shrink-0 flex-wrap gap-3">
            <motion.button
              type="button"
              onClick={() => setActivePage('newDiagnosis')}
              whileTap={buttonTap}
              className="inline-flex items-center gap-2 rounded-lg bg-[#1fb6c9] px-4 py-2.5 text-sm font-bold text-[#041014] transition hover:bg-[#38c7d7]"
            >
              <Stethoscope size={16} />
              Start New Diagnosis
            </motion.button>

            <motion.button
              type="button"
              onClick={() => setActivePage('systemProfile')}
              whileTap={buttonTap}
              className="inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border-soft)] bg-black/20 px-4 py-2.5 text-sm font-bold text-slate-200 transition hover:border-cyan-400/35 hover:text-cyan-300"
            >
              <Server size={16} />
              View System Profile
            </motion.button>
          </div>
        </div>
      </div>
    </motion.section>
  );
}


function DashboardMetricCard({
  icon: Icon,
  title,
  value,
  tag,
  tagColor,
  iconColor,
  description,
  onViewDetails,
  index = 0,
}: DashboardMetricCardProps) {
  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      whileHover={hoverLift}
      transition={{ duration: 0.22, delay: 0.04 * index }}
      className={`rigmd-card-surface group flex h-full min-h-[218px] min-w-0 flex-col rounded-lg border p-5 transition-colors hover:bg-[var(--rigmd-card-hover)] ${getActionBorderColor(tag)}`}
    >
      <div className="mb-5 flex min-h-[42px] items-start justify-between gap-3">
        <div className="flex h-10 w-10 items-center justify-center rounded-lg border border-[var(--rigmd-border-soft)] bg-black/20">
          <Icon size={20} className={iconColor} />
        </div>
        <span className={`max-w-[140px] truncate rounded-full border px-2.5 py-1 text-[11px] font-bold ${tagColor}`}>
          {tag}
        </span>
      </div>

      <div className="flex flex-1 flex-col">
        <p className="text-xs uppercase tracking-wider text-[var(--rigmd-text-faint)]">{title}</p>
        <h3 className="mt-1 min-h-[32px] text-2xl font-bold text-white">{value}</h3>
        <p className="mt-2 min-h-[46px] text-sm leading-relaxed text-[var(--rigmd-text-muted)]">{description}</p>
      </div>

      <motion.button
        type="button"
        onClick={onViewDetails}
        whileTap={buttonTap}
        className="mt-5 inline-flex items-center gap-1 self-start text-sm font-semibold text-cyan-400 transition hover:text-cyan-300"
      >
        View details
        <ChevronRight size={15} className="transition-transform group-hover:translate-x-0.5" />
      </motion.button>
    </motion.section>
  );
}

function RecentActivityCard({ dashboard }: { dashboard: DashboardSummary }) {
  const maxCount = Math.max(...dashboard.action_distribution.map((item) => item.count), 1);

  const colorMap: Record<string, string> = {
    Monitor: 'bg-cyan-400',
    Maintain: 'bg-emerald-400',
    Troubleshoot: 'bg-amber-400',
    Escalate: 'bg-red-400',
  };

  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={{ duration: 0.25, delay: 0.06 }}
      className="rigmd-card-surface flex h-full min-h-[330px] flex-col rounded-lg border p-5"
    >
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h3 className="font-semibold text-white">Action Category Distribution</h3>
          <p className="text-sm text-gray-500">How your diagnoses have been categorized over time</p>
        </div>

        <p className="text-xs text-gray-500">All-time - {dashboard.totals.total_sessions} sessions</p>
      </div>

      <div className="flex h-52 items-end gap-10 border-b border-[var(--rigmd-border)] px-8 pb-0">
        {dashboard.action_distribution.map((item) => (
          <div key={item.label} className="flex flex-1 flex-col items-center justify-end gap-2">
            <div
              className={`w-full max-w-[76px] rounded-t ${colorMap[item.label] ?? 'bg-cyan-400'}`}
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
            <span className={`h-2.5 w-2.5 rounded-full ${colorMap[item.label] ?? 'bg-cyan-400'}`} />
            <span>
              {item.label} <strong className="text-white">{item.count}</strong>
            </span>
          </div>
        ))}
      </div>
    </motion.section>
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
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={{ duration: 0.25, delay: 0.1 }}
      className="rigmd-card-surface flex h-full min-h-[330px] flex-col rounded-lg border p-5"
    >
      <h3 className="font-semibold text-white">Session Frequency</h3>
      <p className="text-sm text-gray-500">Last 30 days</p>

      <div className="mt-6 h-48 flex-1">
        <svg viewBox="0 0 300 170" className="h-full w-full">
          <path
            d={pathData}
            fill="none"
            stroke="#22d3ee"
            strokeWidth="3"
            strokeLinecap="round"
            strokeLinejoin="round"
          />

          <line x1="10" y1="140" x2="290" y2="140" stroke="var(--rigmd-border)" />
          <line x1="10" y1="100" x2="290" y2="100" stroke="var(--rigmd-card-soft)" strokeDasharray="4 4" />
          <line x1="10" y1="60" x2="290" y2="60" stroke="var(--rigmd-card-soft)" strokeDasharray="4 4" />
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
    </motion.section>
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
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={{ duration: 0.28, delay: 0.14 }}
      className="rigmd-side-card rounded-lg border p-4"
    >
      <div className="mb-4 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <AlertTriangle size={16} className="text-amber-300" />
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
        <div className="space-y-3">
          {dashboard.recent_warning_signs.map((warning, index) => (
            <div key={warning.id} className="flex items-start gap-3 rounded-md border border-transparent py-1">
              <span className={`mt-1.5 h-2 w-2 rounded-full ${getWarningDotColor(index)}`} />

              <div className="min-w-0">
                <p className="text-sm font-semibold text-white">{warning.warning_sign}</p>
                <p className="text-xs text-gray-500">{warning.display_date ?? 'No date available'}</p>
              </div>
            </div>
          ))}
        </div>
      )}
    </motion.section>
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
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={{ duration: 0.28, delay: 0.2 }}
      className="rigmd-side-card rounded-lg border p-4"
    >
      <div className="mb-4 flex items-center gap-2">
        <FileText size={16} className="text-cyan-400" />
        <h3 className="font-semibold text-white">Last Saved Session</h3>
      </div>

      {!session ? (
        <p className="text-sm text-gray-500">No saved diagnostic session yet.</p>
      ) : (
        <div className="space-y-3.5">
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

            <span className={`rounded border px-2 py-1 text-xs font-bold uppercase ${getConfidenceColor(session.confidence_label)}`}>
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
            className="flex w-full items-center justify-between rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)]/50 px-4 py-2.5 text-sm font-semibold text-cyan-300 transition hover:border-cyan-400/35"
          >
            View Full Result
            <ChevronRight size={16} />
          </button>
        </div>
      )}
    </motion.section>
  );
}

function HomeDashboardView({
  stats,
  dashboard,
  hardwareUpdatedAt,
  liveStatus,
  setActivePage,
  onViewSession,
}: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  hardwareUpdatedAt: Date | null;
  liveStatus: LiveDataStatus;
  setActivePage: (page: PageKey) => void;
  onViewSession: (sessionId: string) => void;
}) {
  const lastDiagnosis = dashboard.last_diagnosis;
  const currentAction = dashboard.current_action_status;
  const latestSession = dashboard.last_saved_session ?? dashboard.last_diagnosis ?? dashboard.current_action_status;

  const openLatestSessionOrHistory = () => {
    if (latestSession?.session_id) {
      onViewSession(latestSession.session_id);
      return;
    }

    setActivePage('diagnosticHistory');
  };

  return (
    <>
      <TopHeader title="Home Dashboard" subtitle={`System overview and diagnostic status - ${formatTodayLabel()}`} />

      <motion.div
        key="home-dashboard"
        variants={pageFade}
        initial="hidden"
        animate="visible"
        transition={cardTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-4 py-5 sm:px-6 lg:px-8"
      >
        {dashboard.database_warning && (
          <div className="mx-auto mb-5 max-w-[1500px] rounded-lg border border-amber-400/30 bg-amber-400/10 px-4 py-3 text-sm text-amber-200">
            Database dashboard data is not available yet. Check your Supabase connection and tables.
          </div>
        )}

        <div className="mx-auto grid w-full max-w-[1500px] grid-cols-1 gap-5 xl:grid-cols-[minmax(0,1fr)_348px] 2xl:grid-cols-[minmax(0,1fr)_372px]">
          <div className="min-w-0 space-y-5">
            <PcHealthSummaryCard
              stats={stats}
              dashboard={dashboard}
              hardwareUpdatedAt={hardwareUpdatedAt}
              liveStatus={liveStatus}
              setActivePage={setActivePage}
            />

            <div className="grid w-full grid-cols-1 items-stretch gap-4 lg:grid-cols-2 2xl:grid-cols-4">
              <DashboardMetricCard
                icon={Clock3}
                title="Last Diagnosis"
                value={
                  lastDiagnosis?.days_ago !== null && lastDiagnosis?.days_ago !== undefined
                    ? `${lastDiagnosis.days_ago} days ago`
                    : 'No sessions'
                }
                tag={lastDiagnosis?.action_category ?? 'None'}
                tagColor={getActionColor(lastDiagnosis?.action_category)}
                iconColor="text-cyan-400"
                description={
                  lastDiagnosis
                    ? `${lastDiagnosis.symptom_type} -> ${lastDiagnosis.diagnosed_category}`
                    : 'Complete a diagnostic session first'
                }
                onViewDetails={openLatestSessionOrHistory}
                index={0}
              />

              <DashboardMetricCard
                icon={Activity}
                title="Current Action Status"
                value={currentAction?.action_category ?? 'No action'}
                tag={currentAction?.action_category ?? 'None'}
                tagColor={getActionColor(currentAction?.action_category)}
                iconColor="text-emerald-400"
                description={currentAction?.diagnosed_category ?? 'Action status will appear after diagnosis'}
                onViewDetails={openLatestSessionOrHistory}
                index={1}
              />

              <DashboardMetricCard
                icon={RefreshCw}
                title="Recurring Issues"
                value={String(dashboard.recurring_issues_count)}
                tag="Detected Patterns"
                tagColor="border-amber-400/45 text-amber-300 bg-amber-400/10"
                iconColor="text-amber-300"
                description={dashboard.recurring_issues_count > 0 ? 'Recurring symptoms found in saved sessions' : 'No repeated symptoms detected'}
                onViewDetails={() => setActivePage('recurringPatterns')}
                index={2}
              />

              <DashboardMetricCard
                icon={ShieldAlert}
                title="Warning Signs Active"
                value={String(dashboard.warning_signs_active_count)}
                tag={dashboard.warning_signs_active_count > 0 ? 'Recorded Warnings' : 'Clear'}
                tagColor={
                  dashboard.warning_signs_active_count > 0
                    ? 'border-red-400/45 text-red-300 bg-red-400/10'
                    : 'border-emerald-400/45 text-emerald-300 bg-emerald-400/10'
                }
                iconColor={dashboard.warning_signs_active_count > 0 ? 'text-red-300' : 'text-emerald-300'}
                description={dashboard.warning_signs_active_count > 0 ? 'Review required' : 'No warning signs recorded'}
                onViewDetails={() => setActivePage('warningSigns')}
                index={3}
              />
            </div>

            <motion.section
              variants={cardFadeUp}
              initial="hidden"
              animate="visible"
              transition={{ ...cardTransition, delay: 0.12 }}
              className="pt-3"
            >
              <div className="mb-3 flex items-end justify-between gap-4">
                <div>
                  <h3 className="text-lg font-bold text-white">Diagnostic Trends</h3>
                  <p className="text-sm text-slate-500">See what your diagnosis history is saying</p>
                </div>
              </div>

              <div className="grid w-full grid-cols-1 items-stretch gap-5 2xl:grid-cols-[minmax(0,2fr)_minmax(320px,1fr)]">
                <RecentActivityCard dashboard={dashboard} />
                <SessionFrequencyCard dashboard={dashboard} />
              </div>
            </motion.section>
          </div>

          <aside className="min-w-0 space-y-4">
            <QuickActionPanel setActivePage={setActivePage} />
            <ActiveWarningSignsCard dashboard={dashboard} setActivePage={setActivePage} />
            <LastSavedSessionCard dashboard={dashboard} setActivePage={setActivePage} />
          </aside>
        </div>
      </motion.div>
    </>
  );
}

function PlaceholderView({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <>
      <TopHeader title={title} subtitle={subtitle} />

      <div className="custom-scrollbar flex-1 overflow-y-auto p-8">
        <section className="rigmd-glass rounded-2xl border p-8">
          <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-lg border border-cyan-400/25 bg-cyan-400/[0.06] text-cyan-300">
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
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);

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
            liveStatus={liveStatus}
            setActivePage={setActivePage}
            onViewSession={(sessionId) => {
              setSelectedSessionId(sessionId);
              setActivePage('diagnosticHistory');
            }}
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
          selectedSessionId={selectedSessionId}
          onStartNewDiagnosis={() => setActivePage('newDiagnosis')}
        />
      );

      case 'recurringPatterns':
        return <RecurringPatternsView setActivePage={setActivePage} />;

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
            liveStatus={liveStatus}
            setActivePage={setActivePage}
            onViewSession={(sessionId) => {
              setSelectedSessionId(sessionId);
              setActivePage('diagnosticHistory');
            }}
          />
        );
    }
  };

  return (
    <div className="flex h-screen overflow-hidden bg-transparent font-sans text-gray-200">
      <AppSidebar
        activePage={activePage}
        setActivePage={handleSetActivePage}
        dashboard={dashboard}
        liveStatus={liveStatus}
        hardwareUpdatedAt={hardwareUpdatedAt}
        mobileOpen={mobileSidebarOpen}
        onMobileClose={() => setMobileSidebarOpen(false)}
      />

      <main className="rigmd-main-surface flex h-full min-w-0 flex-1 flex-col overflow-hidden">
        <HeaderStatusProvider
          deviceName={deviceName}
          liveStatus={liveStatus}
          onMenuClick={() => setMobileSidebarOpen(true)}
        >
          <AnimatePresence mode="wait">
            <div key={`${activePage}-${selectedSessionId ?? 'list'}`} className="flex min-h-0 flex-1 flex-col overflow-hidden">
              {renderPage()}
            </div>
          </AnimatePresence>
        </HeaderStatusProvider>
      </main>
    </div>
  );
}
