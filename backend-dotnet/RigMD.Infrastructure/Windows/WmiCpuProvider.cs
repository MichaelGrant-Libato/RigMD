using System.Management;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiCpuProvider : ICpuProvider
{
    private static PerformanceCounter? _usageCounter;
    private static bool _utilityUnavailable;
    private static DateTimeOffset _retryUtilityAt;
    private static (ulong Idle, ulong Kernel, ulong User)? _previousTimes;
    private static readonly CpuUsageSampler NativeSampler = new(() =>
    {
        if (Environment.ProcessorCount > 64 || !GetSystemTimes(out var idle, out var kernel, out var user))
            throw new InvalidOperationException("System CPU times unavailable.");
        var current = (Idle: idle.Value, Kernel: kernel.Value, User: user.Value);
        var previous = _previousTimes;
        _previousTimes = current;
        return previous.HasValue ? CpuUsageSampler.BusyPercent(previous.Value.Idle, previous.Value.Kernel,
            previous.Value.User, current.Idle, current.Kernel, current.User) ?? double.NaN : double.NaN;
    });
    private static readonly CpuUsageSampler UsageSampler = new(() =>
    {
        try
        {
            // Busy time is workload, unlike frequency-normalized Processor Utility.
            _usageCounter ??= new PerformanceCounter("Processor Information", "% Processor Time", "_Total", true);
            var value = _usageCounter.NextValue();
            _utilityUnavailable = false;
            return value;
        }
        catch
        {
            _usageCounter?.Dispose();
            _usageCounter = null;
            _utilityUnavailable = true;
            _retryUtilityAt = DateTimeOffset.UtcNow.AddMinutes(1);
            throw;
        }
    });

    public static double? ReadUsagePercent()
    {
        var utility = !_utilityUnavailable || DateTimeOffset.UtcNow >= _retryUtilityAt ? UsageSampler.Read() : null;
        return _utilityUnavailable ? NativeSampler.Read() : utility;
    }

    public static string UsageSource => _utilityUnavailable ? "Windows processor busy time (fallback)" : "Windows processor busy time";
    public static DateTimeOffset? UsageCapturedAt => _utilityUnavailable ? NativeSampler.CapturedAt : UsageSampler.CapturedAt;
    public static double? UsageWindowSeconds => _utilityUnavailable ? NativeSampler.WindowSeconds : UsageSampler.WindowSeconds;

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out CpuFileTime idle, out CpuFileTime kernel, out CpuFileTime user);

    public CpuStatsDto GetCpuStats()
    {
        var dto = new CpuStatsDto { UsageAvailable = false };

        try
        {
            LoadStaticCpuInfo(dto);
        }
        catch
        {
            ApplyFallback(dto);
        }

        try { LoadLiveCpuInfo(dto); }
        catch { /* Frequency metadata must not invalidate a measured activity sample. */ }

        dto.SetTemperatureReading(CpuTemperatureSensorReader.Read());
        return dto;
    }

    private static void LoadStaticCpuInfo(
        CpuStatsDto dto)
    {
        using var searcher =
            new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, CurrentClockSpeed, MaxClockSpeed, VirtualizationFirmwareEnabled, SocketDesignation FROM Win32_Processor");

        int sockets = 0;
        foreach (
            ManagementObject obj
            in searcher.Get())
        {
            sockets++;
            if (sockets > 1) continue;
            
            dto.Name =
                obj["Name"]
                    ?.ToString()
                    ?.Trim()
                ?? "Unknown CPU";

            dto.Cores =
                Convert.ToInt32(
                    obj["NumberOfCores"]
                    ?? 0);

            dto.Threads =
                Convert.ToInt32(
                    obj["NumberOfLogicalProcessors"]
                    ?? 0);

            dto.FrequencyMhz =
                Convert.ToDouble(
                    obj["CurrentClockSpeed"]
                    ?? 0);

            dto.MaxFrequencyMhz =
                Convert.ToDouble(
                    obj["MaxClockSpeed"]
                    ?? 0);

            if (obj["VirtualizationFirmwareEnabled"] != null && bool.TryParse(obj["VirtualizationFirmwareEnabled"].ToString(), out var virt))
            {
                dto.VirtualizationEnabled = virt;
            }
        }
        
        dto.Sockets = sockets > 0 ? sockets : 1;

        try
        {
            using var cacheSearcher = new ManagementObjectSearcher("SELECT Level, MaxCacheSize FROM Win32_CacheMemory");
            foreach (ManagementObject obj in cacheSearcher.Get())
            {
                if (obj["Level"] != null && obj["MaxCacheSize"] != null &&
                    int.TryParse(obj["Level"].ToString(), out var level) &&
                    double.TryParse(obj["MaxCacheSize"].ToString(), out var sizeKb))
                {
                    if (level == 3) dto.L1CacheKb += sizeKb; // WMI Levels are 3=L1, 4=L2, 5=L3 usually
                    else if (level == 4) dto.L2CacheMb += sizeKb / 1024.0;
                    else if (level == 5) dto.L3CacheMb += sizeKb / 1024.0;
                }
            }
        }
        catch { }
    }

    private static void LoadLiveCpuInfo(
        CpuStatsDto dto)
    {
        RefreshUsage(dto);
        using var searcher =
            new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation");

        foreach (
            ManagementObject obj
            in searcher.Get())
        {
            var name =
                obj["Name"]
                    ?.ToString();

            if (!string.Equals(
                    name,
                    "_Total",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var frequency =
                TryConvertToDouble(
                    obj["ProcessorFrequency"]);
            if (frequency > 0) dto.FrequencyMhz = frequency;
            break;
        }
    }

    public void RefreshActivity(CpuStatsDto cpu) => RefreshUsage(cpu);

    private static void RefreshUsage(CpuStatsDto dto)
    {
        var sampledUsage = ReadUsagePercent();
        dto.UsageAvailable = sampledUsage.HasValue;
        dto.UsagePercent = sampledUsage ?? 0;
        dto.UsageSource = sampledUsage.HasValue ? UsageSource : "Unavailable";
        dto.UsageCapturedAtUtc = sampledUsage.HasValue ? UsageCapturedAt : null;
        dto.UsageWindowSeconds = sampledUsage.HasValue ? UsageWindowSeconds : null;
    }

    internal static double? ReadUsage(object? value)
    {
        return value != null && double.TryParse(value.ToString(), out var parsed) &&
            double.IsFinite(parsed) && parsed >= 0 ? parsed : null;
    }

    private static double TryConvertToDouble(
        object? value)
    {
        if (value == null)
        {
            return 0;
        }

        return double.TryParse(
            value.ToString(),
            out var result)
            ? result
            : 0;
    }

    private static void ApplyFallback(
        CpuStatsDto dto)
    {
        if (string.IsNullOrWhiteSpace(
                dto.Name))
        {
            dto.Name =
                "Unknown CPU";
        }

        if (dto.Cores <= 0)
        {
            dto.Cores =
                Environment.ProcessorCount;
        }

        if (dto.Threads <= 0)
        {
            dto.Threads =
                Environment.ProcessorCount;
        }

        dto.UsagePercent =
            Math.Max(
                dto.UsagePercent,
                0);

        dto.FrequencyMhz =
            Math.Max(
                dto.FrequencyMhz,
                0);
    }
}
