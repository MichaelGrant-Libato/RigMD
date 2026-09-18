using System.Management;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiMemoryProvider : IMemoryProvider
{
    public MemoryStatsDto GetMemoryStats()
    {
        var dto = new MemoryStatsDto();
        
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                if (long.TryParse(obj["TotalVisibleMemorySize"]?.ToString(), out var totalKb) && 
                    long.TryParse(obj["FreePhysicalMemory"]?.ToString(), out var freeKb))
                {
                    var totalGb = totalKb / (1024.0 * 1024.0);
                    var freeGb = freeKb / (1024.0 * 1024.0);
                    var usedGb = totalGb - freeGb;
                    
                    dto.TotalGb = Math.Round(totalGb, 2);
                    dto.UsedGb = Math.Round(usedGb, 2);
                    if (totalGb > 0)
                        dto.UsagePercent = Math.Round((usedGb / totalGb) * 100, 1);
                }
                break;
            }

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
