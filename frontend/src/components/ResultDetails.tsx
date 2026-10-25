import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { FileText, X, type LucideIcon } from 'lucide-react';

export function ResultDialog({ open, onClose, title, children }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode;
}) {
  const heading = useId();
  const panel = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const overflow = document.body.style.overflow;
    const root = document.getElementById('root');
    const wasInert = root?.inert ?? false;
    if (root) root.inert = true;
    document.body.style.overflow = 'hidden';
    panel.current?.focus();
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); onClose(); return; }
      if (event.key !== 'Tab') return;
      const controls = [...(panel.current?.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), summary, [tabindex="0"]') ?? [])]
        .filter(element => element.getClientRects().length > 0);
      const first = controls[0], last = controls[controls.length - 1];
      if (!first || !last) { event.preventDefault(); return; }
      if (event.shiftKey && (document.activeElement === first || document.activeElement === panel.current)) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === panel.current)) {
        event.preventDefault(); first.focus();
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('keydown', handleKey);
      document.body.style.overflow = overflow;
      if (root) root.inert = wasInert;
      if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
    };
  }, [open, onClose]);
  if (!open) return null;
  return createPortal(<div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/70 p-4"
    onClick={event => { if (event.target === event.currentTarget) onClose(); }}>
    <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={heading} tabIndex={-1}
      className="flex max-h-[calc(100dvh-2rem)] w-full min-w-0 max-w-2xl flex-col overflow-hidden rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5 shadow-2xl outline-none [overflow-wrap:anywhere]">
      <div data-testid="result-dialog-header" className="mb-4 flex shrink-0 items-start justify-between gap-4 border-b border-[var(--rigmd-border)] pb-4">
        <h2 id={heading} className="min-w-0 text-xl font-bold text-white">{title}</h2>
        <button type="button" onClick={onClose} aria-label="Close details" title="Close details"
          className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-[var(--rigmd-border)] text-slate-300 hover:text-white"><X size={18} /></button>
      </div>
      <div data-testid="result-dialog-body" className="custom-scrollbar min-h-0 overflow-y-auto overscroll-contain pr-1">{children}</div>
    </div>
  </div>, document.body);
}

export default function ResultDetails({ label, title, children, icon: Icon = FileText }: {
  label: string; title: string; children: ReactNode; icon?: LucideIcon;
}) {
  const [open, setOpen] = useState(false);
  const close = useCallback(() => setOpen(false), []);
  return <>
    <button type="button" onClick={() => setOpen(true)} aria-haspopup="dialog"
      className="mr-3 inline-flex min-h-11 items-center gap-2 rounded-lg border border-[var(--rigmd-border)] px-3 py-2 text-sm font-semibold text-slate-200 hover:border-cyan-400/50 hover:text-cyan-200">
      <Icon size={16} />{label}
    </button>
    <ResultDialog open={open} onClose={close} title={title}>{children}</ResultDialog>
  </>;
}
