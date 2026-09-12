import { useMemo, useState } from 'react';
import { motion } from 'motion/react';
import {
  Activity,
  CheckCircle2,
  Cpu,
  Database,
  HardDrive,
  Info,
  MemoryStick,
  Monitor,
  RefreshCw,
  ShieldCheck,
  Terminal,
  type LucideIcon,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import { buttonTap, cardFadeUp, cardTransition, pageFade, pageTransition } from '../lib/motion';
import type { HardwareStats } from '../types/rigmd';
import { saveHardwareProfile } from '../services/profileService';

interface SystemProfileViewProps {
  stats: HardwareStats | null;
  error: string | null;
  hardwareUpdatedAt: Date | null;
  isRefreshingHardware: boolean;
  onRefreshHardware: () => Promise<void>;
}

interface FriendlyInfoCardProps {
  icon: LucideIcon;
  title: string;
  value: string;
  helper: string;
  tone?: 'good' | 'watch' | 'danger' | 'neutral';
}

function cleanValue(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') return 'Still checking...';
  if (String(value).toLowerCase() === 'unknown') return 'Still checking...';
  return String(value);
}

function isDetected(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') return false;
  return String(value).toLowerCase() !== 'unknown';
}

function formatLastUpdated(value: Date | null) {
  if (!value) return 'Waiting for first scan';

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(value);
}

function formatStorageSize(sizeGb: number | null | undefined) {
  if (typeof sizeGb !== 'number' || Number.isNaN(sizeGb)) return 'Still checking...';
  return sizeGb >= 1000 ? `${(sizeGb / 1024).toFixed(1)} TB` : `${sizeGb} GB`;
}

function getUsageTone(value: number | null | undefined) {
  if (typeof value !== 'number') return 'neutral';
  if (value >= 90) return 'danger';
  if (value >= 75) return 'watch';
  return 'good';
}

function getToneClasses(tone: FriendlyInfoCardProps['tone'] = 'neutral') {
  if (tone === 'good') return 'border-emerald-400/25 bg-emerald-400/10 text-emerald-300';
  if (tone === 'watch') return 'border-amber-400/25 bg-amber-400/10 text-amber-300';
  if (tone === 'danger') return 'border-red-400/25 bg-red-400/10 text-red-300';
  return 'border-cyan-400/25 bg-cyan-400/10 text-cyan-300';
}

function getPlainUsageLabel(kind: 'processor' | 'memory' | 'storage', value: number | null | undefined) {
  if (typeof value !== 'number') return 'RigMD is still checking this part.';

  if (kind === 'storage') {
    if (value >= 90) return 'Storage is almost full. This can slow down saving, loading, and updates.';
    if (value >= 75) return 'Storage is getting full. It is okay for now, but worth watching.';
    return 'Storage space looks okay.';
  }

  if (kind === 'memory') {
    if (value >= 90) return 'Memory is very busy. Closing unused apps may help.';
    if (value >= 75) return 'Memory is somewhat busy, but still usable.';
    return 'Memory use looks normal.';
  }

  if (value >= 90) return 'The processor is working very hard right now.';
  if (value >= 75) return 'The processor is busy, but not necessarily a problem.';
  return 'Processor activity looks normal.';
}

function FriendlyInfoCard({ icon: Icon, title, value, helper, tone = 'neutral' }: FriendlyInfoCardProps) {
  return (
    <motion.section
      variants={cardFadeUp}
      transition={cardTransition}
      className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5"
    >
      <div className="mb-4 flex items-start gap-4">
        <div className={`rounded-lg border p-3 ${getToneClasses(tone)}`}>
          <Icon size={22} />
        </div>

        <div className="min-w-0">
          <p className="text-sm font-semibold text-slate-400">{title}</p>
          <p className="mt-1 truncate text-lg font-bold text-white">{value}</p>
        </div>
      </div>

      <p className="text-sm leading-relaxed text-slate-400">{helper}</p>
    </motion.section>
  );
}

function SimpleStatusPanel({
  stats,
  hardwareUpdatedAt,
}: {
  stats: HardwareStats | null;
  hardwareUpdatedAt: Date | null;
}) {
  const detectedItems = useMemo(
    () => [
      stats?.cpu?.name,
      stats?.ram?.total_gb,
      stats?.disk?.total_gb,
      stats?.gpu?.name,
      stats?.os_version,
      stats?.gpu?.driver,
      stats?.system_age,
    ],
    [stats]
  );

  const detectedCount = detectedItems.filter(isDetected).length;
  const totalCount = detectedItems.length;
  const percent = Math.round((detectedCount / totalCount) * 100);

  const headline =
    percent >= 90
      ? 'RigMD found the important PC details'
      : percent >= 60
        ? 'RigMD found most PC details'
        : 'RigMD is still checking this PC';

  return (
    <motion.section
      variants={cardFadeUp}
      initial="hidden"
      animate="visible"
      transition={cardTransition}
      className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6"
    >
      <div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0">
          <div className="mb-3 flex items-center gap-2 text-cyan-300">
            <ShieldCheck size={18} />
            <span className="text-xs font-bold uppercase tracking-[0.18em]">My PC Info</span>
          </div>

          <h3 className="text-2xl font-bold text-white">{headline}</h3>

          <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-400">
            This page shows the PC parts that matter most in simple words. The deeper technical details are still saved for diagnosis, but they do not need to be front and center.
          </p>
        </div>

        <div className="shrink-0 rounded-xl border border-cyan-400/25 bg-cyan-400/10 px-5 py-4 text-center">
          <p className="text-3xl font-bold text-white">{percent}%</p>
          <p className="text-xs font-semibold text-cyan-300">{detectedCount}/{totalCount} details found</p>
        </div>
      </div>

      <div className="mt-5 h-3 w-full overflow-hidden rounded-full bg-[var(--rigmd-card-soft)]">
        <motion.div
          className="h-full rounded-full bg-cyan-400"
          initial={{ width: 0 }}
          animate={{ width: `${percent}%` }}
          transition={{ duration: 0.3, ease: 'easeOut' }}
        />
      </div>

      <p className="mt-4 text-xs text-slate-500">Last updated: {formatLastUpdated(hardwareUpdatedAt)}</p>
    </motion.section>
  );
}

export default function SystemProfileView({
  stats,
  error,
  hardwareUpdatedAt,
  isRefreshingHardware,
  onRefreshHardware,
}: SystemProfileViewProps) {
  const [isSavingProfile, setIsSavingProfile] = useState(false);
  const [saveMessage, setSaveMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const handleSaveProfile = async () => {
    if (!stats) {
      setSaveMessage({ type: 'error', text: 'PC info is not loaded yet. Please wait a moment.' });
      return;
    }

    setIsSavingProfile(true);
    setSaveMessage(null);

    try {
      const payload = {
        cpu_model: cleanValue(stats.cpu.name),
        ram_capacity: `${stats.ram.total_gb} GB`,
        storage_type: cleanValue(stats.storage_type),
        storage_capacity: `${stats.disk.total_gb} GB`,
        storage_details: stats.storage_drives ?? null,
        os_version: cleanValue(stats.os_version),
        gpu_driver: stats.gpu.driver !== 'Unknown' ? cleanValue(stats.gpu.driver) : null,
        chipset_driver: stats.chipset_driver !== 'Standard/Auto-Managed' ? cleanValue(stats.chipset_driver) : null,
        system_age: stats.system_age !== 'Unknown' ? cleanValue(stats.system_age) : null,
      };

      await saveHardwareProfile(payload);
      setSaveMessage({ type: 'success', text: 'PC info saved successfully.' });
      setTimeout(() => setSaveMessage(null), 5000);
    } catch (err) {
      const errorText = err instanceof Error ? err.message : 'Something went wrong while saving.';
      setSaveMessage({ type: 'error', text: errorText });
    } finally {
      setIsSavingProfile(false);
    }
  };

  return (
    <>
      <TopHeader
        title="My PC Info"
        subtitle="Simple summary of what RigMD found on this computer"
      />

      <motion.div
        variants={pageFade}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={pageTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8"
      >
        <div className="w-full space-y-6">
          <SimpleStatusPanel stats={stats} hardwareUpdatedAt={hardwareUpdatedAt} />

          <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
            <div className="flex items-center gap-2 text-sm text-slate-400">
              <CheckCircle2 size={16} className="text-emerald-400" />
              <span>RigMD checks this automatically.</span>
            </div>

            <div className="flex flex-wrap gap-3">
              <motion.button
                type="button"
                onClick={onRefreshHardware}
                disabled={isRefreshingHardware}
                whileTap={buttonTap}
                className="flex items-center gap-2 rounded-lg bg-cyan-400 px-5 py-2.5 text-sm font-semibold text-[#041014] transition hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <RefreshCw size={16} className={isRefreshingHardware ? 'animate-spin' : ''} />
                Check Again
              </motion.button>

              <motion.button
                type="button"
                onClick={handleSaveProfile}
                disabled={isSavingProfile || !stats}
                whileTap={buttonTap}
                className="flex items-center gap-2 rounded-lg border border-cyan-500/30 bg-cyan-500/10 px-5 py-2.5 text-sm font-semibold text-cyan-300 transition hover:bg-cyan-500/20 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <Database size={16} className={isSavingProfile ? 'animate-spin' : ''} />
                {isSavingProfile ? 'Saving...' : 'Save PC Info'}
              </motion.button>
            </div>
          </div>

          {error && (
            <div className="rounded-lg border border-red-500/25 bg-red-500/10 p-4 text-sm font-medium text-red-300">
              RigMD could not read your PC info right now. Try checking again.
            </div>
          )}

          {!stats ? (
            <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6">
              <div className="flex items-start gap-3">
                <Activity className="mt-0.5 animate-pulse text-cyan-300" size={18} />
                <div>
                  <h3 className="font-semibold text-white">Checking your PC...</h3>
                  <p className="mt-1 text-sm text-slate-400">
                    This usually takes a few seconds.
                  </p>
                </div>
              </div>
            </section>
          ) : (
            <>
              <section>
                <div className="mb-4">
                  <h3 className="text-lg font-bold text-white">Important PC Details</h3>
                  <p className="text-sm text-slate-500">These are the details most users need to understand.</p>
                </div>

                <motion.div
                  variants={cardFadeUp}
                  initial="hidden"
                  animate="visible"
                  transition={cardTransition}
                  className="grid grid-cols-1 gap-4 lg:grid-cols-2 xl:grid-cols-3"
                >
                  <FriendlyInfoCard
                    icon={Cpu}
                    title="Processor"
                    value={cleanValue(stats.cpu.name)}
                    helper={getPlainUsageLabel('processor', stats.cpu.usage_percent)}
                    tone={getUsageTone(stats.cpu.usage_percent)}
                  />

                  <FriendlyInfoCard
                    icon={MemoryStick}
                    title="Memory"
                    value={`${stats.ram.total_gb} GB total`}
                    helper={getPlainUsageLabel('memory', stats.ram.usage_percent)}
                    tone={getUsageTone(stats.ram.usage_percent)}
                  />

                  <FriendlyInfoCard
                    icon={HardDrive}
                    title="Storage"
                    value={`${formatStorageSize(stats.disk.total_gb)} ${cleanValue(stats.storage_type)}`}
                    helper={getPlainUsageLabel('storage', stats.disk.usage_percent)}
                    tone={getUsageTone(stats.disk.usage_percent)}
                  />

                  <FriendlyInfoCard
                    icon={Monitor}
                    title="Graphics"
                    value={cleanValue(stats.gpu.name)}
                    helper="This handles display, videos, visual effects, and games."
                    tone="neutral"
                  />

                  <FriendlyInfoCard
                    icon={Terminal}
                    title="Windows"
                    value={cleanValue(stats.os_version)}
                    helper="This helps RigMD understand your system environment."
                    tone="neutral"
                  />

                  <FriendlyInfoCard
                    icon={Activity}
                    title="Computer Name"
                    value={cleanValue(stats.device_name)}
                    helper="This is the PC currently connected to RigMD."
                    tone="neutral"
                  />
                </motion.div>
              </section>

              <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6">
                <div className="mb-4 flex items-start gap-3">
                  <Info className="mt-0.5 shrink-0 text-cyan-300" size={18} />
                  <div>
                    <h3 className="font-semibold text-white">More Details</h3>
                    <p className="mt-1 text-sm text-slate-400">
                      These are useful for technicians or deeper troubleshooting.
                    </p>
                  </div>
                </div>

                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                    <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">Graphics driver</p>
                    <p className="mt-1 text-sm font-semibold text-white">{cleanValue(stats.gpu.driver)}</p>
                  </div>

                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                    <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">Device driver</p>
                    <p className="mt-1 text-sm font-semibold text-white">{cleanValue(stats.chipset_driver)}</p>
                  </div>

                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                    <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">Approximate age</p>
                    <p className="mt-1 text-sm font-semibold text-white">{cleanValue(stats.system_age)}</p>
                  </div>

                  <div className="rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card-soft)] p-4">
                    <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">Storage used</p>
                    <p className="mt-1 text-sm font-semibold text-white">{stats.disk.usage_percent}% full</p>
                  </div>
                </div>
              </section>
            </>
          )}

          {saveMessage && (
            <div
              className={`rounded-lg border px-4 py-3 text-sm font-medium ${
                saveMessage.type === 'success'
                  ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
                  : 'border-red-500/30 bg-red-500/10 text-red-300'
              }`}
            >
              {saveMessage.text}
            </div>
          )}
        </div>
      </motion.div>
    </>
  );
}