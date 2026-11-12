using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using RigMD.Infrastructure.Windows;

namespace RigMD.Api.Services;

public sealed record HomeCpuReading(string Name, double? UsagePercent, string State, string Source, DateTimeOffset? CapturedAt, double? WindowSeconds);
public sealed record HomeMemoryReading(double? TotalGb, double? UsedGb, double? UsagePercent);
public sealed record HomeVolumeReading(string Drive, double? TotalGb, double? UsedGb, double? UsagePercent);
public sealed record HomeNetworkReading(bool QuerySucceeded, bool? HasActiveAdapter, bool? HasIpv4Address, bool? HasDefaultGateway, bool? HasDnsServers, string? Kind, string? IpAddress);
public sealed record HomeOverviewSnapshot(string Scope, DateTimeOffset CapturedAt, string DeviceName, HomeCpuReading Cpu, HomeMemoryReading Memory, bool StorageQuerySucceeded, IReadOnlyList<HomeVolumeReading> Volumes, HomeNetworkReading Network);

public interface IHomeOverviewReader
{
    void Prime();
    HomeOverviewSnapshot Read();
}

// This overview deliberately avoids WMI inventory, active network probes, and sensor-driver discovery.
public sealed class WindowsHomeOverviewReader : IHomeOverviewReader
{
    public void Prime() => WmiCpuProvider.ReadNativeActivity();

    public HomeOverviewSnapshot Read()
    {
        var activity = WmiCpuProvider.ReadNativeActivity();
        var memory = WmiMemoryProvider.ReadUsage();
        var name = "Processor information unavailable";
        try { name = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string ?? name; }
        catch { }
        var volumes = new List<HomeVolumeReading>();
        var storageAvailable = true;
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed))
            {
                try
                {
                    if (!drive.IsReady) { volumes.Add(new(drive.Name, null, null, null)); continue; }
                    var total = drive.TotalSize; var free = drive.TotalFreeSpace;
                    volumes.Add(VolumeFromBytes(drive.Name, total, free));
                }
                catch { volumes.Add(new(drive.Name, null, null, null)); }
            }
        }
        catch { storageAvailable = false; }
        return new("essential", DateTimeOffset.UtcNow, Environment.MachineName,
            new(name.Trim(), activity.UsagePercent, activity.State, "Windows processor busy time (native)", activity.CapturedAt, activity.WindowSeconds),
            new(memory.UsageAvailable == true ? memory.TotalGb : null, memory.UsageAvailable == true ? memory.UsedGb : null, memory.UsageAvailable == true ? memory.UsagePercent : null),
            storageAvailable, volumes, ReadLink());
    }

    public static HomeVolumeReading VolumeFromBytes(string drive, long total, long free) => total <= 0 || free < 0 || free > total
        ? new(drive, null, null, null)
        : new(drive, total / 1073741824d, (total - free) / 1073741824d, (total - free) * 100d / total);

    private static HomeNetworkReading ReadLink()
    {
        try
        {
            var links = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(item => new { Adapter = item, Properties = item.GetIPProperties() }).ToArray();
            var selected = links.FirstOrDefault(item => item.Properties.GatewayAddresses.Any(address => address.Address.AddressFamily == AddressFamily.InterNetwork))
                ?? links.FirstOrDefault(item => item.Properties.UnicastAddresses.Any(address => address.Address.AddressFamily == AddressFamily.InterNetwork))
                ?? links.FirstOrDefault();
            if (selected == null) return new(true, false, false, false, false, null, null);
            var ip = selected.Properties.UnicastAddresses.FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            var kind = selected.Adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "wireless"
                : selected.Adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT ? "wired" : "other";
            return new(true, true, ip != null, selected.Properties.GatewayAddresses.Any(address => address.Address.AddressFamily == AddressFamily.InterNetwork), selected.Properties.DnsAddresses.Count > 0, kind, ip?.ToString());
        }
        catch { return new(false, null, null, null, null, null, null); }
    }
}

