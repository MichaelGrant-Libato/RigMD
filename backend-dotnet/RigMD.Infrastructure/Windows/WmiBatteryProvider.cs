using System.Management;
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
                return new BatteryStatsDto { HasBattery = false };
            }

            foreach (var obj in batteries)
            {
                var chargeStr = obj["EstimatedChargeRemaining"]?.ToString();
                var statusStr = obj["BatteryStatus"]?.ToString();
                var runTimeStr = obj["EstimatedRunTime"]?.ToString();

                int.TryParse(chargeStr, out int charge);
                int.TryParse(statusStr, out int status);
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

                bool isCharging = status == 2 || status == 6 || status == 7 || status == 8 || status == 9;

                return new BatteryStatsDto
                {
                    HasBattery = true,
                    EstimatedChargeRemaining = charge,
                    BatteryStatus = status,
                    StatusDescription = description,
                    EstimatedRunTime = runTime == 71582788 ? 0 : runTime, // 71582788 usually means calculating
                    IsCharging = isCharging,
                    ChargePercent = charge,
                    HealthStatus = "Good" // WMI doesn't easily expose health wear without deeper queries, default to Good for now
                };
            }
        }
        catch
        {
            // Ignore
        }

        return new BatteryStatsDto { HasBattery = false };
    }
}
