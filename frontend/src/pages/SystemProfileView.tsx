import { useState, useEffect, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
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
  Battery,
  BatteryCharging,
  Wifi,
  Zap,
  type LucideIcon,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import { API_BASE_URL } from '../lib/api';
import { getLocalApiToken } from '../lib/localApiSession';
import { formatTemperature, selectedCpuTemperature, selectedGpuTemperature } from '../lib/temperatureTelemetry';
import { formatMeasurement, isCurrent, keepDeviceSample, latestDeviceSample, measurement, type DeviceFeed, type DeviceSample } from '../lib/deviceTelemetry';
import { REVIEW_THRESHOLDS } from '../lib/reviewThresholds';
import { storageReviewStatus } from '../lib/storageTelemetry';
import { buttonTap, cardFadeUp, cardTransition, pageFade, pageTransition } from '../lib/motion';
import type { HardwareStats } from '../types/rigmd';
import { saveHardwareProfile } from '../services/profileService';
import DeviceDetailModal, { HardwareType } from '../components/DeviceDetailModal';

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
  helperTitle?: string;
  warningLabel?: string;
  tone?: 'good' | 'watch' | 'danger' | 'neutral';
  onClick?: () => void;
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

function formatLastUpdated(value?: string | null) {
  const time = Date.parse(value ?? '');
  if (!Number.isFinite(time)) return 'Waiting for device info';

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(time);
}

function formatStorageSize(sizeGb: number | null | undefined) {
  if (typeof sizeGb !== 'number' || !Number.isFinite(sizeGb) || sizeGb <= 0) return 'Not available';
  if (sizeGb >= 1000) return `${(sizeGb / 1024).toFixed(1)} TB`;
  if (sizeGb >= 10) return `${Math.round(sizeGb)} GB`;
  return `${sizeGb.toFixed(1)} GB`;
}

function formatStorageDisplay(stats: HardwareStats): string {
  const sizeStr = formatStorageSize(stats.disk?.total_gb);
  const drives = stats.storage_drives ?? [];
  const driveCount = drives.length;
  const rawType = stats.storage_type?.trim();
  const hasKnownType =
    rawType &&
    rawType.toLowerCase() !== 'unknown' &&
    rawType.toLowerCase() !== 'not available';

  if (driveCount > 1) {
    return hasKnownType
      ? `${sizeStr} (${driveCount} Storage Drives • ${rawType})`
      : `${sizeStr} (${driveCount} Storage Drives)`;
  }

  return hasKnownType ? `${sizeStr} · ${rawType}` : sizeStr;
}

function storageSpaceLabel(space: HardwareStats['volume_space']) {
  if (!space) return 'Volume space unavailable';
  const free = space.free_gb === 0 ? 'Volumes full' : `${formatStorageSize(space.free_gb)} free`;
  return `${free} · ${formatMeasurement(space.usage_percent, '% used', 1, 'Empty')}`;
}

function getUsageTone(value: number | null | undefined, warningAt: number = REVIEW_THRESHOLDS.storage) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 'neutral';
  if (value >= warningAt) return 'watch';
  return 'good';
}

function getToneClasses(tone: FriendlyInfoCardProps['tone'] = 'neutral') {
  if (tone === 'good') return 'border-emerald-400/25 bg-emerald-400/10 text-emerald-300';
  if (tone === 'watch') return 'border-amber-400/25 bg-amber-400/10 text-amber-300';
  if (tone === 'danger') return 'border-red-400/25 bg-red-400/10 text-red-300';
  return 'border-cyan-400/25 bg-cyan-400/10 text-cyan-300';
}

function FriendlyInfoCard({ icon: Icon, title, value, helper, helperTitle, warningLabel, tone = 'neutral', onClick }: FriendlyInfoCardProps) {
  const Component = onClick ? motion.button : motion.section;
  const interactiveProps = onClick ? {
    whileHover: { scale: 1.02, backgroundColor: 'rgba(255,255,255,0.02)' },
    whileTap: { scale: 0.98 },
    onClick: onClick
  } : {};

  return (
    <Component
      variants={cardFadeUp}
      transition={cardTransition}
      {...interactiveProps}
      className={`flex flex-col text-left rounded-xl border border-[var(--rigmd-border)] bg-[var(--rigmd-card)] p-5 ${onClick ? 'cursor-pointer hover:border-cyan-500/50' : ''}`}
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

      <div className="mt-auto">
        {helper && (
          <p title={helperTitle} className="text-sm font-medium text-slate-300 mb-1">{helper}</p>
        )}
        {warningLabel && tone !== 'neutral' && tone !== 'good' && (
          <p className="mt-1 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider opacity-90">
            {warningLabel}
          </p>
        )}
      </div>
    </Component>
  );
}

export default function SystemProfileView({
  stats,
  error,
  isRefreshingHardware,
  onRefreshHardware,
}: SystemProfileViewProps) {
  const [feed, setFeed] = useState<DeviceFeed>(() => ({ samples: [], now: Date.now(), disconnected: false }));
  const hasStats = Boolean(stats);

  useEffect(() => {
    if (!hasStats) return;

    let disposed = false;
    let retryTimer: number | undefined;
    const timer = window.setInterval(() => setFeed(current => ({ ...current, now: Date.now() })), 1000);
    const hubUrl = `${API_BASE_URL}/hubs/telemetry`;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: getLocalApiToken })
      .withAutomaticReconnect()
      .build();

    connection.on('ReceiveTelemetry', (sample: DeviceSample) => {
      if (disposed) return;
      const now = Date.now();
      setFeed(current => {
        const samples = keepDeviceSample(current.samples, sample, now);
        return samples === current.samples ? current : { samples, now, disconnected: false };
      });
    });
    const markDisconnected = () => {
      if (disposed) return;
      const now = Date.now();
      setFeed(current => ({ ...current, now, disconnected: true }));
    };
    const scheduleRetry = () => {
      if (!disposed) retryTimer = window.setTimeout(start, 3000);
    };
    const start = () => {
      void connection.start().catch(() => {
        markDisconnected();
        scheduleRetry();
      });
    };
    connection.onreconnecting(markDisconnected);
    connection.onclose(() => {
      markDisconnected();
      scheduleRetry();
    });
    start();

    return () => {
      disposed = true;
      window.clearInterval(timer);
      window.clearTimeout(retryTimer);
      void connection.stop().catch(() => {});
    };
  }, [hasStats]);

  const latest = latestDeviceSample(feed);
  const waiting = feed.samples.length === 0 && !feed.disconnected;
  const cpuTemperature = selectedCpuTemperature(stats?.cpu, latest ?? (waiting ? undefined : {}), feed.now);
  const gpuTemperature = selectedGpuTemperature(stats?.gpu, latest?.gpuAdapters ?? (waiting ? undefined : []), feed.now);
  const cpuUsage = isCurrent(latest?.cpuUsageCapturedAtUtc, feed.now) ? measurement(latest?.cpuUsagePercent, 100) : null;
  const memoryUsage = measurement(latest?.ramUsagePercent, 100);
  const memoryTotal = latest?.ramTotalGb ?? stats?.ram.total_gb;
  const charge = measurement(latest ? latest.batteryChargePercent : waiting ? stats?.battery?.charge_percent : null, 100);
  const charging = latest ? latest.batteryCharging : waiting ? stats?.battery?.is_charging : null;
  const lowBattery = charge !== null && charge <= 20 && charging === false;

  const [isSavingProfile, setIsSavingProfile] = useState(false);
  const [saveMessage, setSaveMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  
  const [selectedHardwareType, setSelectedHardwareType] = useState<HardwareType | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const closeDeviceDetails = useCallback(() => setIsModalOpen(false), []);

  const handleCardClick = (type: HardwareType) => {
    setSelectedHardwareType(type);
    setIsModalOpen(true);
  };

  const handleSaveProfile = async () => {
    if (!stats) {
      setSaveMessage({ type: 'error', text: 'Device info is not loaded yet. Please wait a moment.' });
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
      setSaveMessage({ type: 'success', text: 'Device info saved successfully.' });
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
        title="My Device Info"
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
              <p className="mb-3 text-xs font-bold uppercase tracking-[0.16em] text-cyan-300">My Device Info</p>
              <h3 className="text-2xl font-bold text-white">Your Device details, at a glance</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-300">Review your hardware and Windows information. Expand Technical details for more.</p>
              {stats && [stats.cpu.name, stats.ram.total_gb, stats.disk.total_gb, stats.gpu.name, stats.os_version].some(value => !isDetected(value)) && <p className="mt-2 text-sm text-amber-300">Some hardware details are unavailable.</p>}
            </div>

            <div className="mt-5 flex flex-col gap-4 border-t border-slate-700/50 pt-4 lg:flex-row lg:items-center lg:justify-between">
              <p className="text-sm text-slate-300">{stats?.inventory_captured_at_utc ? `Device info collected ${formatLastUpdated(stats.inventory_captured_at_utc)}` : 'Waiting for device info'}</p>
              <div className="flex flex-wrap gap-3">
              <motion.button
                type="button"
                title="Collect the latest device information"
                onClick={onRefreshHardware}
                disabled={isRefreshingHardware}
                whileTap={buttonTap}
                className="flex items-center gap-2 rounded-lg bg-cyan-400 px-5 py-2.5 text-sm font-semibold text-[#041014] transition hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <RefreshCw size={16} className={isRefreshingHardware ? 'animate-spin' : ''} />
                {isRefreshingHardware ? 'Refreshing...' : 'Refresh Device Info'}
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
                {isSavingProfile ? 'Saving...' : 'Save Current Device Info'}
              </motion.button>
              </div>
            </div>
          </section>

          {error && (
            <div className="rounded-lg border border-red-500/25 bg-red-500/10 p-4 text-sm font-medium text-red-300">
              RigMD could not read your Device info right now. Try checking again.
            </div>
          )}

          {stats?.device_errors && stats.device_errors.length > 0 && (
            <div className="rounded-lg border border-red-500/25 bg-red-500/10 p-4 text-sm font-medium text-red-300">
              <h4 className="mb-1 font-bold">Warning: Hardware Errors Detected</h4>
              <ul className="list-disc pl-5">
                {stats.device_errors.map(err => (
                  <li key={err.device_id}>{err.name} (Code {err.error_code})</li>
                ))}
              </ul>
            </div>
          )}

          {!stats ? (
            <section className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6">
              <div className="flex items-start gap-3">
                <Activity className="mt-0.5 animate-pulse text-cyan-300" size={18} />
                <div>
                  <h3 className="font-semibold text-white">Device information unavailable</h3>
                  <p className="mt-1 text-sm text-slate-400">
                    Run a hardware scan to collect Device information, then reload the saved readings.
                  </p>
                </div>
              </div>
            </section>
          ) : (
            <>
              <section>
                <div className="mb-4">
                  <h3 className="text-lg font-bold text-white">Important Device Details</h3>
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
                    helper={`Temperature: ${formatTemperature(cpuTemperature?.celsius)}`}
                    helperTitle={cpuTemperature ? `${cpuTemperature.source} · ${cpuTemperature.sensorIdentifier} · ${new Date(cpuTemperature.capturedAtUtc).toLocaleTimeString()}` : 'A current CPU temperature reading could not be collected.'}
                    tone={getUsageTone(cpuUsage, 90)}
                    warningLabel="Processor is busy"
                    onClick={() => handleCardClick('CPU')}
                  />

                  <FriendlyInfoCard
                    icon={MemoryStick}
                    title="Memory"
                    value={memoryTotal && memoryTotal > 0 ? `${memoryTotal} GB total` : 'Not available'}
                    helper={memoryUsage === null ? (waiting ? 'Collecting usage…' : 'Usage unavailable') : formatMeasurement(memoryUsage, '% in use now', 1, 'Idle')}
                    tone={getUsageTone(memoryUsage, REVIEW_THRESHOLDS.memory)}
                    warningLabel="Memory usage is high"
                    onClick={() => handleCardClick('Memory')}
                  />

                  <FriendlyInfoCard
                    icon={HardDrive}
                    title="Storage"
                    value={formatStorageDisplay(stats)}
                    helper={storageSpaceLabel(stats.volume_space)}
                    tone={storageReviewStatus(stats) === 'high' ? 'watch' : storageReviewStatus(stats) === 'normal' ? 'good' : 'neutral'}
                    warningLabel={storageReviewStatus(stats) === 'high' ? 'Review volume free space' : ''}
                    onClick={() => handleCardClick('Storage')}
                  />

                  <FriendlyInfoCard
                    icon={Monitor}
                    title="Graphics"
                    value={cleanValue(stats.gpu.name)}
                    helper={`Temperature: ${formatTemperature(gpuTemperature?.celsius)}`}
                    helperTitle={gpuTemperature ? `${gpuTemperature.source} · ${new Date(gpuTemperature.capturedAtUtc).toLocaleTimeString()}` : undefined}
                    tone="neutral"
                    onClick={() => handleCardClick('GPU')}
                  />

                  <FriendlyInfoCard
                    icon={Terminal}
                    title="Windows"
                    value={cleanValue(stats.os_version).replace(/^Microsoft\s+/i, '').replace(/\s*\([\d.]+\)$/, '')}
                    tone="neutral"
                    onClick={() => handleCardClick('OS')}
                  />

                  <FriendlyInfoCard
                    icon={Activity}
                    title="Computer Name"
                    value={cleanValue(stats.device_name)}
                    tone="neutral"
                    onClick={() => handleCardClick('Device')}
                  />
                </motion.div>
              </section>

              {(stats.device_type === 'Laptop' || stats.device_type === 'Notebook') && (
                <section className="mt-2">
                  <div className="mb-4">
                    <h3 className="text-lg font-bold text-white">Laptop Health</h3>
                    <p className="text-sm text-slate-500">Live battery and network details for this portable device.</p>
                  </div>

                  <motion.div
                    variants={cardFadeUp}
                    initial="hidden"
                    animate="visible"
                    transition={cardTransition}
                    className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3"
                  >
                    {stats.battery && (
                      <FriendlyInfoCard
                        icon={charging === true ? BatteryCharging : Battery}
                        title="Battery"
                        value={formatMeasurement(charge, '%', 0, 'Empty')}
                        helper={charging == null ? undefined : charging ? 'Charging' : 'Not charging'}
                        tone={lowBattery ? 'danger' : charge === null ? 'neutral' : 'good'}
                        warningLabel={lowBattery ? 'Low battery' : ''}
                        onClick={() => handleCardClick('Battery')}
                      />
                    )}
                    {stats.network && (
                      <FriendlyInfoCard
                        icon={stats.network.is_wifi ? Wifi : Activity}
                        title="Network"
                        value={stats.network.query_succeeded === false ? 'Check unavailable' : stats.network.has_active_adapter === false ? 'No active IPv4 adapter' : stats.network.is_wifi ? 'Wi-Fi adapter' : 'Ethernet adapter'}
                        helper={stats.network.adapter_name ?? undefined}
                        tone="neutral"
                        onClick={() => handleCardClick('Network')}
                      />
                    )}
                    {stats.active_power_plan && (
                      <FriendlyInfoCard
                        icon={Zap}
                        title="Power Plan"
                        value={stats.active_power_plan}
                        tone="neutral"
                        onClick={() => handleCardClick('Power')}
                      />
                    )}
                  </motion.div>
                </section>
              )}

              <details className="rounded-xl border border-[var(--rigmd-border)] bg-[#101821] p-5">
                <summary className="cursor-pointer rounded font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-300">Technical details</summary>
                <dl className="mt-4 divide-y divide-slate-700/50">
                  {[
                    ['Processor model', cleanValue(stats.cpu.name)],
                    ['Total Storage Space', stats.storage_drives?.map(d => `${d.model} (${formatStorageSize(d.size_gb)})`).join(', ') || 'Not available'],
                    ['System Storage Breakdown', stats.storage_drives?.flatMap(d => d.volumes?.map(v => `${v.drive} (${formatStorageSize(v.total_gb)})`)).join(', ') || 'Not available'],
                    ['Windows version', cleanValue(stats.os_version)],
                    ['Active displays', formatMeasurement(stats.connected_displays)],
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

      <DeviceDetailModal
        isOpen={isModalOpen}
        onClose={closeDeviceDetails}
        hardwareType={selectedHardwareType}
        stats={stats}
        feed={feed}
      />
    </>
  );
}
