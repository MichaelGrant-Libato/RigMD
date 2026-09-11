import { useEffect, useState } from 'react';
import {
  Activity,
  CheckCircle2,
  Download,
  History,
  Loader2,
  LogOut,
  Monitor,
  ShieldCheck,
  TriangleAlert,
  Wrench,
} from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import { useNavigate } from 'react-router-dom';

import { supabase } from '../lib/supabase';

const DOWNLOAD_BUCKET =
  import.meta.env.VITE_SUPABASE_DOWNLOAD_BUCKET?.trim() ?? '';
const INSTALLER_PATH =
  import.meta.env.VITE_SUPABASE_INSTALLER_PATH?.trim() ?? '';
const INSTALLER_FILE_NAME =
  import.meta.env.VITE_RIGMD_INSTALLER_FILE_NAME?.trim() ??
  'RigMD-Setup.exe';

const RELEASE_DOWNLOAD_URL =
  import.meta.env.VITE_RIGMD_DOWNLOAD_URL?.trim() ?? '';

const SIGNED_URL_TTL_SECONDS = 60;
const hasSecureDownloadConfig = Boolean(DOWNLOAD_BUCKET && INSTALLER_PATH);

const INSTALLER_SHA256 =
  '98EE5E498EEA95C50A8498F043D5F93B52F9336F6326D087096A05C2B8C64008';

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
    'Connects to a local Windows service after installation.',
    ShieldCheck,
  ],
];

function DownloadLandingPage() {
  const navigate = useNavigate();
  const [userEmail, setUserEmail] = useState('');
  const [isPreparingDownload, setIsPreparingDownload] = useState(false);
  const [downloadError, setDownloadError] = useState('');
  const [showLogoutConfirm, setShowLogoutConfirm] = useState(false);

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

  const handleDownload = async () => {
    setDownloadError('');

    if (!supabase) {
      setDownloadError('Supabase authentication is not configured yet.');
      return;
    }

    setIsPreparingDownload(true);

    try {
      let downloadUrl = RELEASE_DOWNLOAD_URL;

      if (hasSecureDownloadConfig) {
        try {
          const { data, error } = await supabase.storage
            .from(DOWNLOAD_BUCKET)
            .createSignedUrl(INSTALLER_PATH, SIGNED_URL_TTL_SECONDS, {
              download: INSTALLER_FILE_NAME,
            });

          if (error) throw error;

          downloadUrl = data.signedUrl;
        } catch (error) {
          if (!RELEASE_DOWNLOAD_URL) throw error;
        }
      }

      if (!downloadUrl) {
        throw new Error('Secure installer storage is not configured yet.');
      }

      window.location.href = downloadUrl;
    } catch (error) {
      setDownloadError(
        error instanceof Error
          ? error.message
          : 'Unable to prepare the installer download.'
      );
    } finally {
      setIsPreparingDownload(false);
    }
  };

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
                RigMD v0.1.0 for verified Windows users
              </div>

              <h1 className="text-5xl font-bold tracking-tight text-white sm:text-6xl lg:text-7xl">
                Diagnose your PC with clearer evidence.
              </h1>

              <p className="mt-7 max-w-xl text-base leading-7 text-[var(--rigmd-text-muted)] sm:text-lg">
                RigMD helps analyze live system data, identify probable issues,
                and guide safer next steps before a problem gets worse.
              </p>

              <div className="mt-10 flex flex-col gap-4 sm:flex-row sm:items-center">
                <motion.button
                  whileHover={{ y: -2 }}
                  whileTap={{ scale: 0.98 }}
                  onClick={handleDownload}
                  disabled={isPreparingDownload}
                  className="inline-flex items-center justify-center gap-3 rounded-lg bg-[var(--rigmd-accent)] px-8 py-4 font-semibold text-slate-950 shadow-lg shadow-black/20 transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-70"
                >
                  {isPreparingDownload ? (
                    <Loader2 size={20} className="animate-spin" />
                  ) : (
                    <Download size={20} />
                  )}

                  {isPreparingDownload
                    ? 'Preparing secure link'
                    : 'Download RigMD'}
                </motion.button>

                <p className="text-sm text-[var(--rigmd-text-faint)]">
                  Windows 10 / 11 - 64-bit
                </p>
              </div>

              {downloadError && (
                <p className="mt-5 max-w-xl rounded-lg border border-[var(--rigmd-danger)]/35 bg-[var(--rigmd-danger-soft)] px-4 py-3 text-sm text-[var(--rigmd-text-soft)]">
                  {downloadError}
                </p>
              )}
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

        <section>
          <div className="mx-auto max-w-4xl px-6 py-20 lg:px-8">
            <motion.div
              initial={{ opacity: 0, y: 18 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              transition={{ duration: 0.35 }}
              className="rigmd-glass rounded-lg border p-7 sm:p-9"
            >
              <div className="flex gap-4">
                <TriangleAlert
                  size={24}
                  className="mt-1 shrink-0 text-[var(--rigmd-warning)]"
                />

                <div>
                  <h2 className="font-semibold text-white">
                    About the current release
                  </h2>

                  <p className="mt-2 text-sm leading-6 text-[var(--rigmd-text-muted)]">
                    RigMD v0.1.0 is an academic capstone release. Windows
                    SmartScreen may display an unrecognized application warning
                    because the installer is not yet digitally signed.
                  </p>

                  <p className="mt-3 text-sm leading-6 text-[var(--rigmd-text-muted)]">
                    Do not disable Windows security features globally when
                    installing RigMD.
                  </p>

                  <div className="mt-5 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)]/70 p-4">
                    <p className="text-sm font-semibold text-white">
                      Installer checksum
                    </p>

                    <p className="mt-1 text-sm leading-6 text-[var(--rigmd-text-muted)]">
                      Use this SHA-256 hash to verify the downloaded installer:
                    </p>

                    <code className="mt-3 block break-all rounded-lg border border-[var(--rigmd-border-soft)] bg-black/25 px-4 py-3 text-xs leading-5 text-[var(--rigmd-text-soft)]">
                      {INSTALLER_SHA256}
                    </code>
                  </div>

                  {!hasSecureDownloadConfig && (
                    <p className="mt-4 text-sm leading-6 text-[var(--rigmd-warning)]">
                    RigMD is currently provided as a verified-access capstone release. Only
                    install it from the official RigMD download page.
                    </p>
                  )}
                </div>
              </div>
            </motion.div>
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