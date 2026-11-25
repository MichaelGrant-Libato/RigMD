using System.Management;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiDisplayProvider : IDisplayProvider
{
    public int GetConnectedDisplays() => GetActiveDisplayCount() ?? 0;

    public int? GetActiveDisplayCount()
    {
        const uint activePaths = 2;
        return GetDisplayConfigBufferSizes(activePaths, out var paths, out _) == 0 && paths <= int.MaxValue
            ? (int)paths : null;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    public List<DisplayStatsDto> GetDisplays()
    {
        var displays = new List<DisplayStatsDto>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var hRes = obj["CurrentHorizontalResolution"]?.ToString();
                var vRes = obj["CurrentVerticalResolution"]?.ToString();
                var refresh = obj["CurrentRefreshRate"]?.ToString();
                var caption = obj["Caption"]?.ToString() ?? "Display";

                if (!string.IsNullOrEmpty(hRes) && !string.IsNullOrEmpty(vRes))
                {
                    displays.Add(new DisplayStatsDto
                    {
                        Name = caption,
                        Resolution = $"{hRes}x{vRes}",
                        RefreshRate = int.TryParse(refresh, out var r) ? r : 0
                    });
                }
            }
        }
        catch
        {
            // Ignore
        }
        return displays;
    }
}
