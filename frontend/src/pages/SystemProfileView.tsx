import { useState } from 'react';
import { motion } from 'motion/react';
import {
  Activity,
  Cpu,
  Database,
  HardDrive,
  MemoryStick,
  Monitor,
  RefreshCw,
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
  helper?: string;
  warningLabel?: string;
  tone?: 'good' | 'watch' | 'danger' | 'neutral';
}

function cleanValue(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') return 'Not available';
  if (String(value).toLowerCase() === 'unknown') return 'Not available';
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
  if (typeof sizeGb !== 'number' || Number.isNaN(sizeGb)) return 'Not available';
  return sizeGb >= 1000 ? `${(sizeGb / 1024).toFixed(1)} TB` : `${sizeGb} GB`;
}

function getUsageTone(value: number | null | undefined) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 'neutral';
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

function usageLabel(value: number | null | undefined) {
  return typeof value === 'number' && Number.isFinite(value)
    ? `${value.toFixed(1)}% in use during last scan`
    : 'Usage unavailable';
}

function FriendlyInfoCard({ icon: Icon, title, value, helper, warningLabel, tone = 'neutral' }: FriendlyInfoCardProps) {
  return (
    <motion.section
      variants={cardFadeUp}
      transition={cardTransition}
      className="flex flex-col rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5"
    >
      <div className="mb-4 flex items-start gap-4">
        <div className={`shrink-0 rounded-lg border p-3 ${getToneClasses()}`}>
          <Icon size={22} />
        </div>

        <div className="min-w-0">
          <p className="text-sm font-semibold text-slate-400">{title}</p>
          <p className="mt-1 break-words text-lg font-bold text-white">{value}</p>
        </div>
      </div>

      {helper && (
        <div className="mt-auto flex flex-wrap items-center gap-x-3 gap-y-2">
          <p className="text-sm leading-relaxed text-slate-300">{helper}</p>
          {warningLabel && (tone === 'watch' || tone === 'danger') && (
            <span className={`rounded-full border px-2.5 py-1 text-xs font-semibold ${getToneClasses(tone)}`}>
              {warningLabel}
            </span>
          )}
        </div>
      )}
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
          <section className="rounded-xl border border-[var(--rigmd-border)] bg-[#101821] p-6">
            <div className="min-w-0">
              <p className="mb-3 text-xs font-bold uppercase tracking-[0.16em] text-cyan-300">My PC Info</p>
              <h3 className="text-2xl font-bold text-white">Your PC details, at a glance</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-300">Review your hardware and Windows information. Expand Technical details for more.</p>
              {stats && [stats.cpu.name, stats.ram.total_gb, stats.disk.total_gb, stats.gpu.name, stats.os_version].some(value => !isDetected(value)) && <p className="mt-2 text-sm text-amber-300">Some hardware details are unavailable.</p>}
            </div>

            <div className="mt-5 flex flex-col gap-4 border-t border-slate-700/50 pt-4 lg:flex-row lg:items-center lg:justify-between">
              <p className="text-sm text-slate-300">Last collected: {formatLastUpdated(hardwareUpdatedAt)}</p>
              <div className="flex flex-wrap gap-3">
              <motion.button
                type="button"
                title="Retrieve the latest saved readings without starting a new hardware scan"
                onClick={onRefreshHardware}
                disabled={isRefreshingHardware}
                whileTap={buttonTap}
                className="flex items-center gap-2 rounded-lg bg-cyan-400 px-5 py-2.5 text-sm font-semibold text-[#041014] transition hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <RefreshCw size={16} className={isRefreshingHardware ? 'animate-spin' : ''} />
                {isRefreshingHardware ? 'Refreshing...' : 'Refresh PC Info'}
              </motion.button>

              <motion.button
                type="button"
                title="Store a hardware profile for your records"
                onClick={handleSaveProfile}
                disabled={isSavingProfile || !stats}
                whileTap={buttonTap}
                className="flex items-center gap-2 rounded-lg border border-cyan-500/30 bg-cyan-500/10 px-5 py-2.5 text-sm font-semibold text-cyan-300 transition hover:bg-cyan-500/20 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <Database size={16} className={isSavingProfile ? 'animate-spin' : ''} />
                {isSavingProfile ? 'Saving...' : 'Save Current PC Info'}
              </motion.button>
              </div>
            </div>
          </section>

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
                  <h3 className="font-semibold text-white">PC information unavailable</h3>
                  <p className="mt-1 text-sm text-slate-400">
                    Run a hardware scan to collect PC information, then reload the saved readings.
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
                  className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3"
                >
                  <FriendlyInfoCard
                    icon={Cpu}
                    title="Processor"
                    value={cleanValue(stats.cpu.name).replace(/\s+\d+-Core Processor$/i, '')}
                    helper={usageLabel(stats.cpu.usage_percent)}
                    tone={getUsageTone(stats.cpu.usage_percent)}
                    warningLabel={stats.cpu.usage_percent >= 90 ? 'Processor was very busy' : 'Processor was busy'}
                  />

                  <FriendlyInfoCard
                    icon={MemoryStick}
                    title="Memory"
                    value={`${stats.ram.total_gb} GB total`}
                    helper={usageLabel(stats.ram.usage_percent)}
                    tone={getUsageTone(stats.ram.usage_percent)}
                    warningLabel={stats.ram.usage_percent >= 90 ? 'Memory was very high' : 'Memory was high'}
                  />

                  <FriendlyInfoCard
                    icon={HardDrive}
                    title="Storage"
                    value={`${formatStorageSize(stats.disk.total_gb)} ${cleanValue(stats.storage_type)}`}
                    helper={usageLabel(stats.disk.usage_percent)}
                    tone={getUsageTone(stats.disk.usage_percent)}
                    warningLabel={stats.disk.usage_percent >= 90 ? 'Storage is almost full' : 'Storage is getting full'}
                  />

                  <FriendlyInfoCard
                    icon={Monitor}
                    title="Graphics"
                    value={cleanValue(stats.gpu.name)}
                    tone="neutral"
                  />

                  <FriendlyInfoCard
                    icon={Terminal}
                    title="Windows"
                    value={cleanValue(stats.os_version).replace(/^Microsoft\s+/i, '').replace(/\s*\([\d.]+\)$/, '')}
                    tone="neutral"
                  />

                  <FriendlyInfoCard
                    icon={Activity}
                    title="Computer Name"
                    value={cleanValue(stats.device_name)}
                    tone="neutral"
                  />
                </motion.div>
              </section>

              <details className="rounded-xl border border-[var(--rigmd-border)] bg-[#101821] p-5">
                <summary className="cursor-pointer rounded font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-300">Technical details</summary>
                <dl className="mt-4 divide-y divide-slate-700/50">
                  {[
                    ['Processor model', cleanValue(stats.cpu.name)],
                    ['Windows version', cleanValue(stats.os_version)],
                    ['Graphics driver', cleanValue(stats.gpu.driver)],
                    ['Motherboard model', stats.chipset_driver === 'Standard/Auto-Managed' ? 'Not available' : cleanValue(stats.chipset_driver).replace(/\s*\(Auto-Managed\)$/, '')],
                    ['Time since Windows installation (estimate)', cleanValue(stats.system_age).replace(/^~/, 'About ').replace(/\b1 years\b/, '1 year')],
                  ].map(([label, value]) => (
                    <div key={label} className="grid gap-1 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)] sm:gap-5">
                      <dt className="text-sm text-slate-300">{label}</dt>
                      <dd className="break-words text-sm font-medium text-white">{value}</dd>
                    </div>
                  ))}
                </dl>
              </details>
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
