import { useCallback, useId, useState } from 'react';
import { AlertTriangle, CheckCircle2, ChevronRight, HelpCircle, Info } from 'lucide-react';
import DiagnosticReadings from './DiagnosticReadings';
import { ResultDialog } from './ResultDetails';
import { expandDiagnosticReadings, type DiagnosticReading } from '../lib/diagnosticLanguage';
import { getFindingDetails } from '../lib/findingDetails';

export default function FindingSummaryList({ readings, originalReadings, hasFollowUp, historical = false }: {
  readings: DiagnosticReading[]; originalReadings?: DiagnosticReading[]; hasFollowUp: boolean; historical?: boolean;
}) {
  const tabsId = useId();
  const groups = [
    { id: 'attention', label: historical ? 'Recorded warnings' : 'Needs attention', rows: readings.filter(row => getFindingDetails(row).tone === 'attention') },
    { id: 'unknown', label: 'Not verified', rows: readings.filter(row => getFindingDetails(row).tone === 'unknown') },
    { id: 'other', label: 'Other readings', rows: readings.filter(row => !['attention', 'unknown'].includes(getFindingDetails(row).tone)) },
  ];
  const [filter, setFilter] = useState(groups.find(group => group.rows.length)?.id ?? 'attention');
  const [selected, setSelected] = useState<DiagnosticReading | null>(null);
  const close = useCallback(() => setSelected(null), []);
  const group = groups.find(item => item.id === filter)!;
  const selectedView = selected ? getFindingDetails(selected) : null;
  const before = selected && hasFollowUp ? expandDiagnosticReadings(originalReadings ?? []).filter(row =>
    selected.metric_id ? row.metric_id === selected.metric_id : Boolean(selected.label) && row.label === selected.label) : [];
  const tones = {
    attention: { icon: AlertTriangle, text: 'text-amber-200' }, unknown: { icon: HelpCircle, text: 'text-slate-400' },
    normal: { icon: CheckCircle2, text: 'text-emerald-300' }, info: { icon: Info, text: 'text-sky-300' },
  };
  return <section aria-label="Findings" className="min-w-0 space-y-3">
    <h3 className="text-base font-bold text-white">{historical ? 'Recorded findings' : 'Findings'}</h3>
    <div role="tablist" aria-label="Filter findings" className="flex flex-wrap gap-1 border-b border-[var(--rigmd-border)] pb-2">
      {groups.map((item, index) => <button key={item.id} id={`${tabsId}-${item.id}`} type="button" role="tab"
        aria-selected={filter === item.id} aria-controls={`${tabsId}-panel`} tabIndex={filter === item.id ? 0 : -1}
        onClick={() => setFilter(item.id)} onKeyDown={event => {
          const next = event.key === 'ArrowRight' ? (index + 1) % groups.length : event.key === 'ArrowLeft' ? (index + groups.length - 1) % groups.length
            : event.key === 'Home' ? 0 : event.key === 'End' ? groups.length - 1 : null;
          if (next === null) return;
          event.preventDefault(); setFilter(groups[next].id); document.getElementById(`${tabsId}-${groups[next].id}`)?.focus();
        }}
        className={`min-h-10 rounded-md px-3 py-2 text-xs font-semibold ${filter === item.id ? 'bg-cyan-400/10 text-cyan-200' : 'text-slate-400 hover:text-white'}`}>
        {item.label} <span className="ml-1 tabular-nums">{item.rows.length}</span>
      </button>)}
    </div>
    <div id={`${tabsId}-panel`} role="tabpanel" aria-labelledby={`${tabsId}-${filter}`} className="min-w-0">
      {!group.rows.length ? <p className="py-4 text-sm text-slate-400">No readings in this group.</p> : <ul className="divide-y divide-[var(--rigmd-border)]">
        {group.rows.map((reading, index) => {
          const view = getFindingDetails(reading);
          const tone = tones[view.tone];
          const Icon = tone.icon;
          return <li key={`${reading.metric_id}-${index}`}>
            <button type="button" aria-haspopup="dialog" aria-label={`View ${view.label} details`} onClick={() => setSelected(reading)}
              className="grid w-full min-w-0 grid-cols-[20px_minmax(0,1fr)_16px] items-center gap-x-3 gap-y-1 rounded-md px-2 py-3 text-left hover:bg-white/[0.03] sm:grid-cols-[20px_minmax(0,1fr)_minmax(100px,220px)_16px]">
              <Icon size={18} className={tone.text} aria-hidden="true" />
              <span className="min-w-0 text-sm font-semibold text-slate-100 [overflow-wrap:anywhere]">{view.label}</span>
              <span className={`col-start-2 row-start-2 min-w-0 text-sm font-semibold tabular-nums [overflow-wrap:anywhere] sm:col-start-3 sm:row-start-1 sm:text-right ${tone.text}`}>{view.valueSummary}</span>
              <ChevronRight size={16} className="col-start-3 row-start-1 text-slate-400 sm:col-start-4" aria-hidden="true" />
            </button>
          </li>;
        })}
      </ul>}
    </div>
    <ResultDialog open={Boolean(selectedView)} onClose={close} title={selectedView?.label ?? 'Finding details'}>
      {selected && selectedView && <div className="space-y-5">
        <div>
          <p className={`text-xs font-semibold ${tones[selectedView.tone].text}`}>{selectedView.statusLabel}</p>
          <p className="mt-2 text-xl font-bold text-white [overflow-wrap:anywhere]">{selectedView.value}</p>
          <p className="mt-2 text-xs text-slate-400">{historical ? 'Historical reading' : hasFollowUp ? 'Latest saved reading' : 'Original saved reading'}{selected.captured_at && Number.isFinite(Date.parse(selected.captured_at)) ? ` - ${new Date(selected.captured_at).toLocaleString()}` : ' - time not recorded'}</p>
        </div>
        <section><h3 className="text-sm font-semibold text-cyan-200">What this means</h3><p className="mt-2 text-sm leading-relaxed text-slate-200">{selectedView.explanation}</p></section>
        <section><h3 className="text-sm font-semibold text-cyan-200">What you can do</h3><p className="mt-2 text-sm leading-relaxed text-slate-200">{historical ? 'Use the newer result to decide whether any action is still needed.' : selectedView.nextStep}</p></section>
        {before.length > 0 && <section className="border-t border-[var(--rigmd-border)] pt-4">
          <h3 className="text-sm font-semibold text-slate-200">Original reading</h3>
          {before.map((row, index) => <p key={index} className="mt-2 text-sm text-slate-400">{getFindingDetails(row).value}{row.captured_at && Number.isFinite(Date.parse(row.captured_at)) ? ` - ${new Date(row.captured_at).toLocaleString()}` : ''}</p>)}
        </section>}
        <details className="border-t border-[var(--rigmd-border)] pt-3">
          <summary className="cursor-pointer py-2 text-sm font-semibold text-slate-300">Recorded evidence</summary>
          <div className="mt-2"><DiagnosticReadings readings={[selected]} title="Saved reading details" /></div>
          {before.length > 0 && <DiagnosticReadings readings={before} title="Original reading details" />}
        </details>
      </div>}
    </ResultDialog>
  </section>;
}
