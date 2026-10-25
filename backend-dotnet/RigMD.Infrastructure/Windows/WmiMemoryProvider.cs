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
        
        try
        {
            using var memSearcher = new ManagementObjectSearcher("SELECT Speed, FormFactor, Capacity FROM Win32_PhysicalMemory");
            int slotsUsed = 0;
            double totalRawCapacity = 0;
            foreach (var obj in memSearcher.Get())
            {
                slotsUsed++;
                if (obj["Speed"] != null && double.TryParse(obj["Speed"].ToString(), out var speed))
                {
                    dto.SpeedMtps = speed;
                }
                
                if (obj["FormFactor"] != null && int.TryParse(obj["FormFactor"].ToString(), out var ff))
                {
                    dto.FormFactor = ff == 8 ? "DIMM" : ff == 12 ? "SODIMM" : "Unknown";
                }
                
                if (obj["Capacity"] != null && long.TryParse(obj["Capacity"].ToString(), out var cap))
                {
                    totalRawCapacity += cap;
                }
            }
            dto.SlotsUsed = slotsUsed;
            dto.SlotsTotal = slotsUsed > 4 ? slotsUsed : 4; // Approximating total slots
            
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

        return dto;
    }
}
