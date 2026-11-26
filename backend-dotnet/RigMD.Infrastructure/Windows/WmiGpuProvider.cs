using System.Management;
using System.Runtime.InteropServices;
using System.Text;
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
                if (dDate.Length >= 8 && DateOnly.TryParseExact(dDate[..8], "yyyyMMdd", out var date))
                {
                    candidate.DriverDate = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
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
                candidate.PhysicalLocation = ReadLocation(pnpId) ?? "";

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

    private static string? ReadLocation(string pnpId)
    {
        if (string.IsNullOrWhiteSpace(pnpId) || CM_Locate_DevNodeW(out var node, pnpId, 0) != 0) return null;
        var key = new DevicePropertyKey { Category = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 15 };
        var buffer = new byte[1024];
        var size = (uint)buffer.Length;
        return CM_Get_DevNode_PropertyW(node, in key, out var type, buffer, ref size, 0) == 0 &&
            type == 0x12 && size is >= 2 and <= 1024 && size % 2 == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim() : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey
    {
        public Guid Category;
        public uint Id;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, in DevicePropertyKey property, out uint propertyType,
        [Out] byte[] buffer, ref uint size, uint flags);
}
