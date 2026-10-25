import type { ReactNode } from 'react';
import { ArrowRight, RefreshCw } from 'lucide-react';
import FindingSummaryList from './FindingSummaryList';
import { getCheckNextSteps, getResultHeading, getShortCheckSummary } from '../lib/checkNextSteps';
import { getCheckStatus, getOriginalResultStatus, type CheckPresentationInput } from '../lib/checkPresentation';
import { getCoverageMessage } from '../lib/findingDetails';
import { getSessionCheckLabel } from '../lib/pastChecksFormatting';
import { getResultActionAccess } from '../lib/remediationEligibility';
import { useRemediationEligibility } from '../lib/useRemediationEligibility';

type ResultCheck = CheckPresentationInput & { session_id?: string; resolution_checked_at?: string | null };

export default function CheckNextSteps({ check, checking, onCheckAgain, onOpenSession, children }: {
  check: ResultCheck; checking: boolean; onCheckAgain: () => void; onOpenSession?: (id: string) => void; children?: ReactNode;
}) {
  const view = getCheckNextSteps(check);
  const status = view.hasFollowUp ? getCheckStatus(check) : getOriginalResultStatus(check);
  const scope = getSessionCheckLabel(check);
  const recordedAt = view.hasFollowUp ? check.resolution_checked_at : check.proof?.find(row => row.captured_at)?.captured_at;
  const { eligibility, pending, error, refresh } = useRemediationEligibility(check.session_id, `${check.resolution_checked_at ?? ''}:${check.resolution_status ?? ''}`);
  const access = getResultActionAccess(check, eligibility);
  const historical = eligibility?.state === 'superseded';
  const title = historical ? scope : getResultHeading(check);
  return <div className="min-w-0 space-y-5">
    <section aria-label="Result and next steps" data-testid="result-next-steps" className="min-w-0 space-y-4 rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4 sm:p-5">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-start">
        <div className="min-w-0">
          <h2 data-testid="check-title" className="text-2xl font-bold text-white [overflow-wrap:anywhere]">{title}</h2>
          {historical ? <p className="mt-2 text-xs text-slate-400">Recorded finding: {getResultHeading(check)}</p>
            : title !== scope && <p className="mt-2 text-xs text-slate-400">{scope}</p>}
        </div>
        {access.recheck && <button type="button" onClick={onCheckAgain} disabled={checking}
          title="Take new readings for this check; no repair is run" aria-busy={checking}
          className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-cyan-400/40 bg-cyan-400/10 px-4 py-2 text-sm font-bold text-cyan-200 disabled:cursor-not-allowed disabled:opacity-50">
          <RefreshCw size={16} className={checking ? 'animate-spin' : ''} />{checking ? 'Checking...' : access.recheck === 'current' ? 'Check current status' : 'Check again'}
        </button>}
      </div>
      <div className="border-t border-[var(--rigmd-border)] pt-4">
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-xs font-semibold text-slate-400">{eligibility?.state === 'superseded' ? 'Recorded result from this check' : view.hasFollowUp ? 'Latest result for this check' : 'Saved result'}</p>
          <span className={`rounded border px-2 py-1 text-xs font-semibold ${status.className}`}>{status.label}</span>
        </div>
        <p className="mt-2 text-sm text-slate-200">{historical ? `This check recorded ${view.attention.length} reading${view.attention.length === 1 ? '' : 's'} needing attention.` : getShortCheckSummary(check)}</p>
        {view.unavailable.length > 0 && <p className="mt-2 text-sm text-amber-200">{getCoverageMessage(view.readings)}</p>}
        <p className="mt-2 text-xs text-slate-400">{view.hasFollowUp ? 'Follow-up saved' : 'Check recorded'}{recordedAt && Number.isFinite(Date.parse(recordedAt)) ? ` ${new Date(recordedAt).toLocaleString()}` : ': time not recorded'}</p>
      </div>
      {pending && check.session_id && <p role="status" className="border-t border-[var(--rigmd-border)] pt-4 text-sm text-slate-400">Checking for newer results...</p>}
      {error && <div role="alert" className="space-y-3 border-t border-[var(--rigmd-border)] pt-4 text-sm text-amber-200">
        <p>Could not check for newer results. Actions are unavailable until this is checked.</p>
        <button type="button" onClick={refresh} className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-cyan-400/40 px-4 py-2 font-bold text-cyan-200"><RefreshCw size={16} />Try again</button>
      </div>}
      {eligibility && eligibility.state !== 'allowed' && <div data-testid="historical-action-notice" className="space-y-3 border-t border-[var(--rigmd-border)] pt-4 text-sm text-slate-200">
        <p>{eligibility.message}</p>
        {eligibility.newer_session_id && onOpenSession && <button type="button" onClick={() => onOpenSession(eligibility.newer_session_id!)}
          className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-cyan-400/40 px-4 py-2 font-bold text-cyan-200"><ArrowRight size={16} />View newer result</button>}
      </div>}
      {access.remediation && children}
    </section>
    <FindingSummaryList readings={view.readings} originalReadings={check.proof} hasFollowUp={view.hasFollowUp} historical={historical} />
  </div>;
}
