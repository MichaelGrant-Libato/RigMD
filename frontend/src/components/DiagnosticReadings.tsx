import { expandDiagnosticReadings, getReadingPresentation, plainDiagnosticText, readingStatusLabel, readingEvidenceLabel, type DiagnosticReading } from '../lib/diagnosticLanguage';
import { Activity, AlertTriangle, Battery, CheckCircle2, Clock3, Cpu, HardDrive, HelpCircle, Info, MemoryStick, Monitor, Thermometer, Wifi } from 'lucide-react';
import { readingPercent, readingTone } from '../lib/diagnosticReadingVisuals';

export default function DiagnosticReadings({ readings, title = 'What the check found' }: {
  readings?: DiagnosticReading[]; title?: string; technicalDetails?: boolean;
}) {
  if (!readings?.length) return null;
  const expanded = expandDiagnosticReadings(readings);
  const attention = expanded.filter(r => readingTone(getReadingPresentation(r).status) === 'attention').length;
  const unknown = expanded.filter(r => readingTone(getReadingPresentation(r).status) === 'unknown').length;
  const timestamps = new Set(expanded.map(r => r.captured_at).filter(Boolean));
  const commonTime = timestamps.size === 1 && expanded.every(r => r.captured_at) ? expanded[0].captured_at : null;
  const tones = {
    normal: { text: 'text-emerald-300', fill: 'bg-emerald-400', icon: CheckCircle2 },
    attention: { text: 'text-amber-200', fill: 'bg-amber-400', icon: AlertTriangle },
    unknown: { text: 'text-slate-400', fill: 'bg-slate-500', icon: HelpCircle },
    info: { text: 'text-sky-300', fill: 'bg-sky-400', icon: Info },
  };
  return <section className="min-w-0" aria-label={title}>
    <div className="flex flex-wrap items-center justify-between gap-2 border-b border-white/10 pb-3">
      <h4 className="text-sm font-semibold text-white">{title}</h4>
      <div className="flex flex-wrap items-center gap-3 text-xs">
        {attention > 0 && <span className="inline-flex items-center gap-1.5 text-amber-200"><AlertTriangle size={13} />{attention} need attention</span>}
        {unknown > 0 && <span className="inline-flex items-center gap-1.5 text-slate-400"><HelpCircle size={13} />{unknown} unavailable</span>}
      </div>
    </div>
    {commonTime && Number.isFinite(Date.parse(commonTime)) && <p className="mt-2 flex items-center gap-1.5 text-xs text-slate-400"><Clock3 size={12} />Recorded {new Date(commonTime).toLocaleString()}</p>}
    <ul className="divide-y divide-white/10">
      {expanded.map((reading, index) => {
        const view = getReadingPresentation(reading);
        const tone = tones[readingTone(view.status)];
        const StatusIcon = tone.icon;
        const id = reading.metric_id ?? '';
        const Icon = id.includes('temperature') ? Thermometer : id.startsWith('cpu') ? Cpu : id.startsWith('memory') ? MemoryStick
          : id.startsWith('storage') ? HardDrive : id.startsWith('graphics') || id.startsWith('gpu') ? Monitor
            : id.startsWith('network') ? Wifi : id.startsWith('battery') ? Battery : Activity;
        const percent = readingPercent(reading);
        return <li key={`${reading.label}-${index}`} className="grid min-w-0 grid-cols-[24px_minmax(0,1fr)] gap-3 py-4 [overflow-wrap:anywhere] sm:grid-cols-[28px_minmax(0,1fr)_minmax(140px,220px)] sm:gap-4">
          <Icon size={19} className={`mt-0.5 ${tone.text}`} aria-hidden="true" />
          <div className="min-w-0">
            <p className="text-sm font-semibold text-slate-100">{view.label}</p>
            <p className="mt-1 text-[11px] text-slate-500">{readingEvidenceLabel(reading)}</p>
            {view.meaning && <p className="mt-1 text-xs leading-relaxed text-slate-400">{view.meaning}</p>}
            {reading.meaning && /back.?up|save your work|unsaved|data loss/i.test(reading.meaning) && plainDiagnosticText(reading.meaning) !== view.meaning && <p className="mt-1 text-xs leading-relaxed text-amber-200">{plainDiagnosticText(reading.meaning)}</p>}
            {!commonTime && reading.captured_at && Number.isFinite(Date.parse(reading.captured_at)) && <p className="mt-2 inline-flex items-center gap-1 text-[11px] text-slate-500"><Clock3 size={11} />Recorded {new Date(reading.captured_at).toLocaleString()}</p>}
          </div>
          <div className="col-start-2 min-w-0 sm:col-start-3 sm:row-start-auto sm:text-right">
            <p className="text-base font-semibold tabular-nums text-white">{view.value}</p>
            {percent !== null && <div role="meter" aria-label={`${view.label} reading`} aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100} className="mt-2 h-1.5 overflow-hidden rounded bg-white/[0.07]">
              <div className={`h-full ${tone.fill}`} style={{ width: `${percent}%` }} />
            </div>}
            {view.status && <p className={`mt-2 inline-flex items-center gap-1.5 text-[11px] font-medium ${tone.text}`}><StatusIcon size={12} aria-hidden="true" />{readingStatusLabel(reading)}</p>}
          </div>
        </li>;
      })}
    </ul>
  </section>;
}
