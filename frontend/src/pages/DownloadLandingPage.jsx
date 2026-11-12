import { useEffect, useState } from 'react';
import {
  Activity,
  CheckCircle2,
  Download,
  History,
  LogOut,
  Monitor,
  ShieldCheck,
  TriangleAlert,
  Wrench,
} from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import { useNavigate } from 'react-router-dom';

import { supabase } from '../lib/supabase';
import release from '../../release.json';
import { releaseDownloadUrl } from '../../server/releasePolicy';

const features = [
  [
    'Live Hardware Detection',
    'View processor, memory, storage, GPU, and system details.',
    Activity,
  ],
  [
    'Guided Diagnosis',
    'Analyze system evidence and identify probable Windows PC issues.',
    Monitor,
  ],
  [
    'Diagnostic History',
    'Review previous sessions and recurring patterns.',
    History,
  ],
  [
    'Warning Signs',
    'Understand important system warning indicators.',
    TriangleAlert,
  ],
  [
    'Safe Remediation',
    'Receive controlled next steps with verification support.',
    Wrench,
  ],
  [
    'Local Windows Agent',
    'Collects supported Windows readings while the desktop app is running.',
    ShieldCheck,
  ],
];

function DownloadLandingPage() {
  const navigate = useNavigate();
  const [userEmail, setUserEmail] = useState('');
  const [showLogoutConfirm, setShowLogoutConfirm] = useState(false);
  const [downloadUrl, setDownloadUrl] = useState('');
  const [downloadMessage, setDownloadMessage] = useState('Checking the current download…');
  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/download-url', { signal: controller.signal, cache: 'no-store' }).then(async response => {
      const data = await response.json();
      if (!response.ok || data.available !== true || data.version !== release.version || data.url !== releaseDownloadUrl)
        throw new Error('The current download is being prepared. Please try again later.');
      if (!controller.signal.aborted) { setDownloadUrl(data.url); setDownloadMessage(`RigMD ${data.version}`); }
    }).catch(() => { if (!controller.signal.aborted) setDownloadMessage('The current download is unavailable. Please try again later.'); });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    let mounted = true;

    async function loadUser() {
      if (!supabase) return;

      const { data } = await supabase.auth.getUser();

      if (mounted) {
        setUserEmail(data.user?.email ?? '');
      }
    }

    loadUser();

    return () => {
      mounted = false;
    };
  }, []);

  const handleSignOut = async () => {
    await supabase?.auth.signOut();
    navigate('/login', { replace: true });
  };

  return (
    <div className="min-h-screen bg-[var(--rigmd-bg)] text-[var(--rigmd-text-main)]">
      <header className="border-b border-[var(--rigmd-border-soft)] bg-[var(--rigmd-header)]/95 backdrop-blur-xl">
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

          <div className="flex items-center gap-3">
            {userEmail && (
              <div className="hidden text-right text-xs text-[var(--rigmd-text-muted)] sm:block">
                <div className="font-medium text-[var(--rigmd-text-soft)]">
                  Verified account
                </div>
                <div>{userEmail}</div>
              </div>
            )}

            <button
              type="button"
              onClick={() => setShowLogoutConfirm(true)}
              className="inline-flex h-10 w-10 items-center justify-center rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] text-[var(--rigmd-text-muted)] transition hover:border-[var(--rigmd-accent)]/45 hover:text-[var(--rigmd-accent)]"
              aria-label="Sign out"
              title="Sign out"
            >
              <LogOut size={18} />
            </button>
          </div>
        </div>
      </header>

      <main>
        <section className="relative min-h-[640px] overflow-hidden border-b border-[var(--rigmd-border-soft)]">
          <div className="absolute inset-0 bg-[linear-gradient(120deg,#05090d_0%,#070c12_38%,rgba(45,212,191,0.13)_100%)]" />

          <motion.div
            initial={{ opacity: 0, x: 80 }}
            animate={{ opacity: 0.92, x: 0 }}
            transition={{ duration: 0.8, ease: 'easeOut' }}
            className="absolute right-[-80px] top-24 hidden w-[760px] lg:block"
          >
            <div className="grid rotate-[-2deg] grid-cols-2 gap-5">
              {features.slice(0, 4).map(([title, description, Icon], index) => (
                <div
                  key={title}
                  className="rigmd-card-surface rounded-lg border p-5 shadow-2xl shadow-black/30"
                  style={{
                    transform: `translateY(${index % 2 ? 48 : 0}px)`,
                  }}
                >
                  <Icon size={22} className="text-[var(--rigmd-accent)]" />
                  <h3 className="mt-5 font-semibold text-white">{title}</h3>
                  <p className="mt-2 text-sm leading-6 text-[var(--rigmd-text-muted)]">
                    {description}
                  </p>
                </div>
              ))}
            </div>
          </motion.div>

          <div className="relative mx-auto max-w-7xl px-6 py-24 lg:px-8 lg:py-32">
            <motion.div
              initial={{ opacity: 0, y: 24 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.55, ease: 'easeOut' }}
              className="max-w-2xl"
            >
              <div className="mb-6 inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] px-4 py-2 text-sm text-[var(--rigmd-text-soft)]">
                <CheckCircle2
                  size={16}
                  className="text-[var(--rigmd-success)]"
                />
                RigMD for verified Windows users
              </div>

              <h1 className="text-5xl font-bold tracking-tight text-white sm:text-6xl lg:text-7xl">
                Diagnose your PC with clearer evidence.
              </h1>

              <p className="mt-7 max-w-xl text-base leading-7 text-[var(--rigmd-text-muted)] sm:text-lg">
                RigMD helps analyze live system data, identify probable issues,
                and guide safer next steps before a problem gets worse.
              </p>

              <div className="mt-10 flex flex-col gap-4 sm:flex-row sm:items-center">
                <motion.a
                  whileHover={{ y: -2 }}
                  whileTap={{ scale: 0.98 }}
                  href={downloadUrl || undefined}
                  aria-disabled={!downloadUrl}
                  tabIndex={downloadUrl ? 0 : -1}
                  className="inline-flex items-center justify-center gap-3 rounded-lg bg-[var(--rigmd-accent)] px-8 py-4 font-semibold text-slate-950 shadow-lg shadow-black/20 transition hover:brightness-110"
                >
                  <Download size={20} />
                  Download RigMD
                </motion.a>

                <p role="status" className="text-sm text-[var(--rigmd-text-faint)]">
                  {downloadMessage}<br />Windows 10 / 11 - 64-bit
                </p>
              </div>

            </motion.div>
          </div>
        </section>

        <section className="bg-[var(--rigmd-main-surface)]">
          <div className="mx-auto max-w-7xl px-6 py-20 lg:px-8">
            <div className="mb-12 max-w-2xl">
              <p className="text-sm font-medium uppercase tracking-[0.18em] text-[var(--rigmd-accent)]">
                What RigMD provides
              </p>
              <h2 className="mt-3 text-3xl font-semibold">
                PC diagnostics without the guesswork
              </h2>
            </div>

            <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
              {features.map(([title, description, Icon], index) => (
                <motion.div
                  key={title}
                  initial={{ opacity: 0, y: 16 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true }}
                  transition={{ duration: 0.35, delay: index * 0.04 }}
                  className="rigmd-card-surface rounded-lg border p-6"
                >
                  <div className="mb-5 flex h-11 w-11 items-center justify-center rounded-lg bg-[var(--rigmd-accent-soft)]">
                    <Icon size={21} className="text-[var(--rigmd-accent)]" />
                  </div>

                  <h3 className="font-semibold">{title}</h3>

                  <p className="mt-2 text-sm leading-6 text-[var(--rigmd-text-muted)]">
                    {description}
                  </p>
                </motion.div>
              ))}
            </div>
          </div>
        </section>

      </main>

      <AnimatePresence>
        {showLogoutConfirm && (
          <motion.div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-5 backdrop-blur-sm"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => setShowLogoutConfirm(false)}
          >
            <motion.div
              role="dialog"
              aria-modal="true"
              aria-labelledby="logout-title"
              className="w-full max-w-sm rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5 shadow-2xl shadow-black/40"
              initial={{ opacity: 0, y: 18, scale: 0.96 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: 12, scale: 0.97 }}
              transition={{ duration: 0.18, ease: 'easeOut' }}
              onClick={(event) => event.stopPropagation()}
            >
              <div className="mb-4 flex items-start gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] text-[var(--rigmd-accent)]">
                  <LogOut size={19} />
                </div>

                <div>
                  <h2
                    id="logout-title"
                    className="text-lg font-semibold text-white"
                  >
                    Sign out of RigMD?
                  </h2>

                  <p className="mt-1 text-sm leading-6 text-[var(--rigmd-text-muted)]">
                    You will need to log in again before downloading the
                    installer.
                  </p>
                </div>
              </div>

              <div className="mt-5 flex gap-3">
                <button
                  type="button"
                  onClick={() => setShowLogoutConfirm(false)}
                  className="flex-1 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] px-4 py-2.5 text-sm font-semibold text-[var(--rigmd-text-soft)] transition hover:border-[var(--rigmd-accent)]/45"
                >
                  Stay signed in
                </button>

                <button
                  type="button"
                  onClick={handleSignOut}
                  className="flex-1 rounded-lg bg-[var(--rigmd-danger)] px-4 py-2.5 text-sm font-semibold text-white transition hover:brightness-110"
                >
                  Sign out
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

export default DownloadLandingPage;
