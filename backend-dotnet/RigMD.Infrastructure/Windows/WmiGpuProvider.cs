using System.Management;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiGpuProvider : IGpuProvider
{
    public GpuStatsDto GetGpuStats()
    {
        var dto = new GpuStatsDto { Name = "Unknown GPU", Driver = "Unknown", Type = "Unknown", VramGb = 0 };
        
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                
                if (name.Contains("Microsoft Basic"))
                    continue;

                dto.Name = name;
                dto.Driver = obj["DriverVersion"]?.ToString() ?? "Unknown";
                
                if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out var ramBytes))
                {
                    if (ramBytes > 0)
                    {
                        dto.VramGb = Math.Round(ramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                    }
                }
                
                var lowerName = name.ToLowerInvariant();
                if (lowerName.Contains("nvidia") || lowerName.Contains("rtx") || lowerName.Contains("gtx") || lowerName.Contains("radeon rx"))
                {
                    dto.Type = "Dedicated";
                }
                else if (lowerName.Contains("intel") || lowerName.Contains("radeon graphics"))
                {
                    dto.Type = "Integrated";
                }
                
                break; 
            }
        }
        catch (Exception)
        {
            // Ignore and return defaults
        }

        return dto;
    }
}
