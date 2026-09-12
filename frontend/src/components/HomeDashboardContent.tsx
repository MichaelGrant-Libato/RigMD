import { AlertTriangle, ChevronRight, Clock3, History, Server, Stethoscope } from 'lucide-react';
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
      return 'Avoid repeated fixes for now. Consider asking a technician to inspect the PC.';
    default:
      return 'Open your latest result to review the available recommendations.';
  }
}

function friendlyActionLabel(action: string | undefined) {
  switch (action?.toLowerCase()) {
    case 'monitor':
      return 'Keep an eye on it';
    case 'maintain':
      return 'Do simple care';
    case 'troubleshoot':
      return 'Try a safe fix';
    case 'escalate':
      return 'Get help';
    default:
      return 'See full result';
  }
}

function friendlyResultLabel(result: string | undefined) {
  if (!result) return 'Result unavailable';
  if (result.toLowerCase() === 'no active issue detected') return 'No active problem found';
  return result;
}

function friendlyStatusLabel(status: string | undefined) {
  if (!status) return null;

  const normalized = status.replaceAll('_', ' ').toLowerCase();
  if (normalized === 'open') return 'Saved for review';
  if (normalized === 'resolved') return 'Marked fixed';
  if (normalized === 'needs recheck') return 'Needs another check';

  return normalized.replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export default function HomeDashboardContent({ stats, dashboard, setActivePage, onViewSession }: Props) {
  const latest = dashboard.last_saved_session ?? dashboard.last_diagnosis ?? dashboard.current_action_status;
  const hasWarnings = dashboard.warning_signs_active_count > 0;
  const hasPatterns = dashboard.recurring_issues_count > 0;
  const unavailable = Boolean(dashboard.database_warning);
  const checkedAt = latest?.created_at ? new Date(latest.created_at) : null;
  const checkHistoryLabel = unavailable
    ? 'Check history unavailable'
    : !latest
      ? 'No check completed yet'
      : checkedAt && !Number.isNaN(checkedAt.getTime())
        ? `Last checked: ${checkedAt.toLocaleString(undefined, {
            month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit',
          })}`
        : latest.display_date
          ? `Last checked: ${latest.display_date}`
          : 'Last check date unavailable';
  const action = latest?.action_category?.toLowerCase();
  const title = unavailable
    ? 'Your check history is unavailable'
    : hasWarnings
      ? 'You have warning signs to review'
      : latest
        ? action === 'escalate' ? 'Your latest check recommends professional inspection'
          : action === 'maintain' || action === 'troubleshoot' ? 'Your latest check has steps to follow'
            : 'Your latest check is ready to review'
        : 'Ready for your first PC check';
  const linkClass = 'inline-flex items-center gap-2 rounded text-sm font-semibold text-cyan-300 hover:text-cyan-200 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-cyan-300';
  const hardwareDetails = [
    ['Processor', stats?.cpu?.name?.replace(/\s+\d+-Core Processor$/i, '').trim()],
    ['Memory', stats?.ram?.total_gb ? `${stats.ram.total_gb} GB installed` : null],
    ['Graphics', stats?.gpu?.name],
    ['Operating system', stats?.os_version?.replace(/^Microsoft\s+/i, '').replace(/\s*\([\d.]+\)$/, '').trim()],
  ];

  return (
    <div className="mx-auto w-full max-w-[1500px] space-y-5">
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1.8fr)_minmax(0,1fr)]">
        <section className={`rigmd-card-surface flex flex-col rounded-lg border p-6 ${hasWarnings ? 'border-amber-400/50' : ''}`}>
          <div className="mb-4 flex flex-col items-start gap-2 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between sm:gap-3">
            <p className="text-xs font-bold uppercase tracking-[0.16em] text-cyan-400">PC Checkup</p>
            <span className="inline-flex min-w-0 items-center gap-2 text-sm leading-5 text-slate-300">
              <Clock3 size={14} className="shrink-0" />
              <span>{checkHistoryLabel}</span>
            </span>
          </div>
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
              {hasWarnings ? 'Review Alerts' : latest ? 'View Latest Result' : 'Check My PC'}
            </button>
            {(latest || hasWarnings) && <button type="button" onClick={() => setActivePage('newDiagnosis')} className={linkClass}>Check My PC</button>}
            <button type="button" onClick={() => setActivePage('systemProfile')} className={linkClass}>View PC Info <ChevronRight size={16} /></button>
          </div>
        </section>
        <section className="rigmd-card-surface flex min-w-0 flex-col rounded-lg border p-6">
          <h3 className="flex items-center gap-2 font-semibold text-white"><Server size={18} className="text-cyan-400" /> Your PC at a Glance</h3>
          <p className="mt-2 text-sm text-slate-300">The main PC details RigMD found during the latest scan.</p>
          <dl className="mt-4 grid gap-x-5 gap-y-4 sm:grid-cols-2">
            {hardwareDetails.map(([label, value]) => (
              <div key={label} className="min-w-0">
                <dt className="text-sm text-slate-300">{label}</dt>
                <dd className="mt-1 break-words text-sm font-medium text-slate-200">{value || 'Not available yet'}</dd>
              </div>
            ))}
          </dl>
        </section>
      </div>

      <div className="grid gap-5 md:grid-cols-2">
        <section className="rigmd-card-surface flex min-w-0 flex-col rounded-lg border p-6">
          <h3 className="flex items-center gap-2 font-semibold text-white"><Clock3 size={18} className="text-cyan-400" /> Latest Check</h3>
          {unavailable ? <p className="mt-4 text-sm text-slate-300">Latest check unavailable.</p> : latest ? (
            <div className="my-4 space-y-3">
              <p className="text-sm text-slate-300">{latest.display_date ?? 'Check date unavailable'}</p>
              <p className="break-words text-lg font-semibold text-white">{latest.symptom_type}</p>
              <p className="break-words text-sm text-slate-300"><span className="text-slate-300">Result: </span>{friendlyResultLabel(latest.diagnosed_category)}</p>
              <p className="text-sm text-slate-300"><span className="text-slate-300">What to do: </span>{friendlyActionLabel(latest.action_category)}</p>
              {friendlyStatusLabel(latest.resolution_status) && <p className="text-sm text-slate-300">Check status: {friendlyStatusLabel(latest.resolution_status)}</p>}
            </div>
          ) : <p className="mt-4 text-sm leading-relaxed text-slate-300">No checks completed yet. Your results will appear here.</p>}
          {!latest && !unavailable && (
            <div className="mt-4 mb-4 border-t border-slate-700/50 pt-4">
              <p className="text-sm font-medium text-slate-200">What your check will include</p>
              <p className="mt-2 text-sm leading-relaxed text-slate-300">A summary of your symptoms, a probable cause, and recommended next steps.</p>
            </div>
          )}
          <button type="button" onClick={() => setActivePage('diagnosticHistory')} className={`mt-auto self-start pt-5 ${linkClass}`}>View Past Checks <ChevronRight size={16} /></button>
        </section>

        <section className={`rigmd-card-surface flex min-w-0 flex-col rounded-lg border p-6 ${hasWarnings ? 'border-amber-400/50' : ''}`}>
          <h3 className="flex items-center gap-2 font-semibold text-white"><AlertTriangle size={18} className={hasWarnings || hasPatterns ? 'text-amber-300' : 'text-slate-300'} /> Warnings &amp; Repeated Problems</h3>
          {unavailable ? <p className="mt-4 text-sm text-slate-300">Warnings and repeated problems are unavailable.</p> : (
            <div className="mt-4 mb-4 space-y-4">
              <p className={`text-sm ${hasWarnings ? 'font-semibold text-amber-200' : 'text-slate-300'}`}>
                {hasWarnings ? `${dashboard.warning_signs_active_count} warning sign${dashboard.warning_signs_active_count === 1 ? '' : 's'} recorded` : latest ? 'No warning signs recorded.' : 'Not assessed yet.'}
              </p>
              {hasWarnings && dashboard.recent_warning_signs.length > 0 && (
                <ul className="space-y-2 text-sm text-slate-300">
                  {dashboard.recent_warning_signs.slice(0, 3).map((warning) => <li key={warning.id} className="break-words">{warning.warning_sign}</li>)}
                </ul>
              )}
              {hasPatterns && <button type="button" onClick={() => setActivePage('recurringPatterns')} className={linkClass}>
                {dashboard.recurring_issues_count} thing{dashboard.recurring_issues_count === 1 ? '' : 's'} appeared more than once <ChevronRight size={16} />
              </button>}
              {!hasPatterns && hasWarnings && <p className="text-sm text-slate-300">No repeated problems detected in saved checks.</p>}
              {!hasWarnings && !hasPatterns && (
                <div className="border-t border-slate-700/50 pt-4">
                  <p className="text-sm font-medium text-slate-200">{latest ? 'No repeated problems detected in saved checks' : 'Results appear after diagnostic checks'}</p>
                  <p className="mt-2 text-sm leading-relaxed text-slate-300">Warnings and recurring symptoms will appear here as you complete checks.</p>
                </div>
              )}
            </div>
          )}
          <button type="button" onClick={() => setActivePage('warningSigns')} className={`mt-auto self-start pt-5 ${linkClass}`}>View Alerts <ChevronRight size={16} /></button>
        </section>
      </div>

    </div>
  );
}
