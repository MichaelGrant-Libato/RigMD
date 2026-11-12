using System.Management;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiMemoryProvider : IMemoryProvider
{
    public static MemoryStatsDto ReadUsage()
    {
        var dto = new MemoryStatsDto { UsageAvailable = false };
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status))
                return FromPhysicalBytes(status.TotalPhysical, status.AvailablePhysical);
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public MemoryStatsDto GetMemoryStats()
    {
        var dto = ReadUsage();
        List<ulong?>? installedModules = null;
        
        try
        {
            using var memSearcher = new ManagementObjectSearcher("SELECT Speed, FormFactor, Capacity FROM Win32_PhysicalMemory");
            var capacities = new List<ulong?>();
            double totalRawCapacity = 0;
            foreach (var obj in memSearcher.Get())
            {

                if (obj["Speed"] != null && double.TryParse(obj["Speed"].ToString(), out var speed))
                {
                    dto.SpeedMtps = speed;
                }
                
                if (obj["FormFactor"] != null && int.TryParse(obj["FormFactor"].ToString(), out var ff))
                {
                    dto.FormFactor = ff == 8 ? "DIMM" : ff == 12 ? "SODIMM" : "Unknown";
                }
                
                var capacity = ulong.TryParse(obj["Capacity"]?.ToString(), out var cap) && cap > 0 ? cap : (ulong?)null;
                capacities.Add(capacity);
                if (capacity.HasValue) totalRawCapacity += capacity.Value;
            }
            installedModules = capacities;
            
            dto.HardwareReservedMb = Math.Max(0, Math.Round((totalRawCapacity / (1024.0 * 1024.0)) - (dto.TotalGb * 1024.0), 1));
            
            using var perfSearcher = new ManagementObjectSearcher("SELECT CommittedBytes, CacheBytes FROM Win32_PerfFormattedData_PerfOS_Memory");
            foreach (var obj in perfSearcher.Get())
            {
                if (obj["CommittedBytes"] != null && long.TryParse(obj["CommittedBytes"].ToString(), out var committed))
                {
                    dto.CommittedGb = Math.Round(committed / (1024.0 * 1024.0 * 1024.0), 1);
                }
                
                if (obj["CacheBytes"] != null && long.TryParse(obj["CacheBytes"].ToString(), out var cached))
                {
                    dto.CachedGb = Math.Round(cached / (1024.0 * 1024.0 * 1024.0), 1);
                }
                break;
            }
        }
        catch
        {
            // Ignore
        }

        var slots = ReadSlotCounts(() => installedModules ?? throw new InvalidOperationException("Memory inventory unavailable."), ReadSystemSlotCounts);
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
