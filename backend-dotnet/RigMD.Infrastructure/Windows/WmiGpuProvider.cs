using System.Management;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiGpuProvider : IGpuProvider
{
    public GpuStatsDto GetGpuStats()
    {
        var fallbackDto = new GpuStatsDto
        {
            HasGpu = false,
            HasDedicatedGpu = false,
            Name = "Unknown GPU",
            Driver = "Unknown",
            Type = "Unknown",
            VramGb = 0
        };

        var candidates = new List<GpuStatsDto>();

        try
        {
            var dxVersion = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\DirectX", "Version", null) as string;
            var resolvedDx = !string.IsNullOrEmpty(dxVersion) ? dxVersion : "Unknown";

            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = (obj["Name"]?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = new GpuStatsDto
                {
                    HasGpu = true,
                    Name = name,
                    Driver = obj["DriverVersion"]?.ToString() ?? "Unknown",
                    DirectXVersion = resolvedDx,
                    Type = "Integrated"
                };

                var dDate = obj["DriverDate"]?.ToString() ?? "";
                if (dDate.Length >= 8)
                {
                    candidate.DriverDate = $"{dDate.Substring(0, 4)}-{dDate.Substring(4, 2)}-{dDate.Substring(6, 2)}";
                }

                if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out var ramBytes) && ramBytes > 0)
                {
                    candidate.VramGb = Math.Round(ramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                    candidate.DedicatedMemoryGb = candidate.VramGb;
                }

                var registryVramGb = HardwareMonitorService.ReadRegistryMaxGpuVramGb(name);
                if (registryVramGb.HasValue && registryVramGb.Value > candidate.VramGb)
                {
                    candidate.VramGb = registryVramGb.Value;
                    candidate.DedicatedMemoryGb = registryVramGb.Value;
                }

                var pnpId = obj["PNPDeviceID"]?.ToString() ?? "";
                if (pnpId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                {
                    candidate.PhysicalLocation = "PCI bus";
                }

                candidate.SharedMemoryGb = Math.Round(candidate.VramGb > 0 ? candidate.VramGb * 0.5 : 8.0, 1);

                var lowerName = name.ToLowerInvariant();
                bool isDedicated =
                    lowerName.Contains("nvidia") ||
                    lowerName.Contains("geforce") ||
                    lowerName.Contains("rtx") ||
                    lowerName.Contains("gtx") ||
                    lowerName.Contains("quadro") ||
                    lowerName.Contains("radeon rx") ||
                    lowerName.Contains("radeon pro") ||
                    lowerName.Contains("intel arc");

                if (isDedicated)
                {
                    candidate.Type = "Dedicated";
                    candidate.HasDedicatedGpu = true;
                }
                else if (lowerName.Contains("intel") || lowerName.Contains("radeon") || lowerName.Contains("uhd") || lowerName.Contains("iris"))
                {
                    candidate.Type = "Integrated";
                    candidate.HasDedicatedGpu = false;
                }

                candidates.Add(candidate);
            }
        }
        catch (Exception)
        {
            // Ignore and return defaults
        }

        if (candidates.Count == 0)
        {
            return fallbackDto;
        }

        bool anyDedicated = candidates.Any(c => c.HasDedicatedGpu);
        var selected = candidates.FirstOrDefault(c => c.HasDedicatedGpu) ?? candidates[0];
        selected.HasGpu = true;
        selected.HasDedicatedGpu = anyDedicated;
        return selected;
    }
}
