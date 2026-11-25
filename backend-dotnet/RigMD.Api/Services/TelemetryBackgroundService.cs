using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Api.Services;

public class TelemetryBackgroundService(
    IHubContext<TelemetryHub> hubContext,
    IHardwareMonitorService hardwareMonitor,
    ILogger<TelemetryBackgroundService> logger) : BackgroundService
{
    private readonly Dictionary<string, WindowsCounterQuery> _diskCounters = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, NetworkBaseline> _networkBaselines = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var system = new WindowsCounterQuery(
            @"\System\Processes", @"\System\Threads", @"\Process(_Total)\Handle Count",
            @"\Processor Information(_Total)\Processor Frequency");
        using var cache = WmiMemoryProvider.OpenCacheQuery();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    hardwareMonitor.Tick();
                    var cpuUsage = WmiCpuProvider.ReadUsagePercent();
                    var cpuTemperature = hardwareMonitor.GetCpuTemperatureReading();
                    var memory = WmiMemoryProvider.ReadUsage();
                    var battery = WmiBatteryProvider.ReadLiveStatus();
                    var dto = new TelemetryUpdateDto
                    {
                        CpuUsagePercent = cpuUsage,
                        CpuUsageSource = cpuUsage.HasValue ? WmiCpuProvider.UsageSource : "Unavailable",
                        CpuUsageCapturedAtUtc = WmiCpuProvider.UsageCapturedAt,
                        CpuUsageWindowSeconds = WmiCpuProvider.UsageWindowSeconds,
                        CpuTempCelsius = cpuTemperature?.Celsius,
                        CpuTemperatureSource = cpuTemperature?.Source ?? "Unavailable",
                        CpuTemperatureSensorIdentifier = cpuTemperature?.SensorIdentifier,
                        CpuTemperatureCapturedAtUtc = cpuTemperature?.CapturedAtUtc,
                        UptimeSeconds = Environment.TickCount64 / 1000d,
                        RamUsagePercent = memory.UsageAvailable == true ? memory.UsagePercent : null,
                        RamUsedGb = memory.UsageAvailable == true ? memory.UsedGb : null,
                        RamTotalGb = memory.UsageAvailable == true ? memory.TotalGb : null,
                        RamAvailableGb = memory.AvailableGb,
                        RamCommittedGb = memory.CommittedGb,
                        RamCommitLimitGb = memory.CommitLimitGb,
                        RamCachedGb = WmiMemoryProvider.ReadCacheGb(cache),
                        GpuAdapters = hardwareMonitor.GetGpuAdapters(),
                        GpuUsagePercent = hardwareMonitor.GetGpuLoad(),
                        GpuTempCelsius = hardwareMonitor.GetGpuTemperature(),
                        GpuMemoryUsedGb = hardwareMonitor.GetGpuDedicatedMemoryUsedGb(),
                        GpuMemoryTotalGb = hardwareMonitor.GetGpuDedicatedMemoryTotalGb(),
                        NetworkAdapters = ReadNetwork(),
                        BatteryChargePercent = battery.ChargePercent,
                        BatteryCharging = battery.Charging
                    };

                    if (system.Collect())
                    {
                        dto.CpuProcesses = PositiveCount(system.Read(0));
                        dto.CpuThreads = PositiveCount(system.Read(1));
                        dto.CpuHandles = PositiveCount(system.Read(2));
                        dto.CpuSpeedMhz = system.Read(3) is > 0 and var speed ? speed : null;
                    }

                    UpdateDisks(dto);
                    dto.CapturedAtUtc = DateTimeOffset.UtcNow;
                    await hubContext.Clients.All.SendAsync("ReceiveTelemetry", dto, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in telemetry streaming");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
        finally
        {
            foreach (var counter in _diskCounters.Values) counter.Dispose();
            _diskCounters.Clear();
        }
    }

    private static int? PositiveCount(double? value) => value is > 0 and <= int.MaxValue ? (int)value.Value : null;

    private void UpdateDisks(TelemetryUpdateDto dto)
    {
        try
        {
            var instances = new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames()
                .Where(name => name.Length > 0 && char.IsAsciiDigit(name[0])).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stale in _diskCounters.Keys.Where(key => !instances.Contains(key)).ToArray())
                if (_diskCounters.Remove(stale, out var old)) old.Dispose();

            foreach (var instance in instances)
            {
                var reading = new DiskTelemetryDto { DeviceId = instance };
                dto.Disks.Add(reading);

                if (!_diskCounters.TryGetValue(instance, out var counters))
                {
                    var prefix = $@"\PhysicalDisk({instance})\";
                    counters = new WindowsCounterQuery(prefix + "% Idle Time", prefix + "Disk Read Bytes/sec",
                        prefix + "Disk Write Bytes/sec", prefix + "Avg. Disk sec/Transfer", prefix + "Disk Transfers/sec");
                    _diskCounters[instance] = counters;
                    counters.Collect();
                    continue;
                }

                if (!counters.Collect()) continue;
                var idle = counters.Read(0);
                // Small above-100 variations come from counter timing and were observed on idle disks.
                reading.ActiveTimePercent = idle is >= 0 and <= 101 ? Math.Max(0, 100 - idle.Value) : null;
                reading.ReadKbps = counters.Read(1) / 1024;
                reading.WriteKbps = counters.Read(2) / 1024;
                var transfers = counters.Read(4);
                reading.HasTransfers = transfers.HasValue ? transfers.Value > 0 : null;
                if (reading.HasTransfers == true)
                    reading.ResponseTimeMs = counters.Read(3) * 1000;
                if (reading.ActiveTimePercent.HasValue || reading.ReadKbps.HasValue ||
                    reading.WriteKbps.HasValue || reading.HasTransfers.HasValue)
                    reading.CapturedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            dto.Disks.Clear();
            logger.LogDebug(ex, "Failed to read physical disk counters");
        }
    }

    private IReadOnlyList<NetworkAdapterTelemetry> ReadNetwork()
    {
        var current = new Dictionary<string, NetworkBaseline>(StringComparer.OrdinalIgnoreCase);
        var readings = new List<NetworkAdapterTelemetry>();
        try
        {
            var timestamp = Stopwatch.GetTimestamp();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                try
                {
                    var statistics = nic.GetIPStatistics();
                    current[nic.Id] = new(statistics.BytesSent, statistics.BytesReceived, timestamp);
                    double? send = null, receive = null;
                    if (_networkBaselines.TryGetValue(nic.Id, out var prior))
                    {
                        var elapsed = Stopwatch.GetElapsedTime(prior.Timestamp, timestamp).TotalSeconds;
                        if (elapsed is >= 0.1 and <= 5)
                        {
                            if (statistics.BytesSent >= prior.Sent) send = (statistics.BytesSent - prior.Sent) * 8d / 1000 / elapsed;
                            if (statistics.BytesReceived >= prior.Received) receive = (statistics.BytesReceived - prior.Received) * 8d / 1000 / elapsed;
                        }
                    }
                    readings.Add(new(nic.Id, send, receive, nic.Speed > 0 ? nic.Speed / 1000000d : null));
                }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException ex)
        {
            logger.LogDebug(ex, "Failed to read network statistics");
        }
        _networkBaselines = current;
        return readings;
    }

    private sealed record NetworkBaseline(long Sent, long Received, long Timestamp);
}
