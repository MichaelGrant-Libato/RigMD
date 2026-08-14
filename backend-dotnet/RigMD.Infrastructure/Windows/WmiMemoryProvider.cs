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
        }
        catch
        {
            // Ignore
        }

        return dto;
    }
}
