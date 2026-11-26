using System.Management;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiMemoryProvider : IMemoryProvider
{
    private const double BytesPerGb = 1073741824d;

    public static WindowsCounterQuery OpenCacheQuery() => new(
        @"\Memory\Standby Cache Core Bytes", @"\Memory\Standby Cache Normal Priority Bytes",
        @"\Memory\Standby Cache Reserve Bytes", @"\Memory\Modified Page List Bytes");

    public static double? ReadCacheGb(WindowsCounterQuery query)
    {
        if (!query.Collect()) return null;
        var core = query.Read(0);
        var normal = query.Read(1);
        var reserve = query.Read(2);
        var modified = query.Read(3);
        return (core + normal + reserve + modified) / BytesPerGb;
    }

    public static MemoryStatsDto ReadUsage()
    {
        var dto = new MemoryStatsDto { UsageAvailable = false };
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                dto = FromPhysicalBytes(status.TotalPhysical, status.AvailablePhysical);
                var performance = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
                if (GetPerformanceInfo(ref performance, performance.Size) && performance.PageSize > 0 &&
                    performance.CommitTotal <= performance.CommitLimit && performance.CommitLimit > 0)
                {
                    dto.CommittedGb = (double)performance.CommitTotal * performance.PageSize / 1073741824d;
                    dto.CommitLimitGb = (double)performance.CommitLimit * performance.PageSize / 1073741824d;
                }
                if (GetPhysicallyInstalledSystemMemory(out var installedKb) && installedKb > 0
                    && installedKb <= ulong.MaxValue / 1024 && installedKb * 1024 >= status.TotalPhysical)
                {
                    dto.InstalledGb = installedKb / 1048576d;
                    dto.HardwareReservedMb = (installedKb * 1024 - status.TotalPhysical) / 1048576d;
                }
            }
        }
        catch { }
        return dto;
    }

    internal static MemoryStatsDto FromPhysicalBytes(ulong total, ulong available)
    {
        if (total == 0 || available > total) return new() { UsageAvailable = false };
        return new()
        {
            UsageAvailable = true, TotalGb = Math.Round(total / 1073741824d, 2),
            UsedGb = Math.Round((total - available) / 1073741824d, 2),
            AvailableGb = available / 1073741824d,
            UsagePercent = Math.Round((total - available) * 100d / total, 1)
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache;
        public nuint KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("psapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInfo information, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public MemoryStatsDto GetMemoryStats()
    {
        var dto = ReadUsage();
        using var cache = OpenCacheQuery();
        dto.CachedGb = ReadCacheGb(cache);

        List<ulong?>? capacities = null;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, FormFactor, Capacity FROM Win32_PhysicalMemory");
            searcher.Options.Timeout = TimeSpan.FromSeconds(2);
            using var modules = searcher.Get();
            var sizes = new List<ulong?>();
            var speeds = new HashSet<double?>();
            var factors = new HashSet<string>();
            foreach (ManagementObject module in modules)
            using (module)
            {
                sizes.Add(ulong.TryParse(module["Capacity"]?.ToString(), out var capacity) && capacity > 0 ? capacity : null);
                speeds.Add(double.TryParse(module["ConfiguredClockSpeed"]?.ToString(), out var speed) && speed > 0 ? speed : null);
                factors.Add(module["FormFactor"]?.ToString() switch { "8" => "DIMM", "12" => "SODIMM", _ => "" });
            }
            capacities = sizes;
            if (speeds.Count == 1 && speeds.Single() is { } configured) dto.SpeedMtps = configured;
            if (factors.Count == 1) dto.FormFactor = factors.Single();
            else if (factors.Count > 1 && !factors.Contains("")) dto.FormFactor = "Mixed";
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }

        var slots = ReadSlotCounts(() => capacities ?? [], ReadSystemSlotCounts);
        dto.SlotsUsed = slots.Used;
        dto.SlotsTotal = slots.Total;
        return dto;
    }

    internal static (int? Used, int? Total) ReadSlotCounts(Func<IReadOnlyList<ulong?>> readModules, Func<IReadOnlyList<int?>> readArrays)
    {
        int? used = null, total = null;
        try
        {
            var modules = readModules();
            if (modules.Count > 0 && modules.All(capacity => capacity is > 0)) used = modules.Count;
        }
        catch { }
        try
        {
            var arrays = readArrays();
            if (arrays.Count > 0 && arrays.All(count => count is > 0 and <= ushort.MaxValue))
                total = arrays.Sum(count => count!.Value);
        }
        catch { }
        // Retain the independently reported installed count without inventing a larger array.
        if (used.HasValue && total.HasValue && used > total) total = null;
        return (used, total);
    }

    private static IReadOnlyList<int?> ReadSystemSlotCounts()
    {
        using var searcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray WHERE Use = 3");
        searcher.Options.Timeout = TimeSpan.FromSeconds(2);
        using var rows = searcher.Get();
        var counts = new List<int?>();
        foreach (var row in rows)
        {
            using (row)
                counts.Add(int.TryParse(row["MemoryDevices"]?.ToString(), out var count) ? count : null);
        }
        return counts;
    }
}
