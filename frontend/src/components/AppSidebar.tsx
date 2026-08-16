import { useEffect, useState } from 'react';
import {
  Activity,
  AlertTriangle,
  FileText,
  HelpCircle,
  History,
  LayoutDashboard,
  Menu,
  Server,
  Settings,
  Stethoscope,
  X,
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
  collapsed?: boolean;
  onClick?: () => void;
}

function SidebarItem({
  icon: Icon,
  label,
  active = false,
  badge = null,
  alert = false,
  collapsed = false,
  onClick,
}: SidebarItemProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={collapsed ? label : undefined}
      className={`relative mb-1 flex w-full items-center transition-colors ${
        collapsed ? 'h-10 justify-center px-0' : 'justify-between px-4 py-2.5 text-left'
      } ${
        active
          ? 'bg-cyan-400/10 text-cyan-200 shadow-[inset_3px_0_0_rgba(34,211,238,0.72)]'
          : 'text-slate-300 hover:bg-cyan-400/[0.045] hover:text-white'
      }`}
    >
      <div className={`flex min-w-0 items-center ${collapsed ? 'justify-center' : 'gap-3'}`}>
        <Icon size={18} className="shrink-0" />
        {!collapsed && <span className="truncate text-sm font-medium">{label}</span>}
      </div>

      {collapsed && (alert || badge !== null) ? (
        <span className="absolute right-2 top-2 h-2 w-2 rounded-full bg-amber-400" />
      ) : alert ? (
        <span
          title="Live warning detected"
          className="ml-3 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border border-amber-400/35 bg-amber-400/10 text-amber-300"
        >
          <AlertTriangle size={13} />
        </span>
      ) : badge !== null ? (
        <span className="ml-3 shrink-0 text-xs font-bold text-cyan-300">{badge}</span>
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
  mobileOpen?: boolean;
  onMobileClose?: () => void;
}

function getStatusCopy(status: LiveDataStatus) {
  if (status === 'syncing') {
    return {
      title: 'System Status',
      label: 'Syncing Hardware',
      dotClassName: 'bg-cyan-400 animate-pulse',
      textClassName: 'text-cyan-300',
    };
  }

  if (status === 'offline') {
    return {
      title: 'System Status',
      label: 'Offline',
      dotClassName: 'bg-red-400',
      textClassName: 'text-red-300',
    };
  }

  if (status === 'stale') {
    return {
      title: 'System Status',
      label: 'Stale Data',
      dotClassName: 'bg-amber-400',
      textClassName: 'text-amber-300',
    };
  }

  return {
    title: 'System Status',
    label: 'Live Scan Active',
      dotClassName: 'bg-emerald-400',
      textClassName: 'text-emerald-300',
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
  mobileOpen = false,
  onMobileClose,
}: AppSidebarProps) {
  const totalSessions = dashboard.totals?.total_sessions ?? 0;
  const recurringIssues = dashboard.recurring_issues_count ?? 0;

  const [seenHistoryCount, setSeenHistoryCount] = useState(() => readSeenCount(HISTORY_SEEN_KEY));
  const [seenRecurringCount, setSeenRecurringCount] = useState(() => readSeenCount(RECURRING_SEEN_KEY));
  const [desktopCollapsed, setDesktopCollapsed] = useState(false);

  const historyBadgeCount = Math.max(totalSessions - seenHistoryCount, 0);
  const recurringBadgeCount = Math.max(recurringIssues - seenRecurringCount, 0);

  const warningAlert = liveWarningActive ?? (dashboard.warning_signs_active_count ?? 0) > 0;
  const statusCopy = getStatusCopy(liveStatus);
  const navigate = (page: PageKey) => {
    setActivePage(page);
    onMobileClose?.();
  };

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

  useEffect(() => {
    if (!mobileOpen) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onMobileClose?.();
      }
    };

    window.addEventListener('keydown', handleKeyDown);

    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [mobileOpen, onMobileClose]);

  const renderSectionLabel = (label: string, collapsed: boolean) =>
    collapsed ? (
      <div className="mx-2 my-4 border-t border-white/15" aria-hidden="true" />
    ) : (
      <p className="mb-2 overflow-hidden px-4 text-[11px] font-bold tracking-wider text-cyan-300/80">{label}</p>
    );

  const renderContent = (collapsed = false, isMobile = false) => (
    <>
      <div className={`border-b border-white/10 ${collapsed ? 'px-0 py-2' : 'px-4 py-4'}`}>
        <div className={`flex items-center ${collapsed ? 'flex-col gap-3' : 'justify-between gap-3'}`}>
          <div className={`flex min-w-0 items-center ${collapsed ? 'justify-center' : 'gap-3'}`}>
            <div className="flex h-9 w-9 shrink-0 items-center justify-center border border-cyan-400/22 bg-cyan-400/10 text-cyan-200">
              <Activity size={22} />
            </div>

            {!collapsed && (
              <div className="overflow-hidden">
                <h1 className="text-lg font-bold leading-tight text-white">RigMD</h1>
                <p className="text-[10px] uppercase tracking-wider text-slate-500">Diagnostic Support</p>
              </div>
            )}
          </div>

          {isMobile ? (
            <button
              type="button"
              onClick={onMobileClose}
              className="flex h-9 w-9 items-center justify-center border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] text-slate-400 transition hover:border-cyan-400/35 hover:text-white md:hidden"
              aria-label="Close navigation menu"
            >
              <X size={18} />
            </button>
          ) : (
            <button
              type="button"
              onClick={() => setDesktopCollapsed((value) => !value)}
              className="flex h-9 w-9 items-center justify-center text-slate-300 transition hover:bg-white/[0.055] hover:text-white"
              aria-label={desktopCollapsed ? 'Expand navigation menu' : 'Collapse navigation menu'}
              title={desktopCollapsed ? 'Expand navigation menu' : 'Collapse navigation menu'}
            >
              <Menu size={19} />
            </button>
          )}
        </div>
      </div>

      <div className={`custom-scrollbar flex-1 overflow-y-auto py-4 ${collapsed ? 'px-0' : 'px-3'}`}>
        <div className="mb-6">
          {renderSectionLabel('OVERVIEW', collapsed)}

          <SidebarItem
            icon={LayoutDashboard}
            label="Home"
            collapsed={collapsed}
            active={activePage === 'home'}
            onClick={() => navigate('home')}
          />
        </div>

        <div className="mb-6">
          {renderSectionLabel('DIAGNOSTICS', collapsed)}

          <SidebarItem
            icon={Server}
            label="System Profile"
            collapsed={collapsed}
            active={activePage === 'systemProfile'}
            onClick={() => navigate('systemProfile')}
          />

          <SidebarItem
            icon={Stethoscope}
            label="New Diagnosis"
            collapsed={collapsed}
            active={activePage === 'newDiagnosis'}
            onClick={() => navigate('newDiagnosis')}
          />

          <SidebarItem
            icon={History}
            label="Diagnostic History"
            badge={historyBadgeCount > 0 ? formatBadge(historyBadgeCount) : null}
            collapsed={collapsed}
            active={activePage === 'diagnosticHistory'}
            onClick={() => navigate('diagnosticHistory')}
          />

          <SidebarItem
            icon={Activity}
            label="Recurring Patterns"
            badge={recurringBadgeCount > 0 ? formatBadge(recurringBadgeCount) : null}
            collapsed={collapsed}
            active={activePage === 'recurringPatterns'}
            onClick={() => navigate('recurringPatterns')}
          />

          <SidebarItem
            icon={AlertTriangle}
            label="Warning Signs"
            alert={warningAlert}
            collapsed={collapsed}
            active={activePage === 'warningSigns'}
            onClick={() => navigate('warningSigns')}
          />
        </div>

        <div className="mb-6">
          {renderSectionLabel('DATA', collapsed)}

          <SidebarItem
            icon={FileText}
            label="Reports"
            collapsed={collapsed}
            active={activePage === 'reports'}
            onClick={() => navigate('reports')}
          />
        </div>

        <div className="mb-6">
          {renderSectionLabel('SYSTEM', collapsed)}

          <SidebarItem
            icon={Settings}
            label="Settings"
            collapsed={collapsed}
            active={activePage === 'settings'}
            onClick={() => navigate('settings')}
          />

          <SidebarItem
            icon={HelpCircle}
            label="Help / Scope"
            collapsed={collapsed}
            active={activePage === 'help'}
            onClick={() => navigate('help')}
          />
        </div>
      </div>

      <div className={`border-t border-white/10 ${collapsed ? 'mx-2 flex justify-center py-4' : 'm-4 border border-[var(--rigmd-border-soft)] bg-black/10 p-3'}`}>
        {collapsed ? (
          <span
            title={`${statusCopy.label} - ${formatRelativeUpdate(hardwareUpdatedAt)}`}
            className={`h-2.5 w-2.5 rounded-full ${statusCopy.dotClassName}`}
          />
        ) : (
          <>
            <div className="mb-2 flex items-center gap-2 overflow-hidden text-[11px] uppercase tracking-wider text-slate-500">
              <Activity size={14} className={statusCopy.textClassName} />
              {statusCopy.title}
            </div>

            <div className={`flex items-center gap-2 text-sm font-semibold ${statusCopy.textClassName}`}>
              <span className={`h-2 w-2 rounded-full ${statusCopy.dotClassName}`} />
              <span className="overflow-hidden">{statusCopy.label}</span>
            </div>

            <p className="mt-2 overflow-hidden text-xs text-slate-500">{formatRelativeUpdate(hardwareUpdatedAt)}</p>
          </>
        )}
      </div>
    </>
  );

  return (
    <>
      <aside
        className={`rigmd-sidebar-surface hidden shrink-0 flex-col overflow-hidden border-r transition-[width] duration-300 ease-out md:flex ${
          desktopCollapsed ? 'w-10' : 'w-64'
        }`}
      >
        {renderContent(desktopCollapsed)}
      </aside>

      <div
        className={`fixed inset-0 z-40 bg-black/55 backdrop-blur-sm transition-opacity duration-300 md:hidden ${
          mobileOpen ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0'
        }`}
        onClick={onMobileClose}
        aria-hidden="true"
      />

      <aside
        className={`rigmd-sidebar-surface fixed inset-y-0 left-0 z-50 flex w-[min(18rem,86vw)] flex-col border-r shadow-2xl transition-transform duration-300 ease-out md:hidden ${
          mobileOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        {renderContent(false, true)}
      </aside>
    </>
  );
}
