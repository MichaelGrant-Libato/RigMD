import { useEffect, useState } from 'react';
import { Minus, Plus } from 'lucide-react';

const MIN_SIZE = 80;
const MAX_SIZE = 150;

export default function DisplaySizeControl() {
  const [size, setSize] = useState(() => {
    try {
      const saved = Number(localStorage.getItem('rigmd-display-size') ?? 100);
      return Number.isFinite(saved) ? Math.max(MIN_SIZE, Math.min(MAX_SIZE, saved)) : 100;
    } catch { return 100; }
  });

  useEffect(() => {
    document.documentElement.style.setProperty('--rigmd-display-scale', String(size / 100));
    try { localStorage.setItem('rigmd-display-size', String(size)); } catch { /* Storage is optional. */ }
  }, [size]);

  return (
    <div role="group" aria-label="Display size" className="flex shrink-0 items-center rounded-lg border border-[var(--rigmd-border)] text-slate-200">
      <button type="button" aria-label="Decrease display size" title="Decrease display size (minimum 80%)" disabled={size <= MIN_SIZE}
        onClick={() => setSize((value) => Math.max(MIN_SIZE, value - 10))} className="min-h-11 min-w-11 rounded-l-lg hover:bg-slate-700 disabled:opacity-40">
        <Minus size={16} className="mx-auto" />
      </button>
      <button type="button" aria-label={`Display size ${size}%. Reset to 100%`} title="Reset display size" onClick={() => setSize(100)} className="min-h-11 min-w-12 text-sm tabular-nums hover:bg-slate-700">
        <span aria-live="polite">{size}%</span>
      </button>
      <button type="button" aria-label="Increase display size" title="Increase display size (maximum 150%)" disabled={size >= MAX_SIZE}
        onClick={() => setSize((value) => Math.min(MAX_SIZE, value + 10))} className="min-h-11 min-w-11 rounded-r-lg hover:bg-slate-700 disabled:opacity-40">
        <Plus size={16} className="mx-auto" />
      </button>
    </div>
  );
}
