using System.Management;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Infrastructure.Windows;

public class WmiDisplayProvider : IDisplayProvider
{
    public int GetConnectedDisplays()
    {
        int count = 0;
        try
        {
            // Win32_DesktopMonitor is sometimes unreliable on newer OS versions with multiple GPUs
            // Win32_VideoController can show active monitors if queried right, but let's count actual monitors 
            // via WmiMonitorID which is usually more accurate for connected displays.
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");
            foreach (var obj in searcher.Get())
            {
                count++;
            }
        }
        catch
        {
            // Fallback if root\wmi fails (sometimes permissions)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor");
                foreach (var obj in searcher.Get())
                {
                    count++;
                }
            }
            catch
            {
                // Ignore
            }
        }

        return count > 0 ? count : 1; // Assume at least 1 if we can't detect
    }
}
