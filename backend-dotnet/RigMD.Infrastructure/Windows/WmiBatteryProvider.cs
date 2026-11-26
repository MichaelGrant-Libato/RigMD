using System.Management;
using System.Runtime.InteropServices;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiBatteryProvider : IBatteryProvider
{
    public BatteryStatsDto? GetBatteryStats()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            var batteries = searcher.Get();

            if (batteries.Count == 0)
            {
                return new BatteryStatsDto { HasBattery = false, QuerySucceeded = true };
            }

            foreach (var obj in batteries)
            {
                var statusObj = obj["BatteryStatus"];
                var wmiStatusStr = obj["Status"]?.ToString();
                var statusStr = statusObj?.ToString();

                // Pre-flight presence check: require a valid non-null BatteryStatus (> 0) or WMI Status
                if (statusObj == null && string.IsNullOrWhiteSpace(wmiStatusStr))
                {
                    continue;
                }

                int.TryParse(statusStr, out int status);
                if (status <= 0 && string.IsNullOrWhiteSpace(wmiStatusStr))
                {
                    continue;
                }

                var chargeStr = obj["EstimatedChargeRemaining"]?.ToString();
                var runTimeStr = obj["EstimatedRunTime"]?.ToString();

                var chargeAvailable = int.TryParse(chargeStr, out int charge) && charge is >= 0 and <= 100;
                int.TryParse(runTimeStr, out int runTime);

                string description = status switch
                {
                    1 => "Discharging",
                    2 => "AC Power",
                    3 => "Fully Charged",
                    4 => "Low",
                    5 => "Critical",
                    6 => "Charging",
                    7 => "Charging and High",
                    8 => "Charging and Low",
                    9 => "Charging and Critical",
                    10 => "Undefined",
                    11 => "Partially Charged",
                    _ => "Unknown"
                };

                bool isCharging = status is 6 or 7 or 8 or 9;

                return new BatteryStatsDto
                {
                    HasBattery = true,
                    QuerySucceeded = true,
                    ChargeAvailable = chargeAvailable,
                    EstimatedChargeRemaining = charge,
                    BatteryStatus = status,
                    StatusDescription = description,
                    EstimatedRunTime = runTime == 71582788 ? 0 : runTime, // 71582788 usually means calculating
                    IsCharging = isCharging,
                    ChargePercent = charge,
                    HealthStatus = "Not measured"
                };
            }
        }
        catch
        {
            // Ignore
        }

        return new BatteryStatsDto { HasBattery = false, QuerySucceeded = false, ChargeAvailable = false };
    }

    public static (int? ChargePercent, bool? Charging) ReadLiveStatus()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryFlag == 255 || (status.BatteryFlag & 128) != 0)
            return (null, null);
        return (status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null, (status.BatteryFlag & 8) != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
