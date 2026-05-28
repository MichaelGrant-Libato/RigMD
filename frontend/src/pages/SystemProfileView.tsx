//SystemProfileView.tsx

import { useMemo, useState } from 'react';
import {
  Activity,
  Calendar,
  CheckCircle2,
  Cpu,
  Database,
  Gamepad2,
  Globe2,
  HardDrive,
  Info,
  Layers,
  ListChecks,
  MemoryStick,
  Microchip,
  Monitor,
  RefreshCw,
  Terminal,
  X,
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
  onClick?: () => void;
  clickable?: boolean;
  active?: boolean;
}

type ComponentInsight =
  | 'cpu'
  | 'ram'
  | 'gpu'
  | 'gpuDriver'
  | 'storage'
  | 'os'
  | 'chipset'
  | 'age'
  | null;

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
  onClick,
  clickable = false,
  active = false,
}: HardwareInfoCardProps) {
  const cardClasses = `flex min-w-0 items-center justify-between rounded-xl border p-4 text-left transition ${
    active
      ? 'border-cyan-500/70 bg-cyan-500/10'
      : 'border-[#30363d] bg-[#161b22] hover:border-cyan-500/30'
  } ${clickable ? 'cursor-pointer hover:bg-[#1b222c]' : ''}`;

  const content = (
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
  );

  if (clickable) {
    return (
      <button type="button" onClick={onClick} className={cardClasses}>
        {content}
      </button>
    );
  }

  return <div className={cardClasses}>{content}</div>;
}

function MetricTile({
  label,
  value,
  tone = 'cyan',
}: {
  label: string;
  value: string;
  tone?: 'cyan' | 'emerald' | 'orange' | 'red';
}) {
  const color =
    tone === 'emerald'
      ? 'text-emerald-400'
      : tone === 'orange'
        ? 'text-orange-400'
        : tone === 'red'
          ? 'text-red-400'
          : 'text-cyan-400';

  return (
    <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
      <p className="text-xs uppercase tracking-wider text-gray-500">{label}</p>
      <p className={`mt-1 text-lg font-bold ${color}`}>{value}</p>
    </div>
  );
}

function TopAppsList({
  title,
  apps,
}: {
  title: string;
  apps: Array<{ name: string; process_count: number; memory_mb: number }>;
}) {
  return (
    <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
      <div className="mb-3 flex items-center gap-2">
        <ListChecks size={15} className="text-cyan-400" />
        <p className="text-xs uppercase tracking-wider text-gray-500">{title}</p>
      </div>

      {apps.length === 0 ? (
        <p className="text-sm text-gray-500">No active app workload data available.</p>
      ) : (
        <div className="space-y-2">
          {apps.slice(0, 5).map((app) => (
            <div
              key={app.name}
              className="flex items-center justify-between rounded-lg border border-[#30363d] bg-[#161b22] px-3 py-2 text-xs"
            >
              <div className="min-w-0">
                <p className="truncate text-gray-200">{app.name}</p>
                <p className="text-[11px] text-gray-500">{app.process_count} process(es)</p>
              </div>

              <span className="shrink-0 font-semibold text-cyan-400">{app.memory_mb} MB</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function ComponentWorkloadPanel({
  stats,
  selectedComponent,
  onClear,
}: {
  stats: HardwareStats | null;
  selectedComponent: ComponentInsight;
  onClear: () => void;
}) {
  if (!stats || !selectedComponent) {
    return (
      <section className="mt-5 rounded-2xl border border-[#30363d] bg-[#0d1117] p-5">
        <div className="flex items-start gap-3">
          <Info size={17} className="mt-0.5 shrink-0 text-cyan-400" />
          <div>
            <h4 className="font-semibold text-white">Component Workload Details</h4>
            <p className="mt-1 text-sm text-gray-500">
              Select CPU, RAM, GPU, Storage, or another detected component to see the live workload data related to it.
            </p>
          </div>
        </div>
      </section>
    );
  }

  const insights = stats.process_insights;
  const topApps = insights?.top_memory_apps ?? [];
  const browserMemory = insights?.browser_memory_mb ?? 0;
  const browserProcesses = insights?.browser_process_count ?? 0;
  const browserHeavy = insights?.browser_heavy ?? false;
  const gameDetected = insights?.game_detected ?? false;
  const gameProcesses = insights?.game_processes ?? [];

  const browserTone = browserHeavy ? 'orange' : 'cyan';
  const ramTone = stats.ram.usage_percent >= 85 ? 'red' : stats.ram.usage_percent >= 75 ? 'orange' : 'emerald';
  const cpuTone = stats.cpu.usage_percent >= 85 ? 'red' : stats.cpu.usage_percent >= 70 ? 'orange' : 'emerald';
  const diskTone = stats.disk.usage_percent >= 90 ? 'red' : stats.disk.usage_percent >= 80 ? 'orange' : 'emerald';

  const titleMap: Record<Exclude<ComponentInsight, null>, string> = {
    cpu: 'CPU Workload Details',
    ram: 'RAM Workload Details',
    gpu: 'GPU Workload Details',
    gpuDriver: 'GPU Driver Details',
    storage: 'Storage Details',
    os: 'Operating System Details',
    chipset: 'Chipset Driver Details',
    age: 'System Age Details',
  };

  return (
    <section className="mt-5 rounded-2xl border border-cyan-500/30 bg-[#101820] p-5">
      <div className="mb-4 flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Activity size={17} className="text-cyan-400" />
          <h3 className="font-semibold text-white">{titleMap[selectedComponent]}</h3>
        </div>

        <button
          type="button"
          onClick={onClear}
          className="rounded-lg border border-[#30363d] bg-[#0d1117] p-2 text-gray-500 transition hover:border-cyan-500/40 hover:text-white"
        >
          <X size={15} />
        </button>
      </div>

      {selectedComponent === 'cpu' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <MetricTile label="Current CPU Load" value={`${Math.round(stats.cpu.usage_percent)}%`} tone={cpuTone} />
            <MetricTile label="CPU Cores / Threads" value={`${stats.cpu.cores} / ${stats.cpu.threads}`} />
            <MetricTile label="Frequency" value={`${stats.cpu.frequency_mhz} MHz`} />
          </div>

          <TopAppsList
            title="Active workload signals"
            apps={topApps}
          />

          <p className="text-xs leading-relaxed text-gray-500">
            RigMD can see the total CPU load, but your current backend does not yet report per-process CPU usage.
            These apps are shown as workload signals because they are currently active and using memory.
          </p>
        </div>
      )}

      {selectedComponent === 'ram' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <MetricTile label="RAM Allocated" value={`${stats.ram.usage_percent}%`} tone={ramTone} />
            <MetricTile label="Used RAM" value={`${stats.ram.used_gb} GB`} tone={ramTone} />
            <MetricTile label="Total RAM" value={`${stats.ram.total_gb} GB`} />
          </div>

          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
              <div className="mb-2 flex items-center gap-2">
                <Globe2 size={16} className="text-cyan-400" />
                <p className="text-xs uppercase tracking-wider text-gray-500">Browser Memory</p>
              </div>

              <p className={`text-xl font-bold ${browserHeavy ? 'text-orange-400' : 'text-cyan-400'}`}>
                {browserMemory} MB
              </p>
              <p className="text-xs text-gray-500">{browserProcesses} browser process(es)</p>
            </div>

            <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
              <p className="text-xs uppercase tracking-wider text-gray-500">Browser Pressure</p>
              <p className={`mt-1 text-lg font-bold ${browserHeavy ? 'text-orange-400' : 'text-emerald-400'}`}>
                {browserHeavy ? 'Heavy' : 'Normal'}
              </p>
              <p className="text-xs text-gray-500">
                Many tabs, extensions, videos, and web apps can increase RAM usage.
              </p>
            </div>
          </div>

          <TopAppsList title="Apps using the most RAM" apps={topApps} />
        </div>
      )}

      {selectedComponent === 'gpu' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <MetricTile label="GPU Type" value={cleanValue(stats.gpu.type)} />
            <MetricTile label="VRAM" value={`${stats.gpu.vram_gb} GB`} />
            <MetricTile label="Game / Launcher" value={gameDetected ? 'Detected' : 'None'} tone={gameDetected ? 'orange' : 'emerald'} />
          </div>

          <div className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4">
            <div className="mb-2 flex items-center gap-2">
              <Gamepad2 size={16} className="text-orange-400" />
              <p className="text-xs uppercase tracking-wider text-gray-500">Detected Game / Launcher Processes</p>
            </div>

            <p className={`text-lg font-bold ${gameDetected ? 'text-orange-400' : 'text-emerald-400'}`}>
              {gameDetected ? 'Launcher Detected' : 'No known game or launcher process active'}
            </p>

            <p className="mt-1 text-sm text-gray-500">
              {gameProcesses.length > 0 ? gameProcesses.join(', ') : 'No detected process names to show.'}
            </p>
          </div>

          <p className="text-xs leading-relaxed text-gray-500">
            This does not always prove a full game is running. It means RigMD found a known game or launcher process that may contribute to graphics load, heat, or display-driver sensitivity.
          </p>
        </div>
      )}

      {selectedComponent === 'gpuDriver' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <MetricTile label="Detected GPU Driver" value={cleanValue(stats.gpu.driver)} />
            <MetricTile label="GPU Name" value={cleanValue(stats.gpu.name)} />
          </div>

          <p className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4 text-sm leading-relaxed text-gray-400">
            Driver problems are usually connected to flickering, black screens, visual glitches, crashes during games,
            or problems after a recent driver update. RigMD does not update, remove, or roll back drivers automatically.
          </p>
        </div>
      )}

      {selectedComponent === 'storage' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <MetricTile label="Total Usage" value={`${stats.disk.usage_percent}% Full`} tone={diskTone} />
            <MetricTile label="Total Capacity" value={`${stats.disk.total_gb} GB`} />
          </div>

          {stats.storage_drives && stats.storage_drives.length > 0 && (
            <div className="space-y-3">
              <h3 className="text-sm font-semibold text-gray-300">Detected System Information - Storage</h3>
              <div className="space-y-2">
                {stats.storage_drives.map((drive, idx) => {
                  // Determine color for type badge
                  let typeColor = "bg-gray-700/30 text-gray-300"; // Unknown
                  if (drive.type.includes("NVMe")) {
                    typeColor = "bg-blue-900/30 text-blue-300";
                  } else if (drive.type.includes("SATA")) {
                    typeColor = "bg-purple-900/30 text-purple-300";
                  } else if (drive.type.includes("HDD")) {
                    typeColor = "bg-orange-900/30 text-orange-300";
                  }
                  
                  // Format size display
                  const sizeDisplay = drive.size_gb >= 1000 
                    ? `${(drive.size_gb / 1024).toFixed(0)}TB`
                    : `${drive.size_gb}GB`;
                  
                  return (
                    <div key={idx} className="rounded-lg border border-[#30363d] bg-[#0d1117] p-4">
                      <div className="flex items-center justify-between">
                        <div className="flex-1">
                          <p className="text-xs font-mono font-semibold text-gray-400">Storage{String(idx).padStart(2, '0')}</p>
                          <p className="mt-2 text-lg font-semibold text-gray-100">{sizeDisplay} {drive.type}</p>
                          <p className="text-xs text-gray-500 mt-1">{drive.model || 'Unknown Model'}</p>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
              {stats.storage_drives.some(d => d.type === "Unknown" || d.type.includes("Unknown")) && (
                <div className="rounded-md border border-amber-500/30 bg-amber-900/10 p-3 text-xs text-amber-200">
                  <p className="font-medium">ℹ️ Drive Detection Note</p>
                  <p className="mt-1 text-amber-200/80">Some drives could not be automatically classified. 
                    Check Device Manager or Disk Management for more details about unidentified drives.</p>
                </div>
              )}
            </div>
          )}

          <p className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4 text-sm leading-relaxed text-gray-400">
            Storage pressure becomes more likely to affect performance when a drive is close to 85-90% full.
            At your current level, RigMD treats storage as a supporting signal unless the user reports file errors,
            saving problems, or loading delays.
          </p>
        </div>
      )}

      {selectedComponent === 'os' && (
        <div className="space-y-4">
          <MetricTile label="Detected Operating System" value={cleanValue(stats.os_version)} />

          <TopAppsList title="Current background app signals" apps={topApps} />

          <p className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4 text-sm leading-relaxed text-gray-400">
            Windows updates, background services, startup apps, browsers, and security tools can affect performance
            even when hardware detection looks healthy.
          </p>
        </div>
      )}

      {selectedComponent === 'chipset' && (
        <div className="space-y-4">
          <MetricTile label="Detected Chipset / Platform" value={cleanValue(stats.chipset_driver)} />

          <p className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4 text-sm leading-relaxed text-gray-400">
            Chipset and platform drivers affect how Windows communicates with motherboard devices, storage controllers,
            power states, and connected hardware. RigMD currently treats this as profile context, not an automated fix target.
          </p>
        </div>
      )}

      {selectedComponent === 'age' && (
        <div className="space-y-4">
          <MetricTile label="System Age" value={cleanValue(stats.system_age)} />

          <p className="rounded-xl border border-[#30363d] bg-[#0d1117] p-4 text-sm leading-relaxed text-gray-400">
            System age is based on the Windows install date. A newer install can still slow down if many apps, browser tabs,
            launchers, or startup items are active.
          </p>
        </div>
      )}
    </section>
  );
}

export default function SystemProfileView({
  stats,
  error,
  hardwareUpdatedAt,
  isRefreshingHardware,
  onRefreshHardware,
}: SystemProfileViewProps) {
  const [selectedComponent, setSelectedComponent] = useState<ComponentInsight>(null);

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
                    Select a detected component to view the active workload data related to that component.
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
                <>
                  <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
                    <HardwareInfoCard
                      icon={Cpu}
                      title="CPU"
                      subtitle="Real-Time"
                      value={`${cleanValue(stats.cpu.name)} (${Math.round(stats.cpu.usage_percent)}% Load)`}
                      confidence={getConfidence(stats.cpu.name)}
                      clickable
                      active={selectedComponent === 'cpu'}
                      onClick={() => setSelectedComponent('cpu')}
                    />

                    <HardwareInfoCard
                      icon={MemoryStick}
                      title="RAM"
                      subtitle="Real-Time"
                      value={`${stats.ram.total_gb} GB (${stats.ram.usage_percent}% Allocated)`}
                      confidence={getConfidence(stats.ram.total_gb)}
                      clickable
                      active={selectedComponent === 'ram'}
                      onClick={() => setSelectedComponent('ram')}
                    />

                    <HardwareInfoCard
                      icon={Monitor}
                      title="GPU"
                      subtitle={cleanValue(stats.gpu.type)}
                      value={cleanValue(stats.gpu.name)}
                      confidence={getConfidence(stats.gpu.name)}
                      clickable
                      active={selectedComponent === 'gpu'}
                      onClick={() => setSelectedComponent('gpu')}
                    />

                    <HardwareInfoCard
                      icon={Microchip}
                      title="GPU Driver"
                      value={cleanValue(stats.gpu.driver)}
                      confidence={getConfidence(stats.gpu.driver)}
                      clickable
                      active={selectedComponent === 'gpuDriver'}
                      onClick={() => setSelectedComponent('gpuDriver')}
                    />

                    {stats.storage_drives && stats.storage_drives.length > 0 ? (
                      stats.storage_drives.map((drive, idx) => {
                        const sizeDisplay = drive.size_gb >= 1000
                          ? `${(drive.size_gb / 1024).toFixed(0)}TB`
                          : `${drive.size_gb}GB`;
                        return (
                          <HardwareInfoCard
                            key={idx}
                            icon={HardDrive}
                            title={`Storage${String(idx).padStart(2, '0')}`}
                            subtitle="Real-Time"
                            value={`${sizeDisplay} ${drive.type} (${stats.disk.usage_percent}% Full)`}
                            confidence={getConfidence(drive.model)}
                            clickable
                            active={selectedComponent === 'storage'}
                            onClick={() => setSelectedComponent('storage')}
                          />
                        );
                      })
                    ) : (
                      <HardwareInfoCard
                        icon={HardDrive}
                        title="Storage"
                        subtitle="Real-Time"
                        value={`${stats.disk.total_gb} GB ${cleanValue(stats.storage_type)} (${stats.disk.usage_percent}% Full)`}
                        confidence={getConfidence(stats.disk.total_gb)}
                        clickable
                        active={selectedComponent === 'storage'}
                        onClick={() => setSelectedComponent('storage')}
                      />
                    )}

                    <HardwareInfoCard
                      icon={Terminal}
                      title="Operating System"
                      value={cleanValue(stats.os_version)}
                      confidence={getConfidence(stats.os_version)}
                      clickable
                      active={selectedComponent === 'os'}
                      onClick={() => setSelectedComponent('os')}
                    />

                    <HardwareInfoCard
                      icon={Layers}
                      title="Chipset Driver"
                      value={cleanValue(stats.chipset_driver)}
                      confidence={getConfidence(stats.chipset_driver)}
                      clickable
                      active={selectedComponent === 'chipset'}
                      onClick={() => setSelectedComponent('chipset')}
                    />

                    <HardwareInfoCard
                      icon={Calendar}
                      title="System Age"
                      subtitle="Since OS Install"
                      value={cleanValue(stats.system_age)}
                      confidence={getConfidence(stats.system_age)}
                      warningTag={!isDetected(stats.system_age) ? 'Needs Rescan' : undefined}
                      clickable
                      active={selectedComponent === 'age'}
                      onClick={() => setSelectedComponent('age')}
                    />
                  </div>

                  <ComponentWorkloadPanel
                    stats={stats}
                    selectedComponent={selectedComponent}
                    onClear={() => setSelectedComponent(null)}
                  />
                </>
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