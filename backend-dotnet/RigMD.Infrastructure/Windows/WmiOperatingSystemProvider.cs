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
            var earliestDate = DetermineOriginalInstallDate();
            if (earliestDate.HasValue)
            {
                return FormatAge(earliestDate.Value, DateTime.Now);
            }
        }
        catch
        {
            // fallback
        }
        return "Unknown";
    }

    public static DateTime? DetermineOriginalInstallDate()
    {
        DateTime? earliest = null;

        void Consider(DateTime? dt)
        {
            if (!dt.HasValue) return;
            var d = dt.Value;
            // Must be reasonable: between year 2000 and current time (+1 day clock skew)
            if (d >= new DateTime(2000, 1, 1) && d <= DateTime.Now.AddDays(1))
            {
                if (!earliest.HasValue || d < earliest.Value)
                {
                    earliest = d;
                }
            }
        }

        // 1. Filesystem: Windows directory creation time
        // Preserved across Windows feature updates and in-place build upgrades
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(winDir) || !Directory.Exists(winDir))
            {
                var sysDir = Environment.SystemDirectory;
                if (!string.IsNullOrWhiteSpace(sysDir))
                {
                    winDir = Directory.GetParent(sysDir)?.FullName;
                }
            }

            if (!string.IsNullOrWhiteSpace(winDir) && Directory.Exists(winDir))
            {
                Consider(Directory.GetCreationTime(winDir));
            }
        }
        catch { }

        // 2. Filesystem: Users directory creation time (user profiles root)
        try
        {
            var sysDir = Environment.SystemDirectory;
            var root = Path.GetPathRoot(sysDir) ?? "C:\\";
            var usersDir = Path.Combine(root, "Users");
            if (Directory.Exists(usersDir))
            {
                Consider(Directory.GetCreationTime(usersDir));
            }
        }
        catch { }

        // 3. Registry: CurrentVersion InstallTime / InstallDate
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var installTimeObj = key.GetValue("InstallTime");
                if (installTimeObj is long installTime && installTime > 0)
                {
                    Consider(DateTime.FromFileTime(installTime));
                }

                var installDateObj = key.GetValue("InstallDate");
                if (installDateObj is int installDateUnix && installDateUnix > 0)
                {
                    Consider(DateTimeOffset.FromUnixTimeSeconds(installDateUnix).LocalDateTime);
                }
            }
        }
        catch { }

        // 4. WMI: Win32_OperatingSystem.InstallDate
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                var installDateStr = obj["InstallDate"]?.ToString();
                if (!string.IsNullOrEmpty(installDateStr) && installDateStr.Length >= 8)
                {
                    if (int.TryParse(installDateStr.Substring(0, 4), out var year) &&
                        int.TryParse(installDateStr.Substring(4, 2), out var month) &&
                        int.TryParse(installDateStr.Substring(6, 2), out var day))
                    {
                        Consider(new DateTime(year, month, day));
                    }
                }
            }
        }
        catch { }

        return earliest;
    }

    public static string FormatAge(DateTime installDate, DateTime now)
    {
        var daysOld = Math.Max(0, (now - installDate).TotalDays);
        var dateFormatted = installDate.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

        if (daysOld < 30)
        {
            var days = Math.Max(1, (int)Math.Round(daysOld));
            return $"{days} {(days == 1 ? "day" : "days")} ({dateFormatted})";
        }

        if (daysOld < 365)
        {
            var months = Math.Max(1, (int)Math.Round(daysOld / 30.44));
            return $"{months} {(months == 1 ? "month" : "months")} ({dateFormatted})";
        }

        var years = Math.Round(daysOld / 365.25, 1);
        if (years == 1.0)
        {
            return $"1 year ({dateFormatted})";
        }

        return $"~{years} years ({dateFormatted})";
    }
}
