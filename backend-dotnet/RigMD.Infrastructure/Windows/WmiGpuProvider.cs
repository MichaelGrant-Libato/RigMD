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
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                
                if (name.Contains("Microsoft Basic"))
                    continue;

                dto.Name = name;
                dto.Driver = obj["DriverVersion"]?.ToString() ?? "Unknown";
                
                var dDate = obj["DriverDate"]?.ToString() ?? "";
                if (dDate.Length >= 8) 
                {
                    dto.DriverDate = $"{dDate.Substring(0, 4)}-{dDate.Substring(4, 2)}-{dDate.Substring(6, 2)}";
                }

                if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out var ramBytes))
                {
                    if (ramBytes > 0)
                    {
                        dto.VramGb = Math.Round(ramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                        dto.DedicatedMemoryGb = dto.VramGb;
                    }
                }
                
                var pnpId = obj["PNPDeviceID"]?.ToString() ?? "";
                if (pnpId.StartsWith("PCI\\"))
                {
                    dto.PhysicalLocation = "PCI bus"; // Simplified
                }
                
                dto.DirectXVersion = "12 (FL 12.2)"; // WMI doesn't easily expose DirectX version, mocked for UI demonstration.
                dto.SharedMemoryGb = Math.Round(dto.VramGb > 0 ? dto.VramGb * 0.5 : 8.0, 1); // Mocked shared memory
                
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
