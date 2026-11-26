using System.Management;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public sealed record NativeCpuActivity(double? UsagePercent, string State, DateTimeOffset? CapturedAt, double? WindowSeconds);

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
    public static NativeCpuActivity ReadNativeActivity()
    {
        var observation = NativeSampler.ReadObservation();
        return new(observation.Value, observation.Value.HasValue ? "measured" : observation.Primed ? "warming" : "unavailable",
            observation.CapturedAt, observation.WindowSeconds);
    }
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

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    public CpuStatsDto GetCpuStats()
    {
        var dto = GetCpuInventory();
        try { LoadLiveCpuInfo(dto); }
        catch { /* Frequency metadata must not invalidate a measured activity sample. */ }
        dto.SetTemperatureReading(CpuTemperatureSensorReader.Read());
        return dto;
    }

    public CpuStatsDto GetCpuInventory()
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

        return dto;
    }

    private static void LoadStaticCpuInfo(
        CpuStatsDto dto)
    {
        using var searcher =
            new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, CurrentClockSpeed, MaxClockSpeed, VirtualizationFirmwareEnabled, SocketDesignation FROM Win32_Processor");

        searcher.Options.Timeout = TimeSpan.FromSeconds(2);
        using var processors = searcher.Get();
        var sockets = 0;
        var cores = 0;
        var threads = 0;
        var completeCores = true;
        var completeThreads = true;
        var virtualization = new HashSet<bool?>();
        foreach (ManagementObject obj in processors)
        using (obj)
        {
            sockets++;
            if (sockets == 1)
            {
                dto.Name = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                dto.FrequencyMhz = TryConvertToDouble(obj["CurrentClockSpeed"]);
            }
            dto.MaxFrequencyMhz = Math.Max(dto.MaxFrequencyMhz, TryConvertToDouble(obj["MaxClockSpeed"]));
            if (int.TryParse(obj["NumberOfCores"]?.ToString(), out var coreCount) && coreCount > 0) cores += coreCount;
            else completeCores = false;
            if (int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out var threadCount) && threadCount > 0) threads += threadCount;
            else completeThreads = false;
            virtualization.Add(bool.TryParse(obj["VirtualizationFirmwareEnabled"]?.ToString(), out var enabled) ? enabled : null);
        }

        dto.Sockets = sockets;
        dto.Cores = completeCores ? cores : 0;
        dto.Threads = completeThreads ? threads : 0;
        if (virtualization.Count == 1) dto.VirtualizationEnabled = virtualization.Single();

        try
        {
            using var cacheSearcher = new ManagementObjectSearcher("SELECT Level, InstalledSize FROM Win32_CacheMemory");
            cacheSearcher.Options.Timeout = TimeSpan.FromSeconds(2);
            using var caches = cacheSearcher.Get();
            foreach (ManagementObject obj in caches)
            using (obj)
            {
                if (!int.TryParse(obj["Level"]?.ToString(), out var level) ||
                    !double.TryParse(obj["InstalledSize"]?.ToString(), out var sizeKb) || sizeKb <= 0) continue;
                if (level == 3) dto.L1CacheKb += sizeKb;
                else if (level == 4) dto.L2CacheMb += sizeKb / 1024.0;
                else if (level == 5) dto.L3CacheMb += sizeKb / 1024.0;
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

        if (dto.Threads <= 0)
        {
            var active = GetActiveProcessorCount(ushort.MaxValue);
            if (active <= int.MaxValue) dto.Threads = (int)active;
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
