import { Accessibility, RotateCcw } from 'lucide-react';
import { useDisplayPreferences } from '../lib/displayPreferencesContext';
import type { ReadingSize } from '../lib/displayPreferences';

const sizes: { value: ReadingSize; label: string }[] = [
  { value: 'standard', label: 'Standard' }, { value: 'larger', label: 'Larger' }, { value: 'largest', label: 'Largest' },
];

export default function ReadingSettings() {
  const { preferences, systemReducesMotion, saveStatus, update, reset } = useDisplayPreferences();
  return <section aria-labelledby="reading-settings-title" className="min-w-0 rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5 sm:p-6">
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div><h2 id="reading-settings-title" className="flex items-center gap-3 text-lg font-bold text-white"><Accessibility aria-hidden="true" className="shrink-0 text-cyan-300" />Reading and movement</h2>
        <p className="mt-2 text-sm leading-relaxed text-slate-300">Make RigMD easier to read and more comfortable to use.</p></div>
      <button type="button" onClick={reset} className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-slate-600 px-3 text-sm text-slate-200 hover:border-cyan-300"><RotateCcw size={16} aria-hidden="true" />Restore defaults</button>
    </div>
    <fieldset className="mt-6"><legend className="font-semibold text-white">Reading size</legend>
      <p className="mt-1 text-sm text-slate-400">Enlarge text and controls in the app. Your exported reports keep their layout.</p>
      <div className="mt-3 grid gap-3 sm:grid-cols-3">{sizes.map(size => <label key={size.value} className={`flex min-h-14 cursor-pointer items-center gap-3 rounded-xl border p-4 ${preferences.readingSize === size.value ? 'border-cyan-400 bg-cyan-400/10 text-white' : 'border-[var(--rigmd-border)] bg-black/20 text-slate-300'}`}>
        <input type="radio" name="rigmd-reading-size" value={size.value} checked={preferences.readingSize === size.value} onChange={() => update({ readingSize: size.value })} className="h-5 w-5 shrink-0 accent-cyan-400" /><span>{size.label}</span>
      </label>)}</div>
    </fieldset>
    <div className="mt-4 rounded-xl border border-[var(--rigmd-border)] bg-[#15222e] p-4"><p className="text-xs font-semibold uppercase tracking-wider text-cyan-300">Reading preview</p><p className="mt-2 leading-relaxed text-white">Review what was checked, then choose your next step.</p></div>
    <fieldset className="mt-6"><legend className="font-semibold text-white">Animations</legend>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">{[{ value: 'system' as const, label: 'Follow Windows', detail: 'Use your Windows animation preference.' }, { value: 'reduced' as const, label: 'Reduce animations', detail: 'Keep screens and transitions mostly still.' }].map(option => <label key={option.value} className={`flex cursor-pointer items-start gap-3 rounded-xl border p-4 ${preferences.motion === option.value ? 'border-cyan-400 bg-cyan-400/10 text-white' : 'border-[var(--rigmd-border)] bg-black/20 text-slate-300'}`}>
        <input type="radio" name="rigmd-motion" value={option.value} checked={preferences.motion === option.value} onChange={() => update({ motion: option.value })} className="mt-0.5 h-5 w-5 shrink-0 accent-cyan-400" /><span><span className="block font-semibold">{option.label}</span><span className="mt-1 block text-sm leading-relaxed text-slate-300">{option.detail}</span></span>
      </label>)}</div>
      <p className="mt-2 text-xs leading-relaxed text-slate-400">{systemReducesMotion ? 'Windows already requests reduced animations. RigMD will respect that in either option.' : 'If Windows requests reduced animations, RigMD will respect that too.'} This controls the app’s screens; Windows dialogs keep their own settings.</p>
    </fieldset>
    <p role="status" aria-live="polite" className={`mt-5 border-t border-[var(--rigmd-border)] pt-4 text-sm ${saveStatus === 'session-only' ? 'text-amber-200' : 'text-slate-300'}`}>
      {saveStatus === 'session-only' ? 'Applied for this session. RigMD couldn’t save these preferences; you may need to choose them again next time.' : saveStatus === 'saved' ? 'Applied and saved on this PC. No need to press Save Settings below.' : 'Changes apply immediately and are remembered on this PC.'}
    </p>
  </section>;
}
