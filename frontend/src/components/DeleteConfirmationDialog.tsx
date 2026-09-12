import { type ReactNode } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { AlertTriangle, Trash2, X } from 'lucide-react';

import { buttonTap } from '../lib/motion';

interface DeleteConfirmationDialogProps {
  open: boolean;
  title: string;
  description: string;
  details?: ReactNode;
  confirmLabel: string;
  isWorking?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

export default function DeleteConfirmationDialog({
  open,
  title,
  description,
  details,
  confirmLabel,
  isWorking,
  onCancel,
  onConfirm,
}: DeleteConfirmationDialogProps) {
  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4 backdrop-blur-sm"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18 }}
        >
          <motion.section
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-dialog-title"
            className="w-full max-w-xl overflow-hidden rounded-2xl border border-red-400/25 bg-[#111c28] shadow-2xl shadow-black/50"
            initial={{ opacity: 0, y: 18, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 18, scale: 0.97 }}
            transition={{ duration: 0.2 }}
          >
            <div className="flex items-start justify-between gap-4 border-b border-[var(--rigmd-border)] px-6 py-5">
              <div className="flex gap-4">
                <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-red-400/35 bg-red-400/10 text-red-200">
                  <AlertTriangle size={22} />
                </div>

                <div>
                  <p className="text-[11px] font-bold uppercase tracking-[0.2em] text-red-200">
                    Review before deleting
                  </p>
                  <h2 id="delete-dialog-title" className="mt-1 text-2xl font-bold text-white">
                    {title}
                  </h2>
                  <p className="mt-2 text-sm leading-relaxed text-slate-300">
                    {description}
                  </p>
                </div>
              </div>

              <motion.button
                type="button"
                onClick={onCancel}
                disabled={isWorking}
                whileTap={buttonTap}
                className="rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-2 text-slate-400 transition hover:border-cyan-400/40 hover:text-cyan-300 disabled:cursor-wait disabled:opacity-50"
                aria-label="Close delete confirmation"
              >
                <X size={18} />
              </motion.button>
            </div>

            <div className="space-y-4 px-6 py-5">
              {details}

              <div className="rounded-xl border border-amber-400/25 bg-amber-400/10 p-4">
                <p className="text-sm font-bold text-amber-100">This cannot be undone.</p>
                <p className="mt-1 text-sm leading-relaxed text-slate-300">
                  RigMD will only remove saved check history. It will not change your PC, Windows settings, files, or accounts.
                </p>
              </div>
            </div>

            <div className="flex flex-col-reverse gap-3 border-t border-[var(--rigmd-border)] px-6 py-5 sm:flex-row sm:justify-end">
              <motion.button
                type="button"
                onClick={onCancel}
                disabled={isWorking}
                whileTap={buttonTap}
                className="inline-flex min-h-11 items-center justify-center rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] px-5 py-2.5 text-sm font-bold text-slate-200 transition hover:border-cyan-400/40 hover:text-cyan-300 disabled:cursor-wait disabled:opacity-50"
              >
                Cancel
              </motion.button>

              <motion.button
                type="button"
                onClick={onConfirm}
                disabled={isWorking}
                whileTap={buttonTap}
                className="inline-flex min-h-11 items-center justify-center gap-2 rounded-xl border border-red-300/45 bg-red-400/15 px-5 py-2.5 text-sm font-bold text-red-100 transition hover:bg-red-400/25 disabled:cursor-wait disabled:opacity-60"
              >
                <Trash2 size={16} />
                {isWorking ? 'Deleting...' : confirmLabel}
              </motion.button>
            </div>
          </motion.section>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
