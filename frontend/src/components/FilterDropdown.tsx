import { useEffect, useId, useRef, useState } from 'react';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { Check, ChevronDown } from 'lucide-react';

interface Props {
  label: string;
  value: string;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
}

export default function FilterDropdown({ label, value, options, onChange }: Props) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const id = useId();
  const reducedMotion = useReducedMotion();

  useEffect(() => {
    if (!open) return;
    const dismiss = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', dismiss);
    return () => document.removeEventListener('pointerdown', dismiss);
  }, [open]);

  return (
    <div ref={root} className="relative flex items-center gap-3 text-sm" onBlur={(event) => {
      if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
    }} onKeyDown={(event) => {
      if (event.key === 'Escape') {
        setOpen(false);
        trigger.current?.focus();
      }
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        if (!open) {
          setOpen(true);
          return;
        }
        const buttons = Array.from(root.current?.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]') ?? []);
        const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
        const next = event.key === 'ArrowDown' ? (current + 1) % buttons.length : (current <= 0 ? buttons.length - 1 : current - 1);
        buttons[next]?.focus();
      }
    }}>
      <span id={`${id}-label`} className="font-semibold text-slate-300">{label}</span>
      <div className="relative min-w-0">
        <button ref={trigger} type="button" aria-haspopup="menu" aria-expanded={open} aria-controls={id} aria-labelledby={`${id}-label ${id}-value`} onClick={() => setOpen(!open)} className="flex min-h-10 min-w-[176px] items-center justify-between gap-5 rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-3 py-2 text-left font-medium text-slate-200 transition-colors hover:border-cyan-400/50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400">
          <span id={`${id}-value`}>{options.find((option) => option.value === value)?.label ?? value}</span>
          <ChevronDown size={16} className={`shrink-0 transition-transform duration-300 motion-reduce:transition-none ${open ? 'rotate-180' : ''}`} />
        </button>
        <AnimatePresence>
          {open && (
            <motion.div id={id} role="menu" aria-labelledby={`${id}-label`} initial={{ opacity: 0, y: reducedMotion ? 0 : -8 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: reducedMotion ? 0 : -4 }} transition={{ duration: reducedMotion ? 0 : 0.25, ease: 'easeOut' }} className="absolute left-0 top-full z-50 mt-2 max-h-72 min-w-full overflow-y-auto rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-1.5 shadow-xl">
              {options.map((option) => (
                <button key={option.value} type="button" role="menuitemradio" aria-checked={option.value === value} onClick={() => { onChange(option.value); setOpen(false); trigger.current?.focus(); }} className="flex w-full items-center justify-between gap-3 whitespace-nowrap rounded-lg px-3 py-2.5 text-left text-sm text-slate-200 transition-colors hover:bg-cyan-400/10 focus-visible:bg-cyan-400/10 focus-visible:outline focus-visible:outline-1 focus-visible:outline-cyan-400">
                  {option.label}
                  <Check size={14} className={`shrink-0 text-cyan-300 ${option.value === value ? '' : 'invisible'}`} />
                </button>
              ))}
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}
