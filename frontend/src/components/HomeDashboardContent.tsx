import { AlertTriangle, CheckCircle2, ChevronRight, Clock3, HardDrive, History, MemoryStick, RefreshCw, Stethoscope, Wifi } from 'lucide-react';
import type { DashboardSummary, HardwareStats, PageKey } from '../types/rigmd';

interface Props {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
  onViewSession: (sessionId: string) => void;
}

function recommendedStep(action: string | undefined) {
  switch (action?.toLowerCase()) {
    case 'monitor':
      return 'Nothing needs to be changed right now. Run another check if the problem happens again or gets worse.';
    case 'maintain':
      return 'Review the latest result and follow the simple care steps RigMD recommends.';
    case 'troubleshoot':
      return 'Review the latest result before trying any safe fix.';
    case 'escalate':
      return 'Avoid repeated fixes for now. Consider asking a technician to inspect the Device.';
    default:
      return 'Open your latest result to review the available recommendations.';
  }
}

function healthStatus(value: number | null | undefined) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return { value: 'Not available', label: 'Needs a refresh', className: 'text-slate-400' };
  }

  const roundedValue = Math.round(value);
  if (roundedValue >= 90) return { value: `${roundedValue}% used`, label: 'Needs attention', className: 'text-red-300' };
  if (roundedValue >= 75) return { value: `${roundedValue}% used`, label: 'Keep an eye on it', className: 'text-amber-300' };
  return { value: `${roundedValue}% used`, label: 'Working normally', className: 'text-emerald-300' };
}

function HealthItem({
  icon: Icon,
  label,
  value,
  status,
}: {
  icon: typeof MemoryStick;
  label: string;
  value: string;
  status: string;
}) {
  return (
    <div className="rigmd-card-surface flex min-h-[144px] min-w-0 items-center gap-6 rounded-lg border px-7 py-6">
      <Icon size={30} strokeWidth={1.8} className="shrink-0 text-cyan-300" />
      <div className="min-w-0">
        <p className="text-base font-semibold uppercase tracking-[0.14em] text-slate-400">{label}</p>
        <p className="truncate text-lg font-semibold leading-tight text-white">{value}</p>
        <p className="mt-1 text-base text-slate-400">{status}</p>
      </div>
    </div>
  );
}

export default function HomeDashboardContent({ stats, dashboard, setActivePage, onViewSession }: Props) {
  const latest = dashboard.last_saved_session ?? dashboard.last_diagnosis ?? dashboard.current_action_status;
  const hasWarnings = dashboard.warning_signs_active_count > 0;
  const hasPatterns = dashboard.recurring_issues_count > 0;
  const unavailable = Boolean(dashboard.database_warning);
  const checkedAt = latest?.created_at ? new Date(latest.created_at) : null;
  const action = latest?.action_category?.toLowerCase();
  const title = unavailable
    ? 'Your check history is unavailable'
    : hasWarnings
      ? 'You have warning signs to review'
      : latest
        ? action === 'escalate' ? 'Your latest check recommends professional inspection'
          : action === 'maintain' || action === 'troubleshoot' ? 'Your latest check has steps to follow'
            : 'Your latest check is ready to review'
        : 'Ready for your first Device check';
  const linkClass = 'inline-flex items-center gap-2 rounded text-sm font-semibold text-cyan-300 hover:text-cyan-200 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-cyan-300';
  const memoryHealth = healthStatus(stats?.ram?.usage_percent);
  const storageHealth = healthStatus(stats?.disk?.usage_percent);
  const networkHealth = stats?.network
    ? { value: stats.network.is_wifi ? 'Wi-Fi connected' : 'Ethernet connected', label: 'Working normally' }
    : { value: 'Not available', label: 'Needs a refresh' };
  const lastCheckValue = checkedAt && !Number.isNaN(checkedAt.getTime())
    ? checkedAt.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
    : latest?.display_date || 'Not available';
  const lastCheckStatus = checkedAt && !Number.isNaN(checkedAt.getTime())
    ? checkedAt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
    : 'Run a check to update';

  return (
    <div className="mx-auto w-full max-w-[1500px] space-y-5">
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)]">
        <section className={`rigmd-card-surface rounded-lg border p-6 ${hasWarnings ? 'border-amber-400/50' : ''}`}>
          <p className="text-xs font-bold uppercase tracking-[0.16em] text-cyan-400">Device Checkup</p>
          <h2 className="max-w-3xl text-2xl font-bold text-white">{title}</h2>
          <p className="mt-3 max-w-2xl text-sm leading-relaxed text-slate-300">
            {unavailable ? 'Saved results could not be loaded. Try again shortly.'
              : hasWarnings ? 'Review the recorded warnings before continuing with troubleshooting.'
                : latest ? recommendedStep(latest.action_category)
                  : 'Answer a few questions to investigate something that feels slow, noisy, or unusual. Run a check for recommendations.'}
          </p>
          <div className="mt-5 flex flex-wrap items-center gap-5">
            <button type="button"
              onClick={() => hasWarnings ? setActivePage('warningSigns') : latest ? onViewSession(latest.session_id) : setActivePage('newDiagnosis')}
              className="inline-flex items-center gap-2 rounded-lg bg-[#1fb6c9] px-5 py-3 text-sm font-bold text-[#041014] transition hover:bg-[#38c7d7] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-cyan-300">
              {hasWarnings ? <AlertTriangle size={18} /> : latest ? <History size={18} /> : <Stethoscope size={18} />}
              {hasWarnings ? 'Review Alerts' : latest ? 'View Latest Result' : 'Check My Device'}
            </button>
            {(latest || hasWarnings) && <button type="button" onClick={() => setActivePage('newDiagnosis')} className={linkClass}>Check My Device</button>}
          </div>
        </section>

        {unavailable ? (
          <section className="rigmd-card-surface flex items-center rounded-lg border p-5">
            <p className="text-sm text-slate-300">Warnings and repeated checks are unavailable right now.</p>
          </section>
        ) : (
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-1">
            <div className={`rigmd-card-surface flex min-w-0 items-center justify-between gap-3 rounded-lg border p-5 ${hasWarnings ? 'border-amber-400/50' : ''}`}>
              <div className="flex min-w-0 items-center gap-3">
                <CheckCircle2 size={20} className={hasWarnings ? 'shrink-0 text-amber-300' : 'shrink-0 text-emerald-300'} />
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-white">Warning signs</p>
                  <p className="truncate text-xs text-slate-400">{hasWarnings ? `${dashboard.warning_signs_active_count} recorded` : latest ? 'None found in the latest check' : 'Not checked yet'}</p>
                </div>
              </div>
              {hasWarnings && <button type="button" onClick={() => setActivePage('warningSigns')} className={`${linkClass} shrink-0 text-xs`}>Review <ChevronRight size={14} /></button>}
            </div>
            <div className="rigmd-card-surface flex min-w-0 items-center justify-between gap-3 rounded-lg border p-5">
              <div className="flex min-w-0 items-center gap-3">
                <RefreshCw size={20} className={hasPatterns ? 'shrink-0 text-cyan-300' : 'shrink-0 text-slate-400'} />
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-white">Repeated checks</p>
                  <p className="truncate text-xs text-slate-400">{hasPatterns ? `${dashboard.recurring_issues_count} repeated check pattern${dashboard.recurring_issues_count === 1 ? '' : 's'} found` : 'None found yet'}</p>
                </div>
              </div>
              {hasPatterns && <button type="button" onClick={() => setActivePage('recurringPatterns')} className={`${linkClass} shrink-0 text-xs`}>View <ChevronRight size={14} /></button>}
            </div>
          </section>
        )}
      </div>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <HealthItem icon={MemoryStick} label="Memory" value={memoryHealth.value} status={memoryHealth.label} />
        <HealthItem icon={HardDrive} label="Storage" value={storageHealth.value} status={storageHealth.label} />
        <HealthItem icon={Wifi} label="Network" value={networkHealth.value} status={networkHealth.label} />
        <HealthItem icon={Clock3} label="Last check" value={lastCheckValue} status={lastCheckStatus} />
      </section>

    </div>
  );
}
