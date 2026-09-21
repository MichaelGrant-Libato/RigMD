import { useEffect, useState, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { X, Cpu, MemoryStick, HardDrive, Monitor, Wifi, Activity } from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, ResponsiveContainer, CartesianGrid } from 'recharts';
import * as signalR from '@microsoft/signalr';
import { API_BASE_URL } from '../lib/api';
import { findDiskReading, validReading, formatDiskRate } from '../lib/storageTelemetry';
import type { HardwareStats } from '../types/rigmd';

export type HardwareType = 'CPU' | 'Memory' | 'Storage' | 'GPU' | 'Network' | 'OS';

interface DeviceDetailModalProps {
  isOpen: boolean;
  onClose: () => void;
  hardwareType: HardwareType | null;
  stats: HardwareStats | null;
}

interface DiskTelemetryPoint {
  DeviceId: string;
  ActiveTimePercent: number;
  ReadKbps: number;
  WriteKbps: number;
}

interface TelemetryPoint {
  time: string;
  CpuUsagePercent: number;
  RamUsagePercent: number;
  GpuUsagePercent: number;
  NetworkSendKbps: number | null;
  NetworkReceiveKbps: number | null;
  CpuSpeedMhz: number;
  RamUsedGb: number;
  GpuMemoryUsedGb: number;
  CpuTempCelsius?: number;
  CpuProcesses?: number;
  CpuThreads?: number;
  CpuHandles?: number;
  GpuTempCelsius?: number;
  GpuMemoryTotalGb?: number;
  Disks: DiskTelemetryPoint[];
}

interface RawTelemetryPayload {
  cpuUsagePercent?: number;
  ramUsagePercent?: number;
  gpuUsagePercent?: number;
  networkSendKbps?: number;
  networkReceiveKbps?: number;
  cpuSpeedMhz?: number;
  ramUsedGb?: number;
  gpuMemoryUsedGb?: number;
  cpuTempCelsius?: number;
  cpuProcesses?: number;
  cpuThreads?: number;
  cpuHandles?: number;
  gpuTempCelsius?: number;
  gpuMemoryTotalGb?: number;
  disks?: Array<{
    deviceId: string;
    activeTimePercent: number;
    readKbps: number;
    writeKbps: number;
  }>;
}

function formatGb(value: number | null | undefined) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 'Not available';
  return value >= 10 ? `${Math.round(value)} GB` : `${value.toFixed(1)} GB`;
}

function formatTelemetryValue(value: unknown, unit: string) {
  return typeof value === 'number' && Number.isFinite(value)
    ? `${value.toFixed(unit === 'Kbps' ? 1 : 0)} ${unit}`
    : 'Not available';
}

export default function DeviceDetailModal({ isOpen, onClose, hardwareType, stats }: DeviceDetailModalProps) {
  const [data, setData] = useState<TelemetryPoint[]>([]);
  const [selectedDiskIndex, setSelectedDiskIndex] = useState(0);
  const dialogRef = useRef<HTMLDivElement>(null);
  const [lastReadingAt, setLastReadingAt] = useState<number | null>(null);
  const [clock, setClock] = useState(Date.now());
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    setData([]);
    setLastReadingAt(null);
    setSelectedDiskIndex(0);
    let disposed = false;
    const timer = window.setInterval(() => setClock(Date.now()), 1000);

    const connectSignalR = async () => {
      const hubUrl = `${API_BASE_URL}/hubs/telemetry`;
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

      connectionRef.current = connection;
      connection.on('ReceiveTelemetry', (telemetry: RawTelemetryPayload) => {
        if (disposed) return;
        setLastReadingAt(Date.now());
        setData((prevData) => {
          const newData = [...prevData.slice(-59)];
          newData.push({
            time: new Date().toLocaleTimeString(),
            CpuUsagePercent: telemetry.cpuUsagePercent || 0,
            RamUsagePercent: telemetry.ramUsagePercent || 0,
            GpuUsagePercent: telemetry.gpuUsagePercent || 0,
            NetworkSendKbps: typeof telemetry.networkSendKbps === 'number' ? telemetry.networkSendKbps : null,
            NetworkReceiveKbps: typeof telemetry.networkReceiveKbps === 'number' ? telemetry.networkReceiveKbps : null,
            CpuSpeedMhz: telemetry.cpuSpeedMhz || 0,
            RamUsedGb: telemetry.ramUsedGb || 0,
            GpuMemoryUsedGb: telemetry.gpuMemoryUsedGb || 0,
            CpuTempCelsius: telemetry.cpuTempCelsius,
            CpuProcesses: telemetry.cpuProcesses,
            CpuThreads: telemetry.cpuThreads,
            CpuHandles: telemetry.cpuHandles,
            GpuTempCelsius: telemetry.gpuTempCelsius,
            GpuMemoryTotalGb: telemetry.gpuMemoryTotalGb,
            Disks: (telemetry.disks || []).map((d) => ({
              DeviceId: d.deviceId,
              ActiveTimePercent: d.activeTimePercent,
              ReadKbps: d.readKbps,
              WriteKbps: d.writeKbps
            })),
          });
          return newData;
        });
      });

      try {
        await connection.start();
        if (disposed) await connection.stop();
      } catch (err) {
        console.error('SignalR Connection Error: ', err);
      }
    };

    connectSignalR();

    return () => {
      disposed = true;
      window.clearInterval(timer);
      if (connectionRef.current) {
        connectionRef.current.stop();
      }
    };
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    const previousFocus = document.activeElement as HTMLElement | null;
    const dialog = dialogRef.current;
    dialog?.focus();
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); onClose(); }
      if (event.key !== 'Tab' || !dialog) return;
      const controls = [...dialog.querySelectorAll<HTMLElement>('button:not([disabled]), [href], summary, [tabindex="0"]')];
      const first = controls[0];
      const last = controls[controls.length - 1];
      if (!first) { event.preventDefault(); return; }
      if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialog)) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', handleKey);
    return () => { document.removeEventListener('keydown', handleKey); previousFocus?.focus(); };
  }, [isOpen, onClose]);

  if (!isOpen || !stats) return null;

  // Determine which metric to show on chart based on hardware type
  let dataKey = 'CpuUsagePercent';
  let strokeColor = '#22d3ee'; // cyan-400
  let chartTitle = 'Activity during the last 60 readings';
  let chartYDomain = [0, 100];
  let Icon = Cpu;
  let title = 'Processor';
  let subtitle = stats.cpu.name;

  if (hardwareType === 'Memory') {
    dataKey = 'RamUsedGb';
    strokeColor = '#a78bfa'; // violet-400 (different color for RAM)
    chartTitle = 'Memory usage (GB)';
    chartYDomain = [0, stats.ram.total_gb || 100];
    Icon = MemoryStick;
    title = 'Memory';
    subtitle = `${stats.ram.total_gb} GB`;
  } else if (hardwareType === 'GPU') {
    dataKey = 'GpuUsagePercent';
    strokeColor = '#34d399'; // emerald-400
    Icon = Monitor;
    title = 'Graphics';
    subtitle = stats.gpu.name;
  } else if (hardwareType === 'Storage') {
    strokeColor = '#fbbf24'; // amber-400
    Icon = HardDrive;
    title = 'Storage';
    subtitle = stats.storage_type;
  } else if (hardwareType === 'Network') {
    dataKey = 'NetworkReceiveKbps';
    strokeColor = '#f472b6'; // pink-400
    Icon = Wifi;
    title = 'Network';
    subtitle = stats.network?.is_wifi ? 'Wi-Fi' : 'Ethernet';
    chartTitle = 'Network receive speed (Kbps)';
    chartYDomain = [0, Math.max(100, ...data.map((point) => point.NetworkReceiveKbps ?? 0))];
  } else if (hardwareType === 'OS') {
    Icon = Activity;
    title = 'Windows';
    subtitle = stats.os_version;
  }

  const latestData = data[data.length - 1];

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
        onClick={onClose}
      >
        <motion.div
          ref={dialogRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby="device-detail-title"
          tabIndex={-1}
          initial={{ y: 50, opacity: 0, scale: 0.95 }}
          animate={{ y: 0, opacity: 1, scale: 1 }}
          exit={{ y: 20, opacity: 0, scale: 0.95 }}
          transition={{ type: 'spring', bounce: 0, duration: 0.4 }}
          className="flex max-h-[90vh] w-full max-w-4xl flex-col overflow-hidden rounded-xl border border-[var(--rigmd-border)] bg-[#0b1118] shadow-2xl"
          onClick={(e) => e.stopPropagation()}
        >
          {/* Header */}
          <div className="flex items-center justify-between border-b border-[var(--rigmd-border)] p-5">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg border border-cyan-400/25 bg-cyan-400/10 text-cyan-300">
                <Icon size={24} />
              </div>
              <div>
                <h2 id="device-detail-title" className="text-2xl font-bold text-white">{title}</h2>
                <p className="text-sm text-slate-400">{subtitle}</p>
              </div>
            </div>
            <button
              type="button"
              aria-label="Close device details"
              onClick={onClose}
              className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-800 hover:text-white"
            >
              <X size={24} />
            </button>
          </div>

          {/* Content */}
          <div className="custom-scrollbar overflow-y-auto p-6">
            
            {hardwareType === 'Storage' && stats.storage_drives && stats.storage_drives.length > 0 && (
              <div className="mb-6 flex flex-col gap-4">
                {/* Disk Selector Tabs */}
                {stats.storage_drives.length > 1 && (
                  <div className="flex flex-wrap gap-2 mb-2 border-b border-[var(--rigmd-border)] pb-4">
                    {stats.storage_drives.map((disk, idx) => (
                      <button
                        type="button"
                        aria-pressed={selectedDiskIndex === idx}
                        key={idx}
                        onClick={() => setSelectedDiskIndex(idx)}
                        className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
                          selectedDiskIndex === idx 
                            ? 'bg-amber-500/20 text-amber-400 border border-amber-500/50' 
                            : 'bg-slate-800 text-slate-400 border border-transparent hover:bg-slate-700'
                        }`}
                      >
                        Drive {disk.disk_index ?? idx}: {disk.model}
                      </button>
                    ))}
                  </div>
                )}
                
                {/* Selected Disk Chart and Details */}
                {(() => {
                  const idx = selectedDiskIndex < stats.storage_drives.length ? selectedDiskIndex : 0;
                  const disk = stats.storage_drives[idx];
                  const latestDisk = findDiskReading(latestData?.Disks ?? [], disk.disk_index);
                  const isFresh = lastReadingAt !== null && clock - lastReadingAt < 10000;
                  const activeTime = isFresh ? validReading(latestDisk?.ActiveTimePercent) : null;
                  const readKbps = isFresh ? validReading(latestDisk?.ReadKbps) : null;
                  const writeKbps = isFresh ? validReading(latestDisk?.WriteKbps) : null;
                  const diskDataKey = 'activity';
                  const diskData = data.map(pt => ({
                    time: pt.time,
                    activity: validReading(findDiskReading(pt.Disks, disk.disk_index)?.ActiveTimePercent),
                  }));

                  return (
                    <div className="rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-4">
                      <div className="mb-3 flex items-center justify-between">
                        <div>
                          <h3 className="text-sm font-semibold text-white">{disk.model}</h3>
                <p className="text-xs text-slate-500">{formatGb(disk.size_gb)} {disk.media_type || disk.type}</p>
                        </div>
                        <span className="text-lg font-bold text-white">{activeTime === null ? 'Not available' : `${activeTime}% active`}</span>
                      </div>
                      
                      <p className="mb-3 text-sm text-slate-300" role="status">
                        {activeTime === null ? 'Live disk activity is unavailable. This does not mean the drive is idle.' : 'Live disk activity is available.'}
                      </p>
                      {lastReadingAt !== null && <p className="mb-3 text-xs text-slate-400">Last reading: {new Date(lastReadingAt).toLocaleTimeString()}</p>}
                      <p className="mb-4 text-sm text-slate-300">Activity shows how busy the drive is. It is different from how much space your files use.</p>
                      {activeTime !== null && <div className="h-48 w-full mb-6 mt-2" aria-label="Recent disk activity chart">
                        <ResponsiveContainer width="100%" height="100%">
                          <LineChart data={diskData} margin={{ top: 5, right: 0, left: -20, bottom: 0 }}>
                            <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" vertical={false} />
                            <XAxis dataKey="time" hide />
                            <YAxis domain={[0, 100]} tick={{ fill: '#64748b', fontSize: 12 }} stroke="#1e293b" />
                            <Line
                              type="monotone"
                              dataKey={diskDataKey}
                              stroke="#fbbf24"
                              strokeWidth={2}
                              dot={false}
                              isAnimationActive={false}
                            />
                          </LineChart>
                        </ResponsiveContainer>
                      </div>}

                      <div className="grid grid-cols-2 gap-4 md:grid-cols-4 mt-6">
                        <div><p className="text-xs text-slate-400">Capacity</p><p className="text-lg text-white">{disk.size_gb} GB</p></div>
                        <div><p className="text-xs text-slate-400">Type</p><p className="text-lg text-white">{disk.media_type || disk.type}</p></div>
                        <div><p className="text-xs text-slate-400">Interface</p><p className="text-lg text-white">{disk.interface || 'Unknown'}</p></div>
                        <div><p className="text-xs text-slate-400">Drive activity</p><p className="text-lg text-white">{activeTime === null ? 'Not available' : `${activeTime}%`}</p></div>
                        <div><p className="text-xs text-slate-400">Reading files</p><p className="text-lg text-white">{formatDiskRate(readKbps)}</p></div>
                        <div><p className="text-xs text-slate-400">Writing files</p><p className="text-lg text-white">{formatDiskRate(writeKbps)}</p></div>
                        {disk.used_gb != null && <div><p className="text-xs text-slate-400">Space used at last scan</p><p className="text-lg text-white">{formatGb(disk.used_gb)}{disk.usage_percent != null ? ` (${disk.usage_percent.toFixed(1)}%)` : ''}</p></div>}
                      </div>
                      <p className="mt-4 text-sm text-slate-400">Space readings come from the last scan. Refresh retrieves the latest saved readings; run a new device check to collect new readings.</p>
                    </div>
                  );
                })()}
              </div>
            )}

            {hardwareType !== 'OS' && hardwareType !== 'Storage' && (
              <div className="mb-6 rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-4">
                <div className="mb-2 flex items-center justify-between">
                  <h3 className="text-sm font-semibold text-slate-300">{chartTitle}</h3>
                  <span className="text-lg font-bold text-white">
                    {hardwareType === 'Memory'
                      ? formatTelemetryValue(latestData?.RamUsedGb, 'GB')
                      : hardwareType === 'Network'
                        ? formatTelemetryValue(latestData?.[dataKey as keyof TelemetryPoint], 'Kbps')
                        : formatTelemetryValue(latestData?.[dataKey as keyof TelemetryPoint], '%')}
                  </span>
                </div>
                <div className="h-48 w-full">
                  <ResponsiveContainer width="100%" height="100%">
                    <LineChart data={data} margin={{ top: 5, right: 0, left: -20, bottom: 0 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" vertical={false} />
                      <XAxis dataKey="time" hide />
                      <YAxis domain={chartYDomain} tick={{ fill: '#64748b', fontSize: 12 }} stroke="#1e293b" />
                      <Line
                        type="monotone"
                        dataKey={dataKey}
                        stroke={strokeColor}
                        strokeWidth={2}
                        dot={false}
                        isAnimationActive={false}
                      />
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              </div>
            )}

            {/* Detailed Properties Grid */}
            <div className="grid grid-cols-2 gap-x-8 gap-y-6 md:grid-cols-4">
              {hardwareType === 'CPU' && (
                <>
                  <div><p className="text-xs text-slate-400">Utilization</p><p className="text-xl text-white">{latestData?.CpuUsagePercent || 0}%</p></div>
                  <div><p className="text-xs text-slate-400">Speed</p><p className="text-xl text-white">{((latestData?.CpuSpeedMhz || stats.cpu.frequency_mhz) / 1000).toFixed(2)} GHz</p></div>
                  <div><p className="text-xs text-slate-400">Processes</p><p className="text-xl text-white">{latestData?.CpuProcesses || stats.cpu.processes || 0}</p></div>
                  <div><p className="text-xs text-slate-400">Threads</p><p className="text-xl text-white">{latestData?.CpuThreads || stats.cpu.threads || 0}</p></div>
                  <div><p className="text-xs text-slate-400">Handles</p><p className="text-xl text-white">{latestData?.CpuHandles || stats.cpu.handles || 0}</p></div>
                  <div><p className="text-xs text-slate-400">Base speed</p><p className="text-xl text-white">{(stats.cpu.max_frequency_mhz ? stats.cpu.max_frequency_mhz / 1000 : 0).toFixed(2)} GHz</p></div>
                  <div><p className="text-xs text-slate-400">Temperature</p><p className="text-xl text-white">{latestData?.CpuTempCelsius ? `${Math.round(latestData.CpuTempCelsius)}°C` : (stats.cpu.temperature_celsius ? `${Math.round(stats.cpu.temperature_celsius)}°C` : 'N/A')}</p></div>
                  <div><p className="text-xs text-slate-400">Sockets</p><p className="text-xl text-white">{stats.cpu.sockets || 1}</p></div>
                  <div><p className="text-xs text-slate-400">Cores</p><p className="text-xl text-white">{stats.cpu.cores}</p></div>
                  <div><p className="text-xs text-slate-400">Logical processors</p><p className="text-xl text-white">{stats.cpu.threads}</p></div>
                  <div><p className="text-xs text-slate-400">Virtualization</p><p className="text-xl text-white">{stats.cpu.virtualization_enabled ? 'Enabled' : 'Disabled'}</p></div>
                  <div><p className="text-xs text-slate-400">L1 cache</p><p className="text-xl text-white">{stats.cpu.l1_cache_kb || 0} KB</p></div>
                  <div><p className="text-xs text-slate-400">L2 cache</p><p className="text-xl text-white">{stats.cpu.l2_cache_mb || 0} MB</p></div>
                  <div><p className="text-xs text-slate-400">L3 cache</p><p className="text-xl text-white">{stats.cpu.l3_cache_mb || 0} MB</p></div>
                </>
              )}

              {hardwareType === 'Memory' && (
                <>
                  <div><p className="text-xs text-slate-400">In use</p><p className="text-xl text-white">{latestData?.RamUsedGb?.toFixed(1) || stats.ram.used_gb} GB</p></div>
                  <div><p className="text-xs text-slate-400">Available</p><p className="text-xl text-white">{(stats.ram.total_gb - (latestData?.RamUsedGb || stats.ram.used_gb)).toFixed(1)} GB</p></div>
                  <div><p className="text-xs text-slate-400">Speed</p><p className="text-xl text-white">{stats.ram.speed_mtps || 0} MT/s</p></div>
                  <div><p className="text-xs text-slate-400">Slots used</p><p className="text-xl text-white">{stats.ram.slots_used || 0} of {stats.ram.slots_total || 4}</p></div>
                  <div><p className="text-xs text-slate-400">Form factor</p><p className="text-xl text-white">{stats.ram.form_factor || 'DIMM'}</p></div>
                  <div><p className="text-xs text-slate-400">Hardware reserved</p><p className="text-xl text-white">{stats.ram.hardware_reserved_mb || 0} MB</p></div>
                  <div><p className="text-xs text-slate-400">Committed</p><p className="text-xl text-white">{stats.ram.committed_gb || 0} GB</p></div>
                  <div><p className="text-xs text-slate-400">Cached</p><p className="text-xl text-white">{stats.ram.cached_gb || 0} GB</p></div>
                </>
              )}

              {hardwareType === 'GPU' && (
                <>
                  <div><p className="text-xs text-slate-400">Utilization</p><p className="text-xl text-white">{latestData?.GpuUsagePercent || 0}%</p></div>
                  <div><p className="text-xs text-slate-400">GPU Memory</p><p className="text-xl text-white">{latestData?.GpuMemoryUsedGb || 0} / {stats.gpu.dedicated_memory_gb || stats.gpu.vram_gb} GB</p></div>
                  <div><p className="text-xs text-slate-400">Temperature</p><p className="text-xl text-white">{latestData?.GpuTempCelsius ? `${Math.round(latestData.GpuTempCelsius)}°C` : (stats.gpu.temperature_celsius ? `${Math.round(stats.gpu.temperature_celsius)}°C` : 'N/A')}</p></div>
                  <div><p className="text-xs text-slate-400">Driver version</p><p className="text-sm text-white">{stats.gpu.driver}</p></div>
                  <div><p className="text-xs text-slate-400">Driver date</p><p className="text-sm text-white">{stats.gpu.driver_date || 'Unknown'}</p></div>
                  <div><p className="text-xs text-slate-400">DirectX version</p><p className="text-sm text-white">{stats.gpu.directx_version || 'Unknown'}</p></div>
                  <div><p className="text-xs text-slate-400">Physical location</p><p className="text-sm text-white">{stats.gpu.physical_location || 'PCI bus'}</p></div>
                </>
              )}

              {hardwareType === 'Network' && (
                <>
                  <div><p className="text-xs text-slate-400">Send speed</p><p className="text-xl text-white">{formatTelemetryValue(latestData?.NetworkSendKbps, 'Kbps')}</p></div>
                  <div><p className="text-xs text-slate-400">Receive speed</p><p className="text-xl text-white">{formatTelemetryValue(latestData?.NetworkReceiveKbps, 'Kbps')}</p></div>
                  <div><p className="text-xs text-slate-400">IPv4 address</p><p className="text-sm text-white">{stats.network?.ip_address}</p></div>
                  <div><p className="text-xs text-slate-400">MAC address</p><p className="text-sm text-white">{stats.network?.mac_address}</p></div>
                </>
              )}

            </div>

            {hardwareType === 'Storage' && (!stats.storage_drives || stats.storage_drives.length === 0) && (
              <div className="mt-4 rounded-lg border border-slate-700 p-4">
                 <p className="text-slate-400">No storage drives detected.</p>
              </div>
            )}
            
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
