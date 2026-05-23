import { useMemo } from 'react';
import {
  Calendar,
  CheckCircle2,
  Cpu,
  Database,
  HardDrive,
  Info,
  Layers,
  MemoryStick,
  Microchip,
  Monitor,
  RefreshCw,
  Server,
  Terminal,
  Zap,
  type LucideIcon,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';
import type { HardwareStats } from '../types/rigmd';

interface SystemProfileViewProps {
  stats: HardwareStats | null;
  error: string | null;
  hardwareUpdatedAt: Date | null;
  isRefreshingHardware: boolean;
  onRefreshHardware: () => Promise<void>;
}

interface ProfileFieldStatus {
  label: string;
  value: string | number | null | undefined;
}

interface HardwareInfoCardProps {
  icon: LucideIcon;
  title: string;
  value: string;
  confidence: 'High Confidence' | 'Medium Confidence' | 'Low Confidence';
  subtitle?: string;
  warningTag?: string;
}

function cleanValue(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') {
    return 'Detecting...';
  }

  if (String(value).toLowerCase() === 'unknown') {
    return 'Detecting...';
  }

  return String(value);
}

function isDetected(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') {
    return false;
  }

  return String(value).toLowerCase() !== 'unknown';
}

function formatLastUpdated(value: Date | null) {
  if (!value) {
    return 'Waiting for live hardware data';
  }

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(value);
}

function getConfidence(value: string | number | null | undefined): 'High Confidence' | 'Low Confidence' {
  return isDetected(value) ? 'High Confidence' : 'Low Confidence';
}

function getConfidenceStyle(confidence: HardwareInfoCardProps['confidence']) {
  if (confidence === 'High Confidence') {
    return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-400';
  }

  if (confidence === 'Medium Confidence') {
    return 'border-cyan-500/30 bg-cyan-500/10 text-cyan-400';
  }

  return 'border-orange-500/30 bg-orange-500/10 text-orange-400';
}

function ProfileCompletenessPanel({
  stats,
  hardwareUpdatedAt,
}: {
  stats: HardwareStats | null;
  hardwareUpdatedAt: Date | null;
}) {
  const fields: ProfileFieldStatus[] = useMemo(
    () => [
      { label: 'CPU', value: stats?.cpu?.name },
      { label: 'GPU', value: stats?.gpu?.name },
      { label: 'RAM', value: stats?.ram?.total_gb },
      { label: 'Storage', value: stats?.disk?.total_gb },
      { label: 'Operating System', value: stats?.os_version },
      { label: 'GPU Driver', value: stats?.gpu?.driver },
      { label: 'Chipset Driver', value: stats?.chipset_driver },
      { label: 'System Age', value: stats?.system_age },
    ],
    [stats]
  );

  const detectedCount = fields.filter((field) => isDetected(field.value)).length;
  const completeness = Math.round((detectedCount / fields.length) * 100);

  const detectionLabel =
    completeness >= 90 ? 'Detection Strong' : completeness >= 60 ? 'Detection Partial' : 'Detection Incomplete';

  const detectionColor =
    completeness >= 90 ? 'text-emerald-400' : completeness >= 60 ? 'text-orange-400' : 'text-red-400';

  return (
    <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6">
      <div className="mb-4 flex items-center justify-between gap-4">
        <div className="flex items-center gap-2">
          <span className="h-2 w-2 rounded-full bg-cyan-400" />
          <h3 className="font-semibold text-white">Profile Completeness</h3>
        </div>

        <div className="flex items-center gap-3">
          <span className={`text-sm font-semibold ${detectionColor}`}>{detectionLabel}</span>
          <span className="text-2xl font-bold text-white">{completeness}%</span>
        </div>
      </div>

      <div className="mb-4 h-3 w-full overflow-hidden rounded-full bg-[#0d1117]">
        <div
          className="h-full rounded-full bg-emerald-400 transition-all duration-500"
          style={{ width: `${completeness}%` }}
        />
      </div>

      <div className="grid grid-cols-2 gap-x-8 gap-y-3 md:grid-cols-4">
        {fields.map((field) => {
          const detected = isDetected(field.value);

          return (
            <div
              key={field.label}
              className={`flex items-center gap-2 text-sm font-medium ${
                detected ? 'text-gray-100' : 'text-gray-500'
              }`}
            >
              <CheckCircle2 size={15} className={detected ? 'text-emerald-400' : 'text-gray-600'} />
              {field.label}
            </div>
          );
        })}
      </div>

      <p className="mt-4 text-xs text-gray-500">
        {detectedCount}/{fields.length} detected fields completed
        {hardwareUpdatedAt ? ` · Last scan: ${formatLastUpdated(hardwareUpdatedAt)}` : ''}
      </p>
    </section>
  );
}

function HardwareInfoCard({
  icon: Icon,
  title,
  value,
  confidence,
  subtitle,
  warningTag,
}: HardwareInfoCardProps) {
  return (
    <div className="flex min-w-0 items-center justify-between rounded-xl border border-[#30363d] bg-[#161b22] p-4 transition hover:border-cyan-500/30">
      <div className="flex min-w-0 items-center gap-4">
        <div className="rounded-lg border border-[#30363d] bg-[#0d1117] p-3 text-cyan-400">
          <Icon size={23} />
        </div>

        <div className="min-w-0">
          <p className="mb-1 text-xs text-gray-400">
            {title}
            {subtitle && <span className="mx-1 text-[#30363d]">|</span>}
            {subtitle && <span className="text-cyan-500">{subtitle}</span>}
          </p>

          <p className="truncate text-sm font-semibold text-gray-100">{value}</p>

          <div className="mt-2 flex flex-wrap gap-2">
            <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${getConfidenceStyle(confidence)}`}>
              {confidence}
            </span>

            {warningTag && (
              <span className="rounded-full border border-orange-500/30 bg-orange-500/10 px-2 py-0.5 text-[11px] font-semibold text-orange-400">
                {warningTag}
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default function SystemProfileView({
  stats,
  error,
  hardwareUpdatedAt,
  isRefreshingHardware,
  onRefreshHardware,
}: SystemProfileViewProps) {
  return (
    <>
      <TopHeader
        title="System Profile"
        subtitle="RigMD automatically detects your desktop PC specifications and uses them as the basis for diagnostic sessions"
      />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="w-full space-y-6">
          <ProfileCompletenessPanel stats={stats} hardwareUpdatedAt={hardwareUpdatedAt} />

          <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <CheckCircle2 size={16} className="text-emerald-400" />
              <span>Last scan:</span>
              <span className="font-semibold text-white">{formatLastUpdated(hardwareUpdatedAt)}</span>
            </div>

            <div className="flex flex-wrap gap-3">
              <button
                type="button"
                onClick={onRefreshHardware}
                disabled={isRefreshingHardware}
                className="flex items-center gap-2 rounded-lg bg-cyan-400 px-5 py-2.5 text-sm font-semibold text-[#041014] transition hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <RefreshCw size={16} className={isRefreshingHardware ? 'animate-spin' : ''} />
                Run System Scan
              </button>

              <button
                type="button"
                onClick={onRefreshHardware}
                disabled={isRefreshingHardware}
                className="flex items-center gap-2 rounded-lg border border-[#30363d] bg-[#1f2937] px-5 py-2.5 text-sm font-semibold text-gray-200 transition hover:border-cyan-500/40 hover:text-cyan-400 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <RefreshCw size={16} className={isRefreshingHardware ? 'animate-spin' : ''} />
                Refresh Detected Data
              </button>
            </div>
          </div>

          <div>
            <div className="mb-4 flex items-center gap-2">
              <Zap size={17} className="text-cyan-400" />
              <h3 className="text-sm font-bold uppercase tracking-[0.18em] text-white">
                Detected System Information
              </h3>
            </div>

            <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-6">
              <div className="mb-5 flex gap-3 rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
                <Info className="mt-0.5 shrink-0 text-cyan-400" size={17} />

                <div>
                  <h4 className="font-semibold text-gray-100">Automatic Detection Active</h4>
                  <p className="mt-1 text-sm text-gray-500">
                    These values were detected from your system. Detection confidence indicates how reliable each detected value is.
                  </p>
                </div>
              </div>

              {error ? (
                <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-4 text-sm font-medium text-red-400">
                  {error}
                </div>
              ) : !stats ? (
                <div className="animate-pulse rounded-xl border border-dashed border-[#30363d] p-8 text-center font-mono tracking-widest text-gray-500">
                  ESTABLISHING WMI DATALINK...
                </div>
              ) : (
                <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
                  <HardwareInfoCard
                    icon={Cpu}
                    title="CPU"
                    subtitle="Real-Time"
                    value={`${cleanValue(stats.cpu.name)} (${Math.round(stats.cpu.usage_percent)}% Load)`}
                    confidence={getConfidence(stats.cpu.name)}
                  />

                  <HardwareInfoCard
                    icon={MemoryStick}
                    title="RAM"
                    subtitle="Real-Time"
                    value={`${stats.ram.total_gb} GB (${stats.ram.usage_percent}% Allocated)`}
                    confidence={getConfidence(stats.ram.total_gb)}
                  />

                  <HardwareInfoCard
                    icon={Monitor}
                    title="GPU"
                    subtitle={cleanValue(stats.gpu.type)}
                    value={cleanValue(stats.gpu.name)}
                    confidence={getConfidence(stats.gpu.name)}
                  />

                  <HardwareInfoCard
                    icon={Microchip}
                    title="GPU Driver"
                    value={cleanValue(stats.gpu.driver)}
                    confidence={getConfidence(stats.gpu.driver)}
                  />

                  <HardwareInfoCard
                    icon={HardDrive}
                    title="Storage"
                    subtitle="Real-Time"
                    value={`${stats.disk.total_gb} GB ${cleanValue(stats.storage_type)} (${stats.disk.usage_percent}% Full)`}
                    confidence={getConfidence(stats.disk.total_gb)}
                  />

                  <HardwareInfoCard
                    icon={Database}
                    title="Storage Type"
                    value={cleanValue(stats.storage_type)}
                    confidence={getConfidence(stats.storage_type)}
                  />

                  <HardwareInfoCard
                    icon={Terminal}
                    title="Operating System"
                    value={cleanValue(stats.os_version)}
                    confidence={getConfidence(stats.os_version)}
                  />

                  <HardwareInfoCard
                    icon={Layers}
                    title="Chipset Driver"
                    value={cleanValue(stats.chipset_driver)}
                    confidence={getConfidence(stats.chipset_driver)}
                  />

                  <HardwareInfoCard
                    icon={Calendar}
                    title="System Age"
                    subtitle="Since OS Install"
                    value={cleanValue(stats.system_age)}
                    confidence={getConfidence(stats.system_age)}
                    warningTag={!isDetected(stats.system_age) ? 'Needs Rescan' : undefined}
                  />
                </div>
              )}

              <div className="mt-5 rounded-xl border border-[#30363d] bg-[#0d1117] px-4 py-3 text-sm text-gray-500">
                RigMD automatically detects your PC profile and uses it as the basis for all diagnostic sessions. Run a system scan to refresh detected values.
              </div>

              <button
                type="button"
                className="mt-5 flex items-center gap-2 rounded-lg border border-cyan-500/30 bg-cyan-500/10 px-5 py-2.5 text-sm font-semibold text-cyan-400 transition-colors hover:bg-cyan-500/20"
              >
                <Database size={16} />
                Save Hardware Profile
              </button>
            </section>
          </div>
        </div>
      </div>
    </>
  );
}