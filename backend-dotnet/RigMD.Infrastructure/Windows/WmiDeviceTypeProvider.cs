using System.Management;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Infrastructure.Windows;

public class WmiDeviceTypeProvider : IDeviceTypeProvider
{
    public string GetDeviceType()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (var obj in searcher.Get())
            {
                var chassisTypes = obj["ChassisTypes"] as ushort[];
                if (chassisTypes != null && chassisTypes.Length > 0)
                {
                    var chassis = chassisTypes[0];
                    return chassis switch
                    {
                        1 => "Other",
                        2 => "Unknown",
                        3 => "Desktop",
                        4 => "Low Profile Desktop",
                        5 => "Pizza Box",
                        6 => "Mini Tower",
                        7 => "Tower",
                        8 => "Portable",
                        9 => "Laptop",
                        10 => "Notebook",
                        11 => "Hand Held",
                        12 => "Docking Station",
                        13 => "All in One",
                        14 => "Sub Notebook",
                        15 => "Space-Saving",
                        16 => "Lunch Box",
                        17 => "Main System Chassis",
                        18 => "Expansion Chassis",
                        19 => "SubChassis",
                        20 => "Bus Expansion Chassis",
                        21 => "Peripheral Chassis",
                        22 => "Storage Chassis",
                        23 => "Rack Mount Chassis",
                        24 => "Sealed-Case PC",
                        30 => "Tablet",
                        31 => "Convertible",
                        32 => "Detachable",
                        _ => "Unknown"
                    };
                }
            }
        }
        catch
        {
            // Ignore
        }

        return "Unknown";
    }
}
