using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Application.Models;
using System.Diagnostics;
using System.Management;

namespace RigMD.Api.Services;

public class TelemetryBackgroundService : BackgroundService
{
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly ILogger<TelemetryBackgroundService> _logger;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;

    public TelemetryBackgroundService(IHubContext<TelemetryHub> hubContext, ILogger<TelemetryBackgroundService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try 
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
        } 
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize some performance counters");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dto = new TelemetryUpdateDto();
                
                if (_cpuCounter != null)
                {
                    dto.CpuUsagePercent = Math.Round((double)_cpuCounter.NextValue(), 1);
                }

                // Get Memory
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
                            if (totalGb > 0)
                                dto.RamUsagePercent = Math.Round((usedGb / totalGb) * 100, 1);
                        }
                        break;
                    }
                }
                catch { }

                // Get GPU (Mock or rough estimate using WMI)
                try
                {
                    using var gpuSearcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                    double totalGpu = 0;
                    foreach (var obj in gpuSearcher.Get())
                    {
                        if (obj["PercentProcessorTime"] != null && double.TryParse(obj["PercentProcessorTime"].ToString(), out var val))
                        {
                            totalGpu += val;
                        }
                    }
                    dto.GpuUsagePercent = Math.Clamp(Math.Round(totalGpu, 1), 0, 100);
                    dto.GpuMemoryUsedGb = 1.8; // Mocked for UI, as WMI doesn't easily expose realtime VRAM usage
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
