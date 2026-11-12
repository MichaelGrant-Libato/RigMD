using System.Management;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiDisplayProvider : IDisplayProvider
{
    private readonly Func<int> _desktopMonitorCount;
    public WmiDisplayProvider() : this(() => GetSystemMetrics(80)) { }
    internal WmiDisplayProvider(Func<int> desktopMonitorCount) => _desktopMonitorCount = desktopMonitorCount;

    // SM_CMONITORS measures the current Windows desktop, including a remote desktop session.
    // It is not a count of graphics adapters or proof that a physical screen works.
    public int? GetConnectedDisplays()
    {
        try
        {
            var count = _desktopMonitorCount();
            return count > 0 ? count : null;
        }
        catch { return null; }
    }

    [DllImport("user32.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetSystemMetrics(int index);
    
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
