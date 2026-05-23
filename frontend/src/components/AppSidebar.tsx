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

import type { DashboardSummary, PageKey } from '../types/rigmd';

interface SidebarItemProps {
  icon: LucideIcon;
  label: string;
  active?: boolean;
  badge?: string | number | null;
  onClick?: () => void;
}

function SidebarItem({ icon: Icon, label, active = false, badge = null, onClick }: SidebarItemProps) {
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
      <div className="flex items-center gap-3">
        <Icon size={18} />
        <span className="text-sm font-medium">{label}</span>
      </div>

      {badge !== null && <span className="text-xs font-bold text-cyan-400">{badge}</span>}
    </button>
  );
}

interface AppSidebarProps {
  activePage: PageKey;
  setActivePage: (page: PageKey) => void;
  dashboard: DashboardSummary;
}

export default function AppSidebar({ activePage, setActivePage, dashboard }: AppSidebarProps) {
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
            badge={dashboard.totals.total_sessions || null}
            active={activePage === 'diagnosticHistory'}
            onClick={() => setActivePage('diagnosticHistory')}
          />

          <SidebarItem
            icon={Activity}
            label="Recurring Patterns"
            badge={dashboard.recurring_issues_count || null}
            active={activePage === 'recurringPatterns'}
            onClick={() => setActivePage('recurringPatterns')}
          />

          <SidebarItem
            icon={AlertTriangle}
            label="Warning Signs"
            badge={dashboard.warning_signs_active_count || null}
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
          <Activity size={14} className="text-emerald-400" />
          System Status
        </div>

        <div className="flex items-center gap-2 text-sm font-semibold text-emerald-400">
          <span className="h-2 w-2 rounded-full bg-emerald-400" />
          Profile Active
        </div>
      </div>
    </aside>
  );
}