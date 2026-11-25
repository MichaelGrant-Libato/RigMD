import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { X, Cpu, MemoryStick, HardDrive, Monitor, Wifi, Activity, Battery, Zap } from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, ResponsiveContainer, CartesianGrid } from 'recharts';
import { chartReadings, formatMeasurement, isCurrent, latestDeviceSample, measurement, type DeviceFeed, type DeviceSample } from '../lib/deviceTelemetry';
import { formatTemperature, gpuTemperatureReading, selectedCpuTemperature } from '../lib/temperatureTelemetry';
import type { HardwareStats } from '../types/rigmd';
import StorageSoftwareList from './StorageSoftwareList';

export type HardwareType = 'CPU' | 'Memory' | 'Storage' | 'GPU' | 'Network' | 'OS' | 'Device' | 'Battery' | 'Power';

interface DeviceDetailModalProps {
  isOpen: boolean;
  onClose: () => void;
  hardwareType: HardwareType | null;
  stats: HardwareStats | null;
  feed?: DeviceFeed;
}

const storageViews = [
  { id: 'overview', label: 'Overview' },
  { id: 'apps', label: 'Installed apps' },
  { id: 'drivers', label: 'Drivers' },
] as const;

function text(value?: string | null) {
  return value?.trim() && !/^(unknown|n\/a|not available)$/i.test(value.trim()) ? value : 'Not available';
}

function positive(value: unknown, unit = '', digits = 0) {
  return typeof value === 'number' && value > 0 ? formatMeasurement(value, unit, digits) : 'Not available';
}

function clockSpeed(value: number | null | undefined) {
  return typeof value === 'number' && value > 0 ? positive(value / 1000, ' GHz', 2) : 'Not available';
}

function duration(seconds: number | undefined | null) {
  if (measurement(seconds) === null) return 'Not available';
  const minutes = Math.floor(seconds! / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  return [days && days + 'd', hours % 24 && hours % 24 + 'h', minutes % 60 && minutes % 60 + 'm']
    .filter(Boolean).join(' ') || 'Under a minute';
}

function timestamp(value?: string | null) {
  const time = Date.parse(value ?? '');
  return Number.isFinite(time) ? new Date(time).toLocaleTimeString() : 'Not available';
}

function findDisk(sample: DeviceSample | undefined, index?: number | null) {
  if (index == null) return undefined;
  const rows = (sample?.disks ?? []).filter(disk => Number(/^(\d+)(?:\s|$)/.exec(disk.deviceId)?.[1]) === index);
  return rows.length === 1 ? rows[0] : undefined;
}

function Details({ children }: { children: ReactNode }) {
  return <dl className="grid grid-cols-2 gap-4 md:grid-cols-4">{children}</dl>;
}

function Field({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <div className="min-w-0" title={title}>
      <dt className="text-xs text-slate-400">{label}</dt>
      <dd className="break-words text-base text-white">{value}</dd>
    </div>
  );
}

function ActivityChart({ title, label, now, color, rows, upperBound }: {
  title: string; label: string; now: number; color: string;
  rows: { time: number; value: number | null }[]; upperBound: number;
}) {
  const validCount = rows.filter(row => row.value !== null).length;
  return (
    <section aria-label={title} className="rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-4">
      <div className="mb-2 flex items-center justify-between gap-4">
        <h3 className="text-sm font-semibold text-slate-300">{title}</h3>
        <span className="text-lg font-bold text-white">{label}</span>
      </div>
      {validCount > 0 && (
        <div className="h-48 w-full" aria-hidden="true">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={rows} margin={{ top: 5, right: 3, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" vertical={false} />
              <XAxis dataKey="time" type="number" scale="time" domain={[now - 60000, now]} allowDataOverflow hide />
              <YAxis domain={[0, upperBound]} tick={{ fill: '#94a3b8', fontSize: 12 }} />
              <Line dataKey="value" type="linear" stroke={color} strokeWidth={2} connectNulls={false}
                dot={validCount === 1 ? { r: 3 } : false} isAnimationActive={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </section>
  );
}

export default function DeviceDetailModal({ isOpen, onClose, hardwareType, stats, feed }: DeviceDetailModalProps) {
  const [selectedDiskIndex, setSelectedDiskIndex] = useState(0);
  const [selectedGpuId, setSelectedGpuId] = useState('');
  const [storageView, setStorageView] = useState<(typeof storageViews)[number]['id']>('overview');
  const dialogRef = useRef<HTMLDivElement>(null);
  const availableStorage = stats?.storage_drives;

  const selectStorageDrive = useCallback((index: number) => {
    const position = availableStorage?.findIndex(drive => drive.disk_index === index) ?? -1;
    if (position >= 0) setSelectedDiskIndex(position);
  }, [availableStorage]);

  useEffect(() => {
    if (!isOpen) return;
    setSelectedDiskIndex(0);
    setSelectedGpuId('');
    setStorageView('overview');
  }, [isOpen, hardwareType]);

  useEffect(() => {
    if (!isOpen) return;
    const previousFocus = document.activeElement as HTMLElement | null;
    const dialog = dialogRef.current;
    dialog?.focus();
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); onClose(); }
      if (event.key !== 'Tab' || !dialog) return;
      const controls = [...dialog.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), select:not([disabled]), [href], summary, [tabindex="0"]')]
        .filter(control => control.getClientRects().length > 0);
      const first = controls[0];
      const last = controls[controls.length - 1];
      if (!first) { event.preventDefault(); return; }
      if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) {
        event.preventDefault(); last?.focus();
      } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialog)) {
        event.preventDefault(); first.focus();
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => { document.removeEventListener('keydown', handleKey); previousFocus?.focus(); };
  }, [isOpen, onClose]);

  if (!isOpen || !stats) return null;

  const now = feed?.now ?? Date.now();
  const samples = feed?.samples ?? [];
  const latest = latestDeviceSample(feed);
  const waiting = samples.length === 0 && !feed?.disconnected;
  const warmingUp = waiting || (samples.length === 1 && Boolean(latest));
  const liveValue = (value: unknown, unit = '', digits = 0, zero = 'Idle', ceiling?: number) =>
    measurement(value, ceiling) === null ? (warmingUp ? 'Collecting…' : 'Not available') : formatMeasurement(value, unit, digits, zero);
  const cpuUsage = isCurrent(latest?.cpuUsageCapturedAtUtc, now) ? measurement(latest?.cpuUsagePercent, 100) : null;
  const cpuTemp = selectedCpuTemperature(stats.cpu, latest ?? (waiting ? undefined : {}), now);
  const adapters = latest?.gpuAdapters ?? stats.gpu.adapters ?? [];
  const gpu = adapters.find(adapter => adapter.id === (selectedGpuId || stats.gpu.adapter_id)) ?? adapters[0];
  const gpuLive = latest?.gpuAdapters?.find(adapter => adapter.id === gpu?.id);
  const gpuUsage = isCurrent(gpuLive?.capturedAtUtc, now) ? measurement(gpuLive?.usagePercent, 100) : null;
  const gpuMemoryFresh = isCurrent(gpuLive?.memoryCapturedAtUtc, now);
  const gpuTemp = gpu && (latest || waiting) ? gpuTemperatureReading(gpu, now) : null;
  const gpuMetadataMatches = gpu?.id != null && gpu.id === stats.gpu.adapter_id &&
    (!gpu.driverVersion || gpu.driverVersion === stats.gpu.driver);
  const drives = availableStorage ?? [];
  const disk = drives[selectedDiskIndex] ?? drives[0];
  const diskSample = findDisk(latest, disk?.disk_index);
  const diskLive = isCurrent(diskSample?.capturedAtUtc, now) ? diskSample : undefined;
  const networkId = stats.network?.adapter_id?.toLowerCase();
  const selectNetwork = (sample: DeviceSample | undefined) => networkId
    ? sample?.networkAdapters?.find(adapter => adapter.id.toLowerCase() === networkId) : undefined;
  const network = selectNetwork(latest);
  const batteryCharge = latest ? latest.batteryChargePercent : waiting ? stats.battery?.charge_percent : null;
  const batteryCharging = latest ? latest.batteryCharging : waiting ? stats.battery?.is_charging : null;
  const volumeNames = disk?.volumes?.map(volume => volume.drive.replace(/\\+$/, '')).join(', ');
  const volumeTotals = disk?.volumes;
  const volumeFree = volumeTotals?.length && volumeTotals.every(volume => volume.total_gb > 0 &&
      measurement(volume.used_gb, volume.total_gb) !== null)
    ? volumeTotals.reduce((sum, volume) => sum + volume.total_gb - volume.used_gb, 0) : null;

  let Icon = Cpu;
  let title = 'Processor';
  let subtitle = text(stats.cpu.name);
  let content: ReactNode;

  if (hardwareType === 'Memory') {
    Icon = MemoryStick;
    title = 'Memory';
    subtitle = positive(stats.ram.total_gb, ' GB usable', 2);
    const used = liveValue(latest?.ramUsedGb, ' GB', 1, 'None');
    const slots = stats.ram.slots_used && stats.ram.slots_total && stats.ram.slots_used <= stats.ram.slots_total
      ? stats.ram.slots_used + ' of ' + stats.ram.slots_total : 'Not available';
    const commit = measurement(latest?.ramCommittedGb) !== null && measurement(latest?.ramCommitLimitGb) !== null
      ? formatMeasurement(latest?.ramCommittedGb, '', 1) + ' / ' + positive(latest?.ramCommitLimitGb, ' GB', 1)
      : liveValue(null);
    content = <>
      <ActivityChart title="Memory usage · Last 60 seconds" label={used} now={now} color="#a78bfa"
        upperBound={latest?.ramTotalGb ?? (stats.ram.total_gb > 0 ? stats.ram.total_gb : 1)}
        rows={chartReadings(samples, now, sample => ({ value: measurement(sample.ramUsedGb) }))} />
      <Details>
        <Field label="In use" value={used} />
        <Field label="Available" value={liveValue(latest?.ramAvailableGb, ' GB', 1, 'None')} />
        <Field label="Configured speed" value={positive(stats.ram.speed_mtps, ' MT/s')} />
        <Field label="Slots used" value={slots} />
        <Field label="Installed" value={positive(stats.ram.installed_gb, ' GB', 1)} />
        <Field label="Form factor" value={text(stats.ram.form_factor)} />
        <Field label="Hardware reserved" value={formatMeasurement(stats.ram.hardware_reserved_mb, ' MB')} />
        <Field label="Committed" value={commit} />
        <Field label="Cached" value={liveValue(latest?.ramCachedGb, ' GB', 1, 'None')} />
      </Details>
    </>;
  } else if (hardwareType === 'GPU') {
    Icon = Monitor;
    title = 'Graphics';
    subtitle = text(gpu?.name ?? stats.gpu.name);
    const utilization = liveValue(gpuUsage, '%', 1);
    const usedMemory = (used: number | null | undefined, total: number | null | undefined) => {
      const value = liveValue(gpuMemoryFresh ? used : null, ' GB', 2, 'None in use');
      return total != null && total > 0 ? value + ' / ' + positive(total, ' GB', 1) : value;
    };
    content = <>
      {adapters.length > 1 && (
        <label className="flex items-center gap-2 text-sm text-slate-300">
          Graphics adapter
          <select className="rounded-lg border border-slate-700 bg-[#101821] px-3 py-2" value={gpu?.id ?? ''}
            onChange={event => setSelectedGpuId(event.target.value)}>
            {adapters.map((adapter, index) => <option key={adapter.id} value={adapter.id}>{'GPU ' + index + ' · ' + adapter.name}</option>)}
          </select>
        </label>
      )}
      <ActivityChart title="GPU activity · Last 60 seconds" label={utilization} now={now} color="#34d399" upperBound={100}
        rows={chartReadings(samples, now, sample => {
          const adapter = sample.gpuAdapters?.find(row => row.id === gpu?.id);
          return { value: measurement(adapter?.usagePercent, 100), timestamp: adapter?.capturedAtUtc ?? null };
        })} />
      <Details>
        <Field label="Utilization" value={utilization} />
        <Field label="Dedicated memory" value={usedMemory(gpuLive?.dedicatedUsedGb, gpu?.dedicatedMemoryGb)} />
        <Field label="Shared memory" value={usedMemory(gpuLive?.sharedUsedGb, gpu?.sharedMemoryGb)} />
        <Field label="Temperature" value={formatTemperature(gpuTemp?.celsius)}
          title={gpuTemp ? gpuTemp.source + ' · ' + timestamp(gpuTemp.capturedAtUtc) : undefined} />
        <Field label="Driver version" value={text(gpu?.driverVersion ?? (gpuMetadataMatches ? stats.gpu.driver : undefined))} />
        <Field label="Driver date" value={text(gpuMetadataMatches ? stats.gpu.driver_date : undefined)} />
        <Field label="Physical location" value={text(gpuMetadataMatches ? stats.gpu.physical_location : undefined)} />
      </Details>
    </>;
  } else if (hardwareType === 'Storage') {
    Icon = HardDrive;
    title = 'Storage';
    subtitle = text(stats.storage_type);
    const active = liveValue(diskLive?.activeTimePercent, '% active', 1, 'Idle', 100);
    const diskMedia = disk?.media_type && disk.media_type !== 'Fixed hard disk media' ? disk.media_type : null;
    content = <>
      <div role="group" aria-label="Storage views" className="flex flex-wrap gap-2">
        {storageViews.map(({ id, label }) => (
          <button type="button" key={id} aria-pressed={storageView === id} onClick={() => setStorageView(id)}
            className={'rounded-lg px-3 py-2 text-sm font-medium ' + (storageView === id
              ? 'bg-cyan-400/15 text-cyan-200' : 'text-slate-400 hover:bg-slate-800 hover:text-white')}>{label}</button>
        ))}
      </div>
      {storageView === 'overview' && drives.length > 1 && (
        <div role="group" aria-label="Physical drives" className="flex flex-wrap gap-2 border-b border-[var(--rigmd-border)] pb-4">
          {drives.map((drive, index) => (
            <button type="button" key={drive.disk_index ?? 'unknown-' + index} aria-pressed={drive === disk}
              onClick={() => setSelectedDiskIndex(index)}
              className={'rounded-lg border px-3 py-2 text-xs font-semibold ' + (drive === disk
                ? 'border-amber-500/50 bg-amber-500/20 text-amber-400'
                : 'border-transparent bg-slate-800 text-slate-400 hover:bg-slate-700')}>
              {(drive.disk_index == null ? '' : 'Drive ' + drive.disk_index + ' · ') + text(drive.model)}
            </button>
          ))}
        </div>
      )}
      {storageView !== 'overview'
        ? <StorageSoftwareList key={storageView} kind={storageView} diskIndex={disk?.disk_index} onSelectDrive={selectStorageDrive} />
        : disk ? <>
          <ActivityChart title={disk.model + ' · Activity over 60 seconds'} label={active} now={now} color="#fbbf24" upperBound={100}
            rows={chartReadings(samples, now, sample => {
              const reading = findDisk(sample, disk.disk_index);
              return { value: measurement(reading?.activeTimePercent, 100), timestamp: reading?.capturedAtUtc ?? null };
            })} />
          <Details>
            <Field label="Physical capacity" value={positive(disk.size_gb, ' GB', 2)} />
            <Field label="Media" value={text(diskMedia)} />
            <Field label="Bus" value={text(disk.bus_type)} />
            <Field label="Volumes" value={text(volumeNames)} />
            <Field label="Read speed" value={liveValue(diskLive?.readKbps, ' KB/s', 1)} />
            <Field label="Write speed" value={liveValue(diskLive?.writeKbps, ' KB/s', 1)} />
            <Field label="Average response time" value={diskLive?.hasTransfers === false ? 'No transfers' : liveValue(diskLive?.responseTimeMs, ' ms', 2, '<0.01 ms')} />
            <Field label="Volume space used at collection" value={formatMeasurement(disk.usage_percent, '%', 1, 'Empty')} />
            <Field label="Volume free space" value={formatMeasurement(volumeFree, ' GB', 1, 'Full')} />
            <Field label="Activity measured" value={timestamp(diskLive?.capturedAtUtc)} />
          </Details>
        </> : <p className="text-sm text-slate-400">Storage devices are unavailable.</p>}
    </>;
  } else if (hardwareType === 'Network') {
    Icon = Wifi;
    title = 'Network';
    subtitle = text(stats.network?.adapter_name);
    const rows = chartReadings(samples, now, sample => ({ value: measurement(selectNetwork(sample)?.receiveKbps) }));
    content = <>
      <ActivityChart title="Receive speed · Last 60 seconds" label={liveValue(network?.receiveKbps, ' Kbps', 1)}
        now={now} color="#f472b6" upperBound={Math.max(1, ...rows.map(row => row.value ?? 0))} rows={rows} />
      <Details>
        <Field label="Send speed" value={liveValue(network?.sendKbps, ' Kbps', 1)} />
        <Field label="Receive speed" value={liveValue(network?.receiveKbps, ' Kbps', 1)} />
        <Field label="Link speed" value={positive(network?.linkMbps, ' Mbps', 1)} />
        <Field label="IP address" value={text(stats.network?.ip_address)} />
        <Field label="Internet latency at last check" value={formatMeasurement(stats.network?.ping_latency_ms, ' ms', 0, '<1 ms')} />
      </Details>
    </>;
  } else if (hardwareType === 'OS' || hardwareType === 'Device') {
    Icon = Activity;
    title = hardwareType === 'OS' ? 'Windows' : 'Computer Name';
    subtitle = text(hardwareType === 'OS' ? stats.os_version : stats.device_name);
    content = <Details>
      <Field label="Computer name" value={text(stats.device_name)} />
      <Field label="Windows edition and build" value={text(stats.os_version)} />
      <Field label="Architecture" value={text(stats.os_architecture)} />
      <Field label="Device type" value={text(stats.device_type)} />
      <Field label="Uptime" value={duration(latest?.uptimeSeconds)} />
      <Field label="Active displays" value={formatMeasurement(stats.connected_displays)} />
      {hardwareType === 'Device' && <Field label="Motherboard" value={text(stats.chipset_driver)} />}
    </Details>;
  } else if (hardwareType === 'Battery' || hardwareType === 'Power') {
    Icon = hardwareType === 'Battery' ? Battery : Zap;
    title = hardwareType === 'Battery' ? 'Battery' : 'Power Plan';
    subtitle = hardwareType === 'Battery' ? text(stats.device_name) : text(stats.active_power_plan);
    content = <Details>
      <Field label="Windows power plan" value={text(stats.active_power_plan)} />
      {hardwareType === 'Battery' && <>
        <Field label="Charge remaining" value={liveValue(batteryCharge, '%', 0, 'Empty', 100)} />
        <Field label="Charging" value={batteryCharging == null ? 'Not available' : batteryCharging ? 'Yes' : 'No'} />
        <Field label="Battery health" value={text(stats.battery?.health_status)} />
      </>}
    </Details>;
  } else {
    const utilization = liveValue(cpuUsage, '%', 1);
    content = <>
      <ActivityChart title="CPU activity · Last 60 seconds" label={utilization} now={now} color="#22d3ee" upperBound={100}
        rows={chartReadings(samples, now, sample => ({
          value: measurement(sample.cpuUsagePercent, 100), timestamp: sample.cpuUsageCapturedAtUtc ?? null
        }))} />
      <Details>
        <Field label="Utilization" value={utilization} />
        <Field label="Windows clock reading" value={clockSpeed(latest?.cpuSpeedMhz)} title="Windows Processor Frequency counter" />
        <Field label="Processes" value={positive(latest?.cpuProcesses)} />
        <Field label="System threads" value={positive(latest?.cpuThreads)} />
        <Field label="Handles" value={positive(latest?.cpuHandles)} />
        <Field label="Reported maximum clock" value={clockSpeed(stats.cpu.max_frequency_mhz)} />
        <Field label="Temperature" value={formatTemperature(cpuTemp?.celsius)}
          title={cpuTemp ? cpuTemp.source + ' · ' + cpuTemp.sensorIdentifier + ' · ' + timestamp(cpuTemp.capturedAtUtc) : undefined} />
        <Field label="Sockets" value={positive(stats.cpu.sockets)} />
        <Field label="Cores" value={positive(stats.cpu.cores)} />
        <Field label="Logical processors" value={positive(stats.cpu.threads)} />
        <Field label="Firmware virtualization" value={stats.cpu.virtualization_enabled == null
          ? 'Not available' : stats.cpu.virtualization_enabled ? 'Enabled' : 'Disabled'} />
        <Field label="L1 cache" value={positive(stats.cpu.l1_cache_kb, ' KB')} />
        <Field label="L2 cache" value={positive(stats.cpu.l2_cache_mb, ' MB', 1)} />
        <Field label="L3 cache" value={positive(stats.cpu.l3_cache_mb, ' MB', 1)} />
        <Field label="Uptime" value={duration(latest?.uptimeSeconds)} />
      </Details>
    </>;
  }

  return (
    <AnimatePresence>
      <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm" onClick={onClose}>
        <motion.div ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby="device-detail-title" tabIndex={-1}
          initial={{ y: 50, opacity: 0, scale: 0.95 }} animate={{ y: 0, opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.95 }} transition={{ type: 'spring', bounce: 0, duration: 0.4 }}
          className="flex max-h-[90vh] w-full max-w-4xl flex-col overflow-hidden rounded-xl border border-[var(--rigmd-border)] bg-[#0b1118] shadow-2xl"
          onClick={event => event.stopPropagation()}>
          <div className="flex items-center justify-between border-b border-[var(--rigmd-border)] p-5">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg border border-cyan-400/25 bg-cyan-400/10 text-cyan-300"><Icon size={24} /></div>
              <div className="min-w-0">
                <h2 id="device-detail-title" className="text-2xl font-bold text-white">{title}</h2>
                <p className="break-words text-sm text-slate-400">{subtitle}</p>
              </div>
            </div>
            <button type="button" aria-label="Close device details" onClick={onClose}
              className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-800 hover:text-white"><X size={24} /></button>
          </div>
          <div className="custom-scrollbar space-y-5 overflow-y-auto p-6">{content}</div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
