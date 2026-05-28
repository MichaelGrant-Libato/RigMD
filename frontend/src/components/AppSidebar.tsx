import { useEffect, useState } from 'react';
import {
  Activity,
  AlertTriangle,
  FileText,
  HelpCircle,
  History,
  LayoutDashboard,
  Server,
  Settings,
  Stethoscope,
  type LucideIcon,
} from 'lucide-react';

import type { LiveDataStatus } from './TopHeader';
import type { DashboardSummary, PageKey } from '../types/rigmd';

const HISTORY_SEEN_KEY = 'rigmd_seen_history_count';
const RECURRING_SEEN_KEY = 'rigmd_seen_recurring_count';

function readSeenCount(key: string) {
  if (typeof window === 'undefined') return 0;

  const value = Number(window.localStorage.getItem(key) || 0);
  return Number.isFinite(value) ? value : 0;
}

function saveSeenCount(key: string, value: number) {
  if (typeof window === 'undefined') return;

  window.localStorage.setItem(key, String(value));
}

function formatBadge(value: number) {
  if (value > 99) return '99+';
  return value;
}

interface SidebarItemProps {
  icon: LucideIcon;
  label: string;
  active?: boolean;
  badge?: string | number | null;
  alert?: boolean;
  onClick?: () => void;
}

function SidebarItem({
  icon: Icon,
  label,
  active = false,
  badge = null,
  alert = false,
  onClick,
}: SidebarItemProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`mb-1 flex w-full items-center justify-between rounded-xl px-4 py-2.5 text-left transition-colors ${
        active
          ? 'border-l-2 border-cyan-400 bg-[#172232] text-cyan-400'
          : 'text-slate-300 hover:bg-[#172232] hover:text-white'
      }`}
    >
      <div className="flex min-w-0 items-center gap-3">
        <Icon size={18} />
        <span className="truncate text-sm font-medium">{label}</span>
      </div>

      {alert ? (
        <span
          title="Live warning detected"
          className="ml-3 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border border-orange-500/40 bg-orange-500/10 text-orange-400"
        >
          <AlertTriangle size={13} />
        </span>
      ) : badge !== null ? (
        <span className="ml-3 shrink-0 text-xs font-bold text-cyan-400">{badge}</span>
      ) : null}
    </button>
  );
}

interface AppSidebarProps {
  activePage: PageKey;
  setActivePage: (page: PageKey) => void;
  dashboard: DashboardSummary;
  liveWarningActive?: boolean;
  liveStatus?: LiveDataStatus;
  hardwareUpdatedAt?: Date | null;
}

function getStatusCopy(status: LiveDataStatus) {
  if (status === 'syncing') {
    return {
      title: 'System Status',
      label: 'Syncing Hardware',
      dotClassName: 'bg-cyan-400 animate-pulse',
      textClassName: 'text-cyan-400',
    };
  }

  if (status === 'offline') {
    return {
      title: 'System Status',
      label: 'Offline',
      dotClassName: 'bg-red-400',
      textClassName: 'text-red-400',
    };
  }

  if (status === 'stale') {
    return {
      title: 'System Status',
      label: 'Stale Data',
      dotClassName: 'bg-orange-400',
      textClassName: 'text-orange-400',
    };
  }

  return {
    title: 'System Status',
    label: 'Live Scan Active',
    dotClassName: 'bg-emerald-400',
    textClassName: 'text-emerald-400',
  };
}

function formatRelativeUpdate(value: Date | null | undefined) {
  if (!value) {
    return 'Waiting for first scan';
  }

  const seconds = Math.max(0, Math.floor((Date.now() - value.getTime()) / 1000));

  if (seconds < 5) return 'Updated just now';
  if (seconds < 60) return `Updated ${seconds}s ago`;

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `Updated ${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  return `Updated ${hours}h ago`;
}

export default function AppSidebar({
  activePage,
  setActivePage,
  dashboard,
  liveWarningActive,
  liveStatus = 'syncing',
  hardwareUpdatedAt,
}: AppSidebarProps) {
  const totalSessions = dashboard.totals?.total_sessions ?? 0;
  const recurringIssues = dashboard.recurring_issues_count ?? 0;

  const [seenHistoryCount, setSeenHistoryCount] = useState(() => readSeenCount(HISTORY_SEEN_KEY));
  const [seenRecurringCount, setSeenRecurringCount] = useState(() => readSeenCount(RECURRING_SEEN_KEY));

  const historyBadgeCount = Math.max(totalSessions - seenHistoryCount, 0);
  const recurringBadgeCount = Math.max(recurringIssues - seenRecurringCount, 0);

  const warningAlert = liveWarningActive ?? (dashboard.warning_signs_active_count ?? 0) > 0;
  const statusCopy = getStatusCopy(liveStatus);

  useEffect(() => {
    if (activePage === 'diagnosticHistory') {
      setSeenHistoryCount(totalSessions);
      saveSeenCount(HISTORY_SEEN_KEY, totalSessions);
    }
  }, [activePage, totalSessions]);

  useEffect(() => {
    if (activePage === 'recurringPatterns') {
      setSeenRecurringCount(recurringIssues);
      saveSeenCount(RECURRING_SEEN_KEY, recurringIssues);
    }
  }, [activePage, recurringIssues]);

  return (
    <aside className="hidden w-64 shrink-0 flex-col border-r border-[#253041] bg-[#111827] md:flex">
      <div className="flex items-center gap-3 border-b border-[#253041] px-6 py-5">
        <Activity className="text-cyan-400" size={28} />

        <div>
          <h1 className="text-lg font-bold leading-tight text-white">RigMD</h1>
          <p className="text-[10px] uppercase tracking-wider text-slate-500">Diagnostic Support</p>
        </div>
      </div>

      <div className="custom-scrollbar flex-1 overflow-y-auto px-3 py-5">
        <div className="mb-6">
          <p className="mb-2 px-4 text-[11px] font-bold tracking-wider text-slate-500">OVERVIEW</p>

          <SidebarItem
            icon={LayoutDashboard}
            label="Home"
            active={activePage === 'home'}
            onClick={() => setActivePage('home')}
          />

          <SidebarItem
            icon={Server}
            label="System Profile"
            active={activePage === 'systemProfile'}
            onClick={() => setActivePage('systemProfile')}
          />
        </div>

        <div className="mb-6">
          <p className="mb-2 px-4 text-[11px] font-bold tracking-wider text-slate-500">DIAGNOSTICS</p>

          <SidebarItem
            icon={Stethoscope}
            label="New Diagnosis"
            active={activePage === 'newDiagnosis'}
            onClick={() => setActivePage('newDiagnosis')}
          />

          <SidebarItem
            icon={History}
            label="Diagnostic History"
            badge={historyBadgeCount > 0 ? formatBadge(historyBadgeCount) : null}
            active={activePage === 'diagnosticHistory'}
            onClick={() => setActivePage('diagnosticHistory')}
          />

          <SidebarItem
            icon={Activity}
            label="Recurring Patterns"
            badge={recurringBadgeCount > 0 ? formatBadge(recurringBadgeCount) : null}
            active={activePage === 'recurringPatterns'}
            onClick={() => setActivePage('recurringPatterns')}
          />

          <SidebarItem
            icon={AlertTriangle}
            label="Warning Signs"
            alert={warningAlert}
            active={activePage === 'warningSigns'}
            onClick={() => setActivePage('warningSigns')}
          />
        </div>

        <div className="mb-6">
          <p className="mb-2 px-4 text-[11px] font-bold tracking-wider text-slate-500">DATA</p>

          <SidebarItem
            icon={FileText}
            label="Reports"
            active={activePage === 'reports'}
            onClick={() => setActivePage('reports')}
          />
        </div>

        <div className="mb-6">
          <p className="mb-2 px-4 text-[11px] font-bold tracking-wider text-slate-500">SYSTEM</p>

          <SidebarItem
            icon={Settings}
            label="Settings"
            active={activePage === 'settings'}
            onClick={() => setActivePage('settings')}
          />

          <SidebarItem
            icon={HelpCircle}
            label="Help / Scope"
            active={activePage === 'help'}
            onClick={() => setActivePage('help')}
          />
        </div>
      </div>

      <div className="m-4 rounded-xl border border-[#253041] bg-[#1b2738] p-4">
        <div className="mb-2 flex items-center gap-2 text-[11px] uppercase tracking-wider text-slate-500">
          <Activity size={14} className={statusCopy.textClassName} />
          {statusCopy.title}
        </div>

        <div className={`flex items-center gap-2 text-sm font-semibold ${statusCopy.textClassName}`}>
          <span className={`h-2 w-2 rounded-full ${statusCopy.dotClassName}`} />
          {statusCopy.label}
        </div>

        <p className="mt-2 text-xs text-slate-500">{formatRelativeUpdate(hardwareUpdatedAt)}</p>
      </div>
    </aside>
  );
}
