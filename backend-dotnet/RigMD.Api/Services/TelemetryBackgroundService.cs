using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Application.Models;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using RigMD.Infrastructure.Windows;

namespace RigMD.Api.Services;

public class TelemetryBackgroundService : BackgroundService
{
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly ILogger<TelemetryBackgroundService> _logger;
    
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _threadCounter;
    private PerformanceCounter? _handleCounter;
    private PerformanceCounter? _processCounter;

    private readonly Dictionary<string, DiskCounters> _diskCounters = new(StringComparer.OrdinalIgnoreCase);
    private long _prevNetworkBytesSent;
    private long _prevNetworkBytesReceived;
    private DateTime _prevNetworkTime = DateTime.MinValue;

    public TelemetryBackgroundService(
        IHubContext<TelemetryHub> hubContext, 
        IHardwareMonitorService hardwareMonitor,
        ILogger<TelemetryBackgroundService> logger)
    {
        _hubContext = hubContext;
        _hardwareMonitor = hardwareMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try 
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _threadCounter = new PerformanceCounter("System", "Threads");
            _processCounter = new PerformanceCounter("System", "Processes");
            // Handles are not easily grabbed globally from System category without iterating processes, 
            // but we can estimate or leave 0 for now. Actually, "Process" -> "Handle Count" -> "_Total" works!
            _handleCounter = new PerformanceCounter("Process", "Handle Count", "_Total");
        } 
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize some performance counters");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Tick LHM to read new sensor values
                _hardwareMonitor.Tick();

                var dto = new TelemetryUpdateDto();
                
                // CPU
                if (_cpuCounter != null)
                {
                    dto.CpuUsagePercent = Math.Round((double)_cpuCounter.NextValue(), 1);
                }
                else
                {
                    dto.CpuUsagePercent = Math.Round(_hardwareMonitor.GetCpuLoad() ?? 0, 1);
                }
                
                dto.CpuTempCelsius = _hardwareMonitor.GetCpuTemperature();
                
                try { if (_processCounter != null) dto.CpuProcesses = (int)_processCounter.NextValue(); } catch {}
                try { if (_threadCounter != null) dto.CpuThreads = (int)_threadCounter.NextValue(); } catch {}
                try { if (_handleCounter != null) dto.CpuHandles = (int)_handleCounter.NextValue(); } catch {}

                // RAM
                dto.RamUsagePercent = Math.Round(_hardwareMonitor.GetRamLoad() ?? 0, 1);
                // Getting absolute memory used is tricky with LHM without total, but let's grab from WMI OperatingSystem briefly, 
                // or just rely on static values for absolute. Let's do a lightweight WMI query or GC memory. 
                // Wait, LHM "Data" sensor for Memory usually has "Used Memory".
                // Let's implement it inside the DTO, but for now we'll do the WMI one fast.
                try 
                {
                    using var osSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (var obj in osSearcher.Get())
                    {
                        if (long.TryParse(obj["TotalVisibleMemorySize"]?.ToString(), out var totalKb) && 
                            long.TryParse(obj["FreePhysicalMemory"]?.ToString(), out var freeKb))
                        {
                            var totalGb = totalKb / (1024.0 * 1024.0);
                            var freeGb = freeKb / (1024.0 * 1024.0);
                            var usedGb = totalGb - freeGb;
                            
                            dto.RamUsedGb = Math.Round(usedGb, 2);
                            if (totalGb > 0 && dto.RamUsagePercent == 0)
                                dto.RamUsagePercent = Math.Round((usedGb / totalGb) * 100, 1);
                        }
                        break;
                    }
                }
                catch { }

                // GPU
                dto.GpuUsagePercent = Math.Round(_hardwareMonitor.GetGpuLoad() ?? 0, 1);
                dto.GpuTempCelsius = _hardwareMonitor.GetGpuTemperature();
                dto.GpuMemoryUsedGb = Math.Round(_hardwareMonitor.GetGpuDedicatedMemoryUsedGb() ?? 0, 1);
                dto.GpuMemoryTotalGb = Math.Round(_hardwareMonitor.GetGpuDedicatedMemoryTotalGb() ?? 0, 1);

                // Disks (Persistent performance counters for real live activity & rates)
                UpdateDisks(dto);

                // Network (Real-time live throughput calculation in Kbps)
                UpdateNetwork(dto);

                await _hubContext.Clients.All.SendAsync("ReceiveTelemetry", dto, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in telemetry streaming");
            }

            await Task.Delay(1000, stoppingToken);
        }

        foreach (var counters in _diskCounters.Values)
        {
            counters.Dispose();
        }
        _diskCounters.Clear();
    }

    private void UpdateDisks(TelemetryUpdateDto dto)
    {
        try
        {
            var cat = new PerformanceCounterCategory("PhysicalDisk");
            var instances = cat.GetInstanceNames().Where(i => i != "_Total").ToHashSet(StringComparer.OrdinalIgnoreCase);

            var staleKeys = _diskCounters.Keys.Where(k => !instances.Contains(k)).ToList();
            foreach (var key in staleKeys)
            {
                if (_diskCounters.Remove(key, out var oldCounters))
                {
                    oldCounters.Dispose();
                }
            }

            foreach (var inst in instances)
            {
                if (!_diskCounters.TryGetValue(inst, out var counters))
                {
                    counters = new DiskCounters(inst);
                    _diskCounters[inst] = counters;
                    continue;
                }

                double activeTime = 0;
                double readKbps = 0;
                double writeKbps = 0;

                try
                {
                    if (counters.ActiveTime != null)
                    {
                        var raw = counters.ActiveTime.NextValue();
                        activeTime = Math.Clamp(Math.Round((double)raw, 1), 0, 100);
                    }
                }
                catch { }

                try
                {
                    if (counters.ReadBytes != null)
                    {
                        var raw = counters.ReadBytes.NextValue();
                        readKbps = Math.Round(raw / 1024.0, 1);
                    }
                }
                catch { }

                try
                {
                    if (counters.WriteBytes != null)
                    {
                        var raw = counters.WriteBytes.NextValue();
                        writeKbps = Math.Round(raw / 1024.0, 1);
                    }
                }
                catch { }

                dto.Disks.Add(new DiskTelemetryDto
                {
                    DeviceId = inst,
                    ActiveTimePercent = activeTime,
                    ReadKbps = readKbps,
                    WriteKbps = writeKbps
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read physical disk counters");
        }
    }

    private void UpdateNetwork(TelemetryUpdateDto dto)
    {
        try
        {
            long currentBytesSent = 0;
            long currentBytesReceived = 0;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var stats = nic.GetIPStatistics();
                currentBytesSent += stats.BytesSent;
                currentBytesReceived += stats.BytesReceived;
            }

            var now = DateTime.UtcNow;
            if (_prevNetworkTime != DateTime.MinValue)
            {
                var elapsedSeconds = (now - _prevNetworkTime).TotalSeconds;
                if (elapsedSeconds > 0.1)
                {
                    var deltaSent = currentBytesSent - _prevNetworkBytesSent;
                    var deltaRecv = currentBytesReceived - _prevNetworkBytesReceived;

                    if (deltaSent >= 0 && deltaRecv >= 0)
                    {
                        var sendKbps = Math.Round((deltaSent * 8.0) / 1024.0 / elapsedSeconds, 1);
                        var recvKbps = Math.Round((deltaRecv * 8.0) / 1024.0 / elapsedSeconds, 1);

                        dto.NetworkSendKbps = sendKbps;
                        dto.NetworkReceiveKbps = recvKbps;
                    }
                }
            }

            _prevNetworkBytesSent = currentBytesSent;
            _prevNetworkBytesReceived = currentBytesReceived;
            _prevNetworkTime = now;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read network statistics");
        }
    }

    private sealed class DiskCounters : IDisposable
    {
        public PerformanceCounter? ActiveTime { get; }
        public PerformanceCounter? ReadBytes { get; }
        public PerformanceCounter? WriteBytes { get; }

        public DiskCounters(string instance)
        {
            try
            {
                ActiveTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", instance, true);
                ActiveTime.NextValue();
            }
            catch { }

            try
            {
                ReadBytes = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instance, true);
                ReadBytes.NextValue();
            }
            catch { }

            try
            {
                WriteBytes = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instance, true);
                WriteBytes.NextValue();
            }
            catch { }
        }

        public void Dispose()
        {
            try { ActiveTime?.Dispose(); } catch { }
            try { ReadBytes?.Dispose(); } catch { }
            try { WriteBytes?.Dispose(); } catch { }
        }
    }
}
