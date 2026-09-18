using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Application.Models;
using System.Diagnostics;
using System.Management;
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

                // Disks
                // We could iterate Win32_PerfFormattedData_PerfDisk_PhysicalDisk but it takes ~300ms.
                // Let's grab it fast using PerformanceCounterCategory
                try
                {
                    var cat = new PerformanceCounterCategory("PhysicalDisk");
                    var instances = cat.GetInstanceNames();
                    foreach (var inst in instances)
                    {
                        if (inst == "_Total") continue;
                        
                        using var activeTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", inst);
                        using var readBytes = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", inst);
                        using var writeBytes = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", inst);
                        
                        // First read usually returns 0 for rate counters
                        activeTime.NextValue(); readBytes.NextValue(); writeBytes.NextValue();
                        
                        dto.Disks.Add(new DiskTelemetryDto
                        {
                            DeviceId = inst,
                            ActiveTimePercent = Math.Clamp(Math.Round((double)activeTime.NextValue(), 1), 0, 100),
                            ReadKbps = Math.Round(readBytes.NextValue() / 1024.0, 1),
                            WriteKbps = Math.Round(writeBytes.NextValue() / 1024.0, 1)
                        });
                    }
                }
                catch { }

                await _hubContext.Clients.All.SendAsync("ReceiveTelemetry", dto, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in telemetry streaming");
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}
