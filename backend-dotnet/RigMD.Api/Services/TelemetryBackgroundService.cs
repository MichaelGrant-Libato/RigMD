using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Api.Services;

public class TelemetryBackgroundService(
    IHubContext<TelemetryHub> hubContext,
    Lazy<IHardwareMonitorService> hardwareMonitor,
    TelemetrySubscriptions subscriptions,
    ILogger<TelemetryBackgroundService> logger) : BackgroundService
{
    private readonly Dictionary<string, WindowsCounterQuery> _diskCounters = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, NetworkBaseline> _networkBaselines = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await subscriptions.WaitAsync(stoppingToken);
        using var system = new WindowsCounterQuery(
            @"\System\Processes", @"\System\Threads", @"\Process(_Total)\Handle Count",
            @"\Processor Information(_Total)\Processor Frequency");
        using var cache = WmiMemoryProvider.OpenCacheQuery();
        var gpu = new TelemetryCollector<IReadOnlyList<GpuAdapterReading>>("Graphics", () =>
        {
            var monitor = hardwareMonitor.Value;
            monitor.TickGpu();
            var adapters = monitor.GetGpuAdapters();
            return adapters.Count > 0 ? adapters : null;
        }, logger);
        var disk = new TelemetryCollector<List<DiskTelemetryDto>>("Storage", () =>
        {
            var sample = new TelemetryUpdateDto();
            UpdateDisks(sample);
            return sample.Disks.Any(drive => drive.CapturedAtUtc.HasValue) ? sample.Disks : null;
        }, logger);
        var network = new TelemetryCollector<IReadOnlyList<NetworkAdapterTelemetry>>("Network", () =>
        {
            var sample = ReadNetwork();
            return sample.Count > 0 ? sample : null;
        }, logger);
        var thermal = new TelemetryCollector<CpuTemperatureReading>("CPU temperature",
            () => CpuTemperatureSensorReader.Read(), logger);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await subscriptions.WaitAsync(stoppingToken);
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var adapters = gpu.Read(now, stoppingToken);
                    var disks = disk.Read(now, stoppingToken);
                    var links = network.Read(now, stoppingToken);
                    var temperature = thermal.Read(now, stoppingToken);
                    var cpuUsage = WmiCpuProvider.ReadUsagePercent();
                    var memory = WmiMemoryProvider.ReadUsage();
                    var battery = WmiBatteryProvider.ReadLiveStatus();
                    var singleGpu = adapters?.Count == 1 ? adapters[0] : null;
                    var dto = new TelemetryUpdateDto
                    {
                        CapturedAtUtc = DateTimeOffset.UtcNow,
                        CpuUsagePercent = cpuUsage,
                        CpuUsageSource = cpuUsage.HasValue ? WmiCpuProvider.UsageSource : "Unavailable",
                        CpuUsageCapturedAtUtc = WmiCpuProvider.UsageCapturedAt,
                        CpuUsageWindowSeconds = WmiCpuProvider.UsageWindowSeconds,
                        CpuTempCelsius = temperature?.Celsius,
                        CpuTemperatureSource = temperature?.Source ?? "Unavailable",
                        CpuTemperatureSensorIdentifier = temperature?.SensorIdentifier,
                        CpuTemperatureCapturedAtUtc = temperature?.CapturedAtUtc,
                        UptimeSeconds = Environment.TickCount64 / 1000d,
                        RamUsagePercent = memory.UsageAvailable == true ? memory.UsagePercent : null,
                        RamUsedGb = memory.UsageAvailable == true ? memory.UsedGb : null,
                        RamTotalGb = memory.UsageAvailable == true ? memory.TotalGb : null,
                        RamAvailableGb = memory.AvailableGb,
                        RamCommittedGb = memory.CommittedGb,
                        RamCommitLimitGb = memory.CommitLimitGb,
                        RamCachedGb = WmiMemoryProvider.ReadCacheGb(cache),
                        GpuAdapters = adapters ?? [],
                        GpuUsagePercent = singleGpu?.UsagePercent,
                        GpuTempCelsius = singleGpu?.TemperatureCelsius,
                        GpuMemoryUsedGb = singleGpu?.DedicatedUsedGb,
                        GpuMemoryTotalGb = singleGpu?.DedicatedMemoryGb,
                        Disks = disks ?? [],
                        NetworkAdapters = links ?? [],
                        BatteryChargePercent = battery.ChargePercent,
                        BatteryCharging = battery.Charging,
                        CollectorStatus = [gpu.Status(now), disk.Status(now), network.Status(now), thermal.Status(now)]
                    };
                    if (system.Collect())
                    {
                        dto.CpuProcesses = PositiveCount(system.Read(0));
                        dto.CpuThreads = PositiveCount(system.Read(1));
                        dto.CpuHandles = PositiveCount(system.Read(2));
                        dto.CpuSpeedMhz = system.Read(3) is > 0 and var speed ? speed : null;
                    }
                    await hubContext.Clients.All.SendAsync("ReceiveTelemetry", dto, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Error in telemetry streaming"); }

                await Task.Delay(1000, stoppingToken);
            }
        }
        finally
        {
            disk.AfterPending(() =>
            {
                foreach (var counter in _diskCounters.Values) counter.Dispose();
                _diskCounters.Clear();
            });
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
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

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
                    var linkBitsPerSecond = nic.Speed;
                    readings.Add(new(nic.Id, send, receive, linkBitsPerSecond > 0 ? linkBitsPerSecond / 1000000d : null));
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
