using System.Management;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Infrastructure.Windows;

public class WmiMotherboardProvider : IMotherboardProvider
{
    public string GetChipsetDriver()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                var product = obj["Product"]?.ToString();
                if (!string.IsNullOrWhiteSpace(product))
                {
                    return $"{product.Trim()} (Auto-Managed)";
                }
            }
        }
        catch
        {
            // Ignore
        }

        return "Standard/Auto-Managed";
    }
}
