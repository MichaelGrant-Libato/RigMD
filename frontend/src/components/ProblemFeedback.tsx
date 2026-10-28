import { useEffect, useRef, useState, type FormEvent } from 'react';
import { CheckCircle2, CircleHelp, MessageSquare, RefreshCw } from 'lucide-react';
import { apiFetch } from '../lib/api';

type Outcome = 'still_happening' | 'not_happening' | 'not_sure';
interface Feedback {
  feedback_id: string;
  session_id: string;
  outcome: Outcome;
  note: string;
  created_at: string;
}
const choices: { value: Outcome; label: string }[] = [
  { value: 'still_happening', label: 'Yes, still happening' },
  { value: 'not_happening', label: 'No, not noticing it now' },
  { value: 'not_sure', label: 'Not sure' },
];
const answerLabel = (value: Outcome) => choices.find(choice => choice.value === value)?.label ?? 'Not sure';
const savedDate = (value: string) => new Date(value).toLocaleString();

export default function ProblemFeedback({ sessionId }: { sessionId: string }) {
  const [history, setHistory] = useState<Feedback[]>([]);
  const [outcome, setOutcome] = useState<Outcome | ''>('');
  const [note, setNote] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState('');
  const [message, setMessage] = useState('');
  const savingRef = useRef(false);
  const latest = history[0];
  const changed = outcome !== (latest?.outcome ?? '') || note.trim() !== (latest?.note ?? '');
  const path = `/api/diagnosis/sessions/${encodeURIComponent(sessionId)}/feedback`;

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setLoadError(false);
    setMessage('');
    setSaveError('');
    void (async () => {
      try {
        const response = await apiFetch(path, { signal: controller.signal });
        if (!response.ok) throw new Error('Feedback unavailable');
        const data: Feedback[] = await response.json();
        if (!Array.isArray(data)) throw new Error('Invalid feedback');
        if (controller.signal.aborted) return;
        setHistory(data);
        setOutcome(data[0]?.outcome ?? '');
        setNote(data[0]?.note ?? '');
      } catch {
        if (!controller.signal.aborted) setLoadError(true);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [path, attempt]);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!outcome || !changed || savingRef.current || note.length > 500) return;
    savingRef.current = true;
    setSaving(true);
    setSaveError('');
    setMessage('');
    try {
      const response = await apiFetch(path, { method: 'POST', body: JSON.stringify({ outcome, note: note.trim() }) });
      if (!response.ok) throw new Error('Feedback not saved');
      const saved: Feedback = await response.json();
      setHistory(previous => [saved, ...previous].slice(0, 20));
      setOutcome(saved.outcome);
      setNote(saved.note);
      setMessage('Your answer is saved. The diagnostic readings have not changed.');
    } catch {
      setSaveError('Your answer could not be saved. Your changes are still here; please try again.');
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  }

  return (
    <section aria-labelledby={`feedback-title-${sessionId}`} className="min-w-0 rounded-2xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-4 sm:p-5">
      <div className="flex items-start gap-3">
        <MessageSquare size={22} aria-hidden="true" className="mt-1 shrink-0 text-cyan-300" />
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-cyan-300">Your experience</p>
          <h2 id={`feedback-title-${sessionId}`} className="mt-1 text-lg font-bold text-white">Is the problem still happening?</h2>
          <p className="mt-2 text-sm leading-relaxed text-slate-300">If you were checking a problem, tell us what you notice now. This answer is about your experience with this saved check.</p>
        </div>
      </div>
      {loading ? <p role="status" className="mt-5 flex items-center gap-2 text-sm text-slate-400"><RefreshCw size={16} aria-hidden="true" className="animate-spin" /> Loading your feedback...</p>
        : loadError ? <div role="alert" className="mt-5 text-sm text-amber-200">
          <p>Your saved feedback could not be loaded. Your diagnostic results are still available.</p>
          <button type="button" onClick={() => setAttempt(value => value + 1)} className="mt-3 min-h-11 rounded-lg border border-cyan-400/40 px-4 py-2 font-semibold text-cyan-200">Try loading feedback again</button>
        </div> : <>
          {latest && <div className="mt-5 rounded-xl border border-cyan-400/20 bg-cyan-400/5 p-4 text-sm">
            <p className="font-semibold text-white">Your latest saved answer: {answerLabel(latest.outcome)}</p>
            <p className="mt-1 text-xs text-slate-400">Saved {savedDate(latest.created_at)}</p>
            {latest.note && <p className="mt-2 whitespace-pre-wrap leading-relaxed text-slate-300 [overflow-wrap:anywhere]">{latest.note}</p>}
          </div>}
          <form onSubmit={save} className="mt-5 space-y-4">
            <fieldset disabled={saving}>
              <legend className="sr-only">Your answer</legend>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                {choices.map(choice => <label key={choice.value} className={`flex min-h-14 cursor-pointer items-center gap-3 rounded-xl border p-3 text-sm font-semibold focus-within:ring-2 focus-within:ring-cyan-300 ${outcome === choice.value ? 'border-cyan-400 bg-cyan-400/10 text-cyan-100' : 'border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] text-slate-300'} ${saving ? 'cursor-wait opacity-60' : ''}`}>
                  <input type="radio" name={`problem-feedback-${sessionId}`} value={choice.value} checked={outcome === choice.value}
                    onChange={() => { setOutcome(choice.value); setMessage(''); setSaveError(''); }} className="h-4 w-4 shrink-0 accent-cyan-400" />
                  {choice.label}
                </label>)}
              </div>
            </fieldset>
            <div>
              <label htmlFor={`feedback-note-${sessionId}`} className="text-sm font-semibold text-white">Anything you want to add? <span className="font-normal text-slate-400">(Optional)</span></label>
              <textarea id={`feedback-note-${sessionId}`} value={note} maxLength={500} rows={3} disabled={saving} aria-describedby={`feedback-note-help-${sessionId}`}
                onChange={event => { setNote(event.target.value); setMessage(''); setSaveError(''); }}
                placeholder="For example: The app still freezes when I open a video."
                className="mt-2 block w-full resize-y rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-bg)] p-3 text-sm text-white placeholder:text-slate-500 focus:border-cyan-400 focus:outline-none disabled:opacity-60" />
              <p id={`feedback-note-help-${sessionId}`} className="mt-1 text-right text-xs text-slate-400">{note.length}/500 characters</p>
            </div>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-xs text-slate-400">{changed && outcome ? 'Changes not saved yet.' : latest ? 'You can update your answer later.' : 'Choose an answer when you are ready.'}</p>
              <button type="submit" disabled={!outcome || !changed || saving || note.length > 500} aria-busy={saving}
                className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg bg-cyan-400 px-5 py-2 text-sm font-bold text-[#041014] hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-50">
                {saving && <RefreshCw size={16} aria-hidden="true" className="animate-spin" />}
                {saving ? 'Saving...' : latest ? 'Update feedback' : 'Save feedback'}
              </button>
            </div>
            {saveError && <p role="alert" className="text-sm leading-relaxed text-amber-200">{saveError}</p>}
            <p role="status" aria-live="polite" className="flex items-start gap-2 text-sm text-cyan-200">{message && <><CheckCircle2 size={18} aria-hidden="true" className="shrink-0" />{message}</>}</p>
          </form>
          {history.length > 1 && <details className="mt-5 border-t border-[var(--rigmd-border)] pt-4 text-sm">
            <summary className="w-fit cursor-pointer rounded font-semibold text-slate-300 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-cyan-300">Previous answers ({history.length - 1})</summary>
            <p className="mt-2 text-xs text-slate-400">Showing up to 20 recent answers, including your latest answer above.</p>
            <ol className="mt-3 divide-y divide-[var(--rigmd-border)]">
              {history.slice(1).map(item => <li key={item.feedback_id} className="py-3">
                <p className="font-semibold text-slate-200">{answerLabel(item.outcome)}</p>
                <p className="mt-1 text-xs text-slate-400">Saved {savedDate(item.created_at)}</p>
                {item.note && <p className="mt-2 whitespace-pre-wrap text-slate-300 [overflow-wrap:anywhere]">{item.note}</p>}
              </li>)}
            </ol>
          </details>}
        </>}
      <div className="mt-5 flex items-start gap-2 border-t border-[var(--rigmd-border)] pt-4 text-xs leading-relaxed text-slate-400">
        <CircleHelp size={16} aria-hidden="true" className="mt-0.5 shrink-0" />
        <p>Feedback is saved on this PC with this check. It does not change measured readings, confirm a repair, or run an action. Use <strong className="font-semibold text-slate-300">Check again</strong> when available to take new readings.</p>
      </div>
    </section>
  );
}
