import { useEffect, useState, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { X, Cpu, MemoryStick, HardDrive, Monitor, Wifi, Activity } from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts';
import * as signalR from '@microsoft/signalr';
import { HardwareStats } from '../types/rigmd';

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
  NetworkSendKbps: number;
  NetworkReceiveKbps: number;
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

export default function DeviceDetailModal({ isOpen, onClose, hardwareType, stats }: DeviceDetailModalProps) {
  const [data, setData] = useState<TelemetryPoint[]>([]);
  const [selectedDiskIndex, setSelectedDiskIndex] = useState(0);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    // Initialize 60 seconds of empty data
    const initialData = Array.from({ length: 60 }).map((_, i) => ({
      time: i.toString(),
      CpuUsagePercent: 0,
      RamUsagePercent: 0,
      GpuUsagePercent: 0,
      NetworkSendKbps: 0,
      NetworkReceiveKbps: 0,
      CpuSpeedMhz: 0,
      RamUsedGb: 0,
      GpuMemoryUsedGb: 0,
      Disks: [],
    }));
    setData(initialData);

    const connectSignalR = async () => {
      const hubUrl = `${import.meta.env.VITE_API_URL || 'http://localhost:5273'}/hubs/telemetry`;
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

      connection.on('ReceiveTelemetry', (telemetry: any) => {
        setData((prevData) => {
          const newData = [...prevData.slice(1)];
          newData.push({
            time: new Date().toLocaleTimeString(),
            CpuUsagePercent: telemetry.cpuUsagePercent || 0,
            RamUsagePercent: telemetry.ramUsagePercent || 0,
            GpuUsagePercent: telemetry.gpuUsagePercent || 0,
            NetworkSendKbps: telemetry.networkSendKbps || 0,
            NetworkReceiveKbps: telemetry.networkReceiveKbps || 0,
            CpuSpeedMhz: telemetry.cpuSpeedMhz || 0,
            RamUsedGb: telemetry.ramUsedGb || 0,
            GpuMemoryUsedGb: telemetry.gpuMemoryUsedGb || 0,
            CpuTempCelsius: telemetry.cpuTempCelsius,
            CpuProcesses: telemetry.cpuProcesses,
            CpuThreads: telemetry.cpuThreads,
            CpuHandles: telemetry.cpuHandles,
            GpuTempCelsius: telemetry.gpuTempCelsius,
            GpuMemoryTotalGb: telemetry.gpuMemoryTotalGb,
            Disks: telemetry.disks || [],
          });
          return newData;
        });
      });

      try {
        await connection.start();
        connectionRef.current = connection;
      } catch (err) {
        console.error('SignalR Connection Error: ', err);
      }
    };

    connectSignalR();

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop();
      }
    };
  }, [isOpen]);

  if (!isOpen || !stats) return null;

  // Determine which metric to show on chart based on hardware type
  let dataKey = 'CpuUsagePercent';
  let strokeColor = '#22d3ee'; // cyan-400
  let chartTitle = '% Utilization over 60 seconds';
  let chartYDomain = [0, 100];
  let Icon = Cpu;
  let title = 'CPU';
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
    title = 'GPU';
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
                <h2 className="text-2xl font-bold text-white">{title}</h2>
                <p className="text-sm text-slate-400">{subtitle}</p>
              </div>
            </div>
            <button
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
                        key={idx}
                        onClick={() => setSelectedDiskIndex(idx)}
                        className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
                          selectedDiskIndex === idx 
                            ? 'bg-amber-500/20 text-amber-400 border border-amber-500/50' 
                            : 'bg-slate-800 text-slate-400 border border-transparent hover:bg-slate-700'
                        }`}
                      >
                        Disk {idx}
                      </button>
                    ))}
                  </div>
                )}
                
                {/* Selected Disk Chart and Details */}
                {(() => {
                  const idx = selectedDiskIndex < stats.storage_drives.length ? selectedDiskIndex : 0;
                  const disk = stats.storage_drives[idx];
                  const latestDisk = latestData?.Disks?.find((d: DiskTelemetryPoint) => String(d?.DeviceId ?? '').startsWith(idx.toString()));
                  const activeTime = latestDisk?.ActiveTimePercent ?? 0;
                  const readKbps = latestDisk?.ReadKbps ?? 0;
                  const writeKbps = latestDisk?.WriteKbps ?? 0;
                  
                  const diskDataKey = `disk_${idx}_active`;
                  const diskData = data.map(pt => ({
                    time: pt.time,
                    [diskDataKey]: (pt.Disks || []).find((d: DiskTelemetryPoint) => String(d?.DeviceId ?? '').startsWith(idx.toString()))?.ActiveTimePercent ?? 0,
                  }));

                  return (
                    <div className="rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-4">
                      <div className="mb-3 flex items-center justify-between">
                        <div>
                          <h3 className="text-sm font-semibold text-white">Disk {idx} ({disk.model})</h3>
                          <p className="text-xs text-slate-500">{disk.size_gb} GB {disk.media_type || disk.type}</p>
                        </div>
                        <span className="text-lg font-bold text-white">{activeTime}%<span className="ml-1 text-xs font-normal text-slate-400">active</span></span>
                      </div>
                      
                      <div className="h-48 w-full mb-6 mt-2">
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
                      </div>

                      <div className="grid grid-cols-2 gap-4 md:grid-cols-4 mt-6">
                        <div><p className="text-xs text-slate-400">Capacity</p><p className="text-lg text-white">{disk.size_gb} GB</p></div>
                        <div><p className="text-xs text-slate-400">Type</p><p className="text-lg text-white">{disk.media_type || disk.type}</p></div>
                        <div><p className="text-xs text-slate-400">Interface</p><p className="text-lg text-white">{disk.interface || 'Unknown'}</p></div>
                        <div><p className="text-xs text-slate-400">Active Time</p><p className="text-lg text-white">{activeTime}%</p></div>
                        <div><p className="text-xs text-slate-400">Read Speed</p><p className="text-lg text-white">{readKbps > 1024 ? `${(readKbps / 1024).toFixed(1)} MB/s` : `${readKbps} KB/s`}</p></div>
                        <div><p className="text-xs text-slate-400">Write Speed</p><p className="text-lg text-white">{writeKbps > 1024 ? `${(writeKbps / 1024).toFixed(1)} MB/s` : `${writeKbps} KB/s`}</p></div>
                        {disk.used_gb != null && <div><p className="text-xs text-slate-400">Used</p><p className="text-lg text-white">{disk.used_gb} GB ({disk.usage_percent}%)</p></div>}
                      </div>
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
                    {hardwareType === 'Memory' ? `${latestData?.RamUsedGb?.toFixed(1) || 0} GB` : `${latestData?.[dataKey as keyof TelemetryPoint] || 0}%`}
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
                  <div><p className="text-xs text-slate-400">Temperature</p><p className="text-xl text-white">{latestData?.CpuTempCelsius ? `${Math.round(latestData.CpuTempCelsius)}°C` : 'N/A'}</p></div>
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
                  <div><p className="text-xs text-slate-400">Speed</p><p className="text-xl text-white">{(stats.ram as any).speed_mtps || 0} MT/s</p></div>
                  <div><p className="text-xs text-slate-400">Slots used</p><p className="text-xl text-white">{(stats.ram as any).slots_used || 0} of {(stats.ram as any).slots_total || 4}</p></div>
                  <div><p className="text-xs text-slate-400">Form factor</p><p className="text-xl text-white">{(stats.ram as any).form_factor || 'DIMM'}</p></div>
                  <div><p className="text-xs text-slate-400">Hardware reserved</p><p className="text-xl text-white">{(stats.ram as any).hardware_reserved_mb || 0} MB</p></div>
                  <div><p className="text-xs text-slate-400">Committed</p><p className="text-xl text-white">{(stats.ram as any).committed_gb || 0} GB</p></div>
                  <div><p className="text-xs text-slate-400">Cached</p><p className="text-xl text-white">{(stats.ram as any).cached_gb || 0} GB</p></div>
                </>
              )}

              {hardwareType === 'GPU' && (
                <>
                  <div><p className="text-xs text-slate-400">Utilization</p><p className="text-xl text-white">{latestData?.GpuUsagePercent || 0}%</p></div>
                  <div><p className="text-xs text-slate-400">GPU Memory</p><p className="text-xl text-white">{latestData?.GpuMemoryUsedGb || 0} / {stats.gpu.dedicated_memory_gb || stats.gpu.vram_gb} GB</p></div>
                  <div><p className="text-xs text-slate-400">Temperature</p><p className="text-xl text-white">{latestData?.GpuTempCelsius ? `${Math.round(latestData.GpuTempCelsius)}°C` : 'N/A'}</p></div>
                  <div><p className="text-xs text-slate-400">Driver version</p><p className="text-sm text-white">{stats.gpu.driver}</p></div>
                  <div><p className="text-xs text-slate-400">Driver date</p><p className="text-sm text-white">{stats.gpu.driver_date || 'Unknown'}</p></div>
                  <div><p className="text-xs text-slate-400">DirectX version</p><p className="text-sm text-white">{stats.gpu.directx_version || 'Unknown'}</p></div>
                  <div><p className="text-xs text-slate-400">Physical location</p><p className="text-sm text-white">{stats.gpu.physical_location || 'PCI bus'}</p></div>
                </>
              )}

              {hardwareType === 'Network' && (
                <>
                  <div><p className="text-xs text-slate-400">Send</p><p className="text-xl text-white">{latestData?.NetworkSendKbps || 0} Kbps</p></div>
                  <div><p className="text-xs text-slate-400">Receive</p><p className="text-xl text-white">{latestData?.NetworkReceiveKbps || 0} Kbps</p></div>
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
