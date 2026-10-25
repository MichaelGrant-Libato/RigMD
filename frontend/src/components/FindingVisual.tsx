import { ArrowRight, Battery, CircleHelp, ClipboardList, Cpu, HardDrive, Info, MemoryStick, Minus, ScanLine, Thermometer } from 'lucide-react';
import type { DiagnosticReading } from '../lib/diagnosticLanguage';
import { getFindingVisual, getUsageComparison } from '../lib/findingVisuals';

const fills = { attention: 'bg-amber-400', normal: 'bg-emerald-400', info: 'bg-sky-400', unknown: 'bg-slate-500' };
const colors = { attention: 'text-amber-200', normal: 'text-emerald-300', info: 'text-sky-300', unknown: 'text-slate-400' };

function UsageBar({ value, label, fill, reviewAt, compact = false }: { value: number; label: string; fill: string; reviewAt?: number; compact?: boolean }) {
  return <div className={`relative w-full rounded-sm bg-white/[0.08] ${compact ? 'h-3' : 'h-7'}`}
    role="meter" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={value} aria-valuetext={`${value}%`}>
    <div className={`h-full rounded-sm ${fill}`} style={{ width: `${value}%` }} />
    {reviewAt !== undefined && <span aria-hidden="true" className="absolute -top-1 h-9 w-0 border-l-2 border-white/75" style={{ left: `${reviewAt}%` }} />}
  </div>;
}

export default function FindingVisual({ reading, originalReadings, historical = false }: {
  reading: DiagnosticReading; originalReadings?: DiagnosticReading[]; historical?: boolean;
}) {
  const visual = getFindingVisual(reading);
  const comparison = originalReadings && !historical ? getUsageComparison(reading, originalReadings) : null;
  const id = reading.metric_id ?? '';
  const Icon = id.startsWith('memory') ? MemoryStick : id.startsWith('storage') ? HardDrive : id.startsWith('cpu') ? Cpu : Battery;
  return <section aria-label={`${visual.label} visual summary`} data-testid="finding-visual" className="min-w-0 space-y-4">
    <p className={`text-xs font-semibold ${colors[visual.tone]}`}>{visual.statusLabel}</p>
    {visual.kind === 'usage' ? <>
      <div className="flex min-w-0 items-start justify-between gap-3">
        <div className="min-w-0"><p className="text-3xl font-bold tabular-nums text-white">{visual.percent}%</p><p className="mt-1 text-sm text-slate-300">{visual.usedLabel}</p></div>
        <Icon size={32} className={`mt-1 shrink-0 ${colors[visual.tone]}`} aria-hidden="true" />
      </div>
      <UsageBar value={visual.percent} label={`${visual.label}: ${visual.usedLabel.toLowerCase()}`} fill={fills[visual.tone]} reviewAt={visual.reviewAt} />
      {visual.capacity && <p className="text-xs tabular-nums text-slate-400">{visual.capacity.total} GB total</p>}
      <div className="grid grid-cols-2 gap-4 text-sm">
        <div className="min-w-0"><p className="flex items-center gap-2 text-xs text-slate-400"><span aria-hidden="true" className={`h-2.5 w-2.5 shrink-0 ${fills[visual.tone]}`} />{visual.usedLabel}</p>
          <p className="mt-1 font-semibold tabular-nums text-white">{visual.capacity ? `${visual.capacity.used} GB` : `${visual.percent}%`}</p></div>
        <div className="min-w-0"><p className="flex items-center gap-2 text-xs text-slate-400"><span aria-hidden="true" className="h-2.5 w-2.5 shrink-0 border border-slate-500 bg-white/[0.08]" />{visual.remainingLabel}</p>
          <p className="mt-1 font-semibold tabular-nums text-white">{visual.capacity ? `About ${visual.capacity.available} GB` : `${visual.remainder}%`}</p></div>
      </div>
      {visual.reviewAt !== undefined && <p className="text-xs text-slate-400">Review level: {visual.reviewAt}% in use. This is not a hardware-failure limit.</p>}
      {comparison && <div className="space-y-2 border-t border-[var(--rigmd-border)] pt-4">
        <p className="text-sm font-semibold text-slate-100">{comparison.description}</p>
        <div className="grid grid-cols-[5rem_minmax(0,1fr)_4rem] items-center gap-2 text-xs">
          <span className="text-slate-400">Original</span><UsageBar compact value={comparison.original} label="Original saved percentage" fill="bg-slate-400" /><span className="text-right tabular-nums text-slate-300">{comparison.original}%</span>
          <span className="text-slate-300">Follow-up</span><UsageBar compact value={comparison.latest} label="Follow-up saved percentage" fill={fills[visual.tone]} /><span className="text-right tabular-nums text-white">{comparison.latest}%</span>
        </div>
        <p className="text-xs text-slate-400">Two saved readings, not proof that an action caused the change.</p>
      </div>}
    </> : visual.kind === 'unavailable' ? <div className="flex items-center gap-4 border-y border-dashed border-slate-500/50 py-5">
      <div aria-hidden="true" className="relative shrink-0 text-slate-400">{visual.temperature ? <Thermometer size={42} /> : <CircleHelp size={42} />}<Minus size={18} className="absolute -bottom-1 -right-1 bg-[var(--rigmd-card)]" /></div>
      <div className="min-w-0"><p className="text-lg font-semibold text-slate-200">{visual.title}</p><p className="mt-1 text-xs text-slate-400">{visual.caption}</p></div>
    </div> : visual.kind === 'report' ? <div className="space-y-3 border-y border-[var(--rigmd-border)] py-4">
      <div className="flex items-center gap-3 text-slate-400"><HardDrive size={24} aria-hidden="true" /><span className="text-xs">Windows drive report</span><ArrowRight size={16} aria-hidden="true" /><ClipboardList size={24} aria-hidden="true" /></div>
      <p className={`text-xl font-bold ${colors[visual.tone]}`}>{visual.value}</p>
      <p className="flex items-center gap-2 text-xs text-slate-400"><Info size={14} className="shrink-0" aria-hidden="true" />A reported status, not a complete health test</p>
    </div> : <div className="flex min-w-0 items-center gap-4 border-y border-[var(--rigmd-border)] py-4">
      {visual.kind === 'temperature' ? <Thermometer size={36} className={`shrink-0 ${colors[visual.tone]}`} aria-hidden="true" /> : <ScanLine size={32} className={`shrink-0 ${colors[visual.tone]}`} aria-hidden="true" />}
      <p className="min-w-0 text-xl font-bold text-white [overflow-wrap:anywhere]">{visual.value}</p>
    </div>}
  </section>;
}
