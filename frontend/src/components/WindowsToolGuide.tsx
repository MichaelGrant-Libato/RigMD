import { ArrowRight, CheckCircle2, ChevronDown, ExternalLink, Info, Search } from 'lucide-react';
import { getWindowsToolGuide } from '../lib/windowsToolGuide';

export default function WindowsToolGuide({ target, appName, onOpen, disabled, busy }: {
  target: string; appName?: string; onOpen: () => void | Promise<unknown>; disabled?: boolean; busy?: boolean;
}) {
  const guide = getWindowsToolGuide(target, appName);
  const icons = [Search, Info, CheckCircle2];
  return <details className="group min-w-0 rounded-xl border border-cyan-400/25 bg-cyan-400/5 p-3">
    <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-3 text-sm font-semibold text-cyan-200">
      <span>Visual guide: {guide.name}</span><ChevronDown aria-hidden="true" size={17} className="shrink-0 group-open:rotate-180" />
    </summary>
    <ol aria-label={`Steps for ${guide.name}`} className="my-3 grid gap-2 sm:grid-cols-3">
      {guide.steps.map(([title, body], index) => {
        const Icon = icons[index];
        return <li key={title} className="min-w-0 rounded-lg border border-white/10 bg-[#101821] p-3">
          <div className="flex items-center justify-between text-cyan-300"><Icon size={22} aria-hidden="true" />{index < 2 && <ArrowRight size={16} aria-hidden="true" />}</div>
          <p className="mt-3 text-sm font-semibold text-white">{index + 1}. {title}</p>
          <p className="mt-2 break-words text-xs leading-relaxed text-slate-300">{body}</p>
        </li>;
      })}
    </ol>
    <p className="mb-3 text-xs text-slate-400">This is a guide, not a live view of Windows. Opening the tool makes no changes.</p>
    <button type="button" onClick={onOpen} disabled={disabled || busy} aria-busy={busy}
      className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-cyan-400/40 px-4 py-2 text-sm font-bold text-cyan-200 disabled:cursor-not-allowed disabled:opacity-50">
      <ExternalLink size={15} aria-hidden="true" />{busy ? 'Opening Windows…' : `Open ${guide.name}`}
    </button>
  </details>;
}
