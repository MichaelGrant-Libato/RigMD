import {
  Activity,
  CheckCircle2,
  LockKeyhole,
  MailCheck,
  MonitorDown,
  ShieldCheck,
} from 'lucide-react';
import { motion } from 'motion/react';

const steps = [
  { icon: LockKeyhole, label: 'Register' },
  { icon: MailCheck, label: 'Verify email' },
  { icon: MonitorDown, label: 'Download' },
];

function AuthFormShell({ title, subtitle, children, footer }) {
  return (
    <div className="min-h-screen overflow-hidden bg-[var(--rigmd-bg)] text-[var(--rigmd-text-main)]">
      <div className="absolute inset-0 bg-[linear-gradient(135deg,rgba(45,212,191,0.10),transparent_32%),linear-gradient(180deg,rgba(15,24,36,0.88),#070c12_72%)]" />

      <header className="relative z-10 border-b border-[var(--rigmd-border-soft)] bg-[var(--rigmd-header)]/80 backdrop-blur-xl">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-5 lg:px-8">
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-lg border border-[var(--rigmd-border-active)] bg-[var(--rigmd-accent-soft)]">
              <Activity size={22} className="text-[var(--rigmd-accent)]" />
            </div>
            <div>
              <div className="text-lg font-semibold tracking-tight">RigMD</div>
              <div className="text-xs text-[var(--rigmd-text-muted)]">
                Windows Diagnostic Support
              </div>
            </div>
          </div>

          <span className="hidden text-xs font-semibold uppercase tracking-[0.18em] text-[var(--rigmd-text-faint)] sm:block">
            Verified installer access
          </span>
        </div>
      </header>

      <main className="relative z-10 mx-auto grid min-h-[calc(100vh-82px)] max-w-7xl items-center gap-10 px-6 py-12 lg:grid-cols-[0.95fr_440px] lg:px-8">
        <motion.section
          initial={{ opacity: 0, x: -26 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.55, ease: 'easeOut' }}
          className="max-w-3xl"
        >
          <p className="text-sm font-semibold uppercase tracking-[0.18em] text-[var(--rigmd-accent)]">
            Secure RigMD Access
          </p>

          <h1 className="mt-5 text-4xl font-bold tracking-tight text-white sm:text-5xl lg:text-6xl">
            Sign in before the installer reaches your PC.
          </h1>

          <p className="mt-6 max-w-2xl text-base leading-7 text-[var(--rigmd-text-muted)] sm:text-lg">
            RigMD keeps the public download flow separate from the local
            desktop dashboard. Create an account, verify your email, then
            unlock the installer.
          </p>

          <div className="mt-10 grid max-w-3xl gap-3 sm:grid-cols-3">
            {steps.map((step, index) => {
              const Icon = step.icon;

              return (
                <motion.div
                  key={step.label}
                  initial={{ opacity: 0, y: 18 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.4, delay: 0.12 + index * 0.08 }}
                  className="rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card)]/75 p-4 shadow-lg shadow-black/10"
                >
                  <Icon size={20} className="text-[var(--rigmd-accent)]" />
                  <p className="mt-4 text-sm font-semibold text-white">
                    {step.label}
                  </p>
                </motion.div>
              );
            })}
          </div>

          <div className="mt-8 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)]/80 p-5">
            <div className="flex items-start gap-3">
              <ShieldCheck
                size={20}
                className="mt-0.5 shrink-0 text-[var(--rigmd-success)]"
              />
              <p className="text-sm leading-6 text-[var(--rigmd-text-soft)]">
                Only verified accounts can reach the download page.
              </p>
            </div>
          </div>
        </motion.section>

        <motion.section
          initial={{ opacity: 0, x: 28, scale: 0.98 }}
          animate={{ opacity: 1, x: 0, scale: 1 }}
          transition={{ duration: 0.55, ease: 'easeOut' }}
          className="rigmd-glass rounded-lg border p-6 shadow-2xl shadow-black/30 sm:p-7"
        >
          <div className="mb-6">
            <div className="mb-4 inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-accent-soft)] px-3 py-1.5 text-xs font-semibold text-[var(--rigmd-accent)]">
              <CheckCircle2 size={14} />
              Account required
            </div>

            <h2 className="text-2xl font-semibold text-white">{title}</h2>

            <p className="mt-2 text-sm leading-6 text-[var(--rigmd-text-muted)]">
              {subtitle}
            </p>
          </div>

          {children}

          {footer && (
            <div className="mt-6 border-t border-[var(--rigmd-border-soft)] pt-5 text-sm text-[var(--rigmd-text-muted)]">
              {footer}
            </div>
          )}
        </motion.section>
      </main>
    </div>
  );
}

export default AuthFormShell;