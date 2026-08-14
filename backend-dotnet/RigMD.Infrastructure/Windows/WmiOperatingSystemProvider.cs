using System.Management;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiOperatingSystemProvider : IOperatingSystemProvider
{
    public string GetDeviceName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "Unknown PC";
        }
    }

    public string GetOsVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                var caption = obj["Caption"]?.ToString() ?? "Windows";
                var version = obj["Version"]?.ToString() ?? "";
                return $"{caption} ({version})";
            }
        }
        catch
        {
            // ignore
        }
        return $"Windows {Environment.OSVersion.Version}";
    }

    public string GetSystemAge()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                var installDateStr = obj["InstallDate"]?.ToString();
                if (!string.IsNullOrEmpty(installDateStr) && installDateStr.Length >= 14)
                {
                    var year = int.Parse(installDateStr.Substring(0, 4));
                    var month = int.Parse(installDateStr.Substring(4, 2));
                    var day = int.Parse(installDateStr.Substring(6, 2));
                    var installDate = new DateTime(year, month, day);
                    
                    var daysOld = (DateTime.Now - installDate).TotalDays;
                    var yearsOld = Math.Round(daysOld / 365.25, 1);
                    return $"~{yearsOld} years";
                }
            }
        }
        catch
        {
            // ignore
        }
        return "Unknown";
    }
}
