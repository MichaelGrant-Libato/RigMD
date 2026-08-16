import { Activity, Cpu, ShieldCheck } from 'lucide-react';
import { motion, useReducedMotion } from 'motion/react';

const startupSteps = [
  'Checking local telemetry layer',
  'Preparing system profile scan',
  'Loading diagnostic workspace',
];

export default function SplashScreen() {
  const prefersReducedMotion = useReducedMotion();
  const itemMotion = prefersReducedMotion
    ? { hidden: { opacity: 0 }, visible: { opacity: 1 } }
    : {
        hidden: { opacity: 0, y: 6 },
        visible: { opacity: 1, y: 0 },
      };

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.98 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 1.02 }}
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="fixed inset-0 z-[9999] flex items-center justify-center overflow-hidden bg-[#070c12] px-6"
      role="status"
      aria-live="polite"
    >
      <div className="absolute inset-0 bg-[linear-gradient(135deg,rgba(45,212,191,0.05),transparent_32%),linear-gradient(225deg,rgba(34,211,238,0.04),transparent_30%)]" />
      <div className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-[#22d3ee]/35 to-transparent" />

      <motion.section
        initial={prefersReducedMotion ? { opacity: 0 } : { opacity: 0, y: 10 }}
        animate={prefersReducedMotion ? { opacity: 1 } : { opacity: 1, y: 0 }}
        transition={{ duration: 0.28, ease: 'easeOut', delay: 0.05 }}
        className="relative w-full max-w-[460px] rounded-2xl border border-[#263647] bg-[#141f2b]/95 px-8 py-8 text-center shadow-[0_24px_60px_rgba(0,0,0,0.36)]"
      >
        <motion.div
          initial={prefersReducedMotion ? { opacity: 0 } : { opacity: 0, y: 8, scale: 0.96 }}
          animate={prefersReducedMotion ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.28, ease: 'easeOut', delay: 0.08 }}
          className="mx-auto flex h-16 w-16 items-center justify-center rounded-2xl border border-cyan-400/25 bg-[#0f1824] text-cyan-300 shadow-[inset_0_1px_0_rgba(248,250,252,0.05)]"
        >
          <div className="relative flex h-11 w-11 items-center justify-center rounded-xl border border-[#263647] bg-[#070c12]">
            <Cpu size={24} />
            <span className="absolute -right-1 -top-1 h-2.5 w-2.5 rounded-full border border-[#0f1824] bg-[#34d399]" />
          </div>
        </motion.div>

        <div className="mt-6">
          <div className="flex items-center justify-center gap-2">
            <span className="h-2 w-2 rounded-full bg-[#34d399]" />
            <p className="text-[11px] font-bold uppercase tracking-[0.28em] text-[#94a3b8]">
              Local Diagnostic Layer
            </p>
          </div>

          <h1 className="mt-3 text-4xl font-bold tracking-normal text-[#f8fafc]">
            RigMD
          </h1>

          <p className="mt-1 text-sm font-semibold uppercase tracking-[0.18em] text-[#22d3ee]">
            Windows Diagnostic & Remediation System
          </p>
        </div>

        <div className="mt-7">
          <div className="mb-3 flex items-center justify-center gap-2 text-sm text-[#94a3b8]">
            <Activity size={15} className="text-[#2dd4bf]" />
            Initializing diagnostic workspace...
          </div>

          <motion.div
            variants={{
              hidden: {},
              visible: {
                transition: {
                  staggerChildren: prefersReducedMotion ? 0 : 0.08,
                },
              },
            }}
            initial="hidden"
            animate="visible"
            className="mb-5 space-y-2 rounded-xl border border-[#263647] bg-[#0f1824]/72 p-3 text-left"
          >
            {startupSteps.map((step, index) => (
              <motion.div
                key={step}
                variants={itemMotion}
                transition={{ duration: 0.22, ease: 'easeOut', delay: prefersReducedMotion ? 0 : index * 0.02 }}
                className="flex items-center gap-3 text-xs font-medium text-[#94a3b8]"
              >
                <span className="flex h-4 w-4 items-center justify-center rounded-full border border-[#263647] bg-[#070c12] text-[#34d399]">
                  <ShieldCheck size={10} />
                </span>
                {step}
              </motion.div>
            ))}
          </motion.div>

          <div className="h-1 overflow-hidden rounded-full border border-[#263647] bg-[#0f1824]">
            <motion.div
              className="h-full rounded-full bg-gradient-to-r from-[#2dd4bf] to-[#22d3ee]"
              initial={{ width: prefersReducedMotion ? '100%' : '0%' }}
              animate={{ width: '100%' }}
              transition={{ duration: prefersReducedMotion ? 0.01 : 1.05, ease: 'easeOut' }}
            />
          </div>
        </div>
      </motion.section>
    </motion.div>
  );
}
