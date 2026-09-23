using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WindowsNetworkProvider :
    INetworkProvider
{
    private const string DnsTestHost =
        "example.com";

    public NetworkStatsDto GetNetworkStats()
    {
        var result =
            new NetworkStatsDto
            {
                DnsTestHost =
                    DnsTestHost
            };

        try
        {
            var candidates =
                NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(adapter =>
                        adapter.OperationalStatus ==
                            OperationalStatus.Up)
                    .Where(adapter =>
                        adapter.NetworkInterfaceType !=
                            NetworkInterfaceType.Loopback)
                    .Where(adapter =>
                        adapter.NetworkInterfaceType !=
                            NetworkInterfaceType.Tunnel)
                    .Select(adapter => new
                    {
                        Adapter =
                            adapter,

                        Properties =
                            adapter.GetIPProperties()
                    })
                    .Where(item =>
                        item.Properties
                            .UnicastAddresses
                            .Any(address =>
                                address.Address.AddressFamily ==
                                    AddressFamily.InterNetwork))
                    .ToList();

            // Prioritize an adapter that has an active default gateway (routed to LAN/Internet)
            var activeAdapter =
                candidates.FirstOrDefault(item =>
                    item.Properties.GatewayAddresses.Any(gateway =>
                        gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(gateway.Address)))
                ?? candidates.FirstOrDefault();

            if (activeAdapter == null)
            {
                result.DnsResolutionMessage =
                    "No active IPv4 network adapter was detected.";

                return result;
            }

            result.HasActiveAdapter =
                true;

            result.AdapterName =
                activeAdapter.Adapter.Name;

            result.HasIpv4Address =
                activeAdapter.Properties
                    .UnicastAddresses
                    .Any(address =>
                        address.Address.AddressFamily ==
                            AddressFamily.InterNetwork);

            var ipv4 = activeAdapter.Properties
                .UnicastAddresses
                .FirstOrDefault(address =>
                    address.Address.AddressFamily ==
                        AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                result.IpAddress = ipv4.Address.ToString();
            }

            try
            {
                var physAddress = activeAdapter.Adapter.GetPhysicalAddress();
                if (physAddress != null && physAddress.GetAddressBytes().Length > 0)
                {
                    result.MacAddress = string.Join(":", physAddress.GetAddressBytes().Select(b => b.ToString("X2")));
                }
            }
            catch
            {
                // Ignore physical address error
            }

            result.HasDefaultGateway =
                activeAdapter.Properties
                    .GatewayAddresses
                    .Any(gateway =>
                        gateway.Address.AddressFamily ==
                            AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(
                            gateway.Address));

            result.HasDnsServers =
                activeAdapter.Properties
                    .DnsAddresses
                    .Any(address =>
                        address.AddressFamily ==
                            AddressFamily.InterNetwork ||
                        address.AddressFamily ==
                            AddressFamily.InterNetworkV6);

            try
            {
                var addresses =
                    Dns.GetHostAddresses(
                        DnsTestHost);

                result.DnsResolutionSucceeded =
                    addresses.Length > 0;

                result.DnsResolutionMessage =
                    result.DnsResolutionSucceeded
                        ? $"DNS resolution for {DnsTestHost} succeeded."
                        : $"DNS resolution for {DnsTestHost} returned no addresses.";
                        
                // Add Ping Test to 8.8.8.8
                if (result.HasActiveAdapter)
                {
                    try
                    {
                        using var ping = new Ping();
                        var buffer = new byte[32];
                        int timeout = 1000;
                        int successfulPings = 0;
                        long totalRoundtripTime = 0;
                        int pingCount = 4;
                        
                        for (int i = 0; i < pingCount; i++)
                        {
                            var reply = ping.Send("8.8.8.8", timeout, buffer);
                            if (reply.Status == IPStatus.Success)
                            {
                                successfulPings++;
                                totalRoundtripTime += reply.RoundtripTime;
                            }
                        }
                        
                        if (successfulPings > 0)
                        {
                            result.PingLatencyMs = totalRoundtripTime / successfulPings;
                        }
                        result.PacketLossPercent = ((double)(pingCount - successfulPings) / pingCount) * 100;
                    }
                    catch
                    {
                        // Ignore ping errors (e.g. ICMP blocked)
                    }
                }
            }
            catch (Exception ex)
            {
                result.DnsResolutionSucceeded =
                    false;

                result.DnsResolutionMessage =
                    $"DNS resolution failed: {ex.Message}";
            }

            if (activeAdapter.Adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                result.IsWifi = true;
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = "wlan show interfaces",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var signalLine = lines.FirstOrDefault(l => l.Trim().StartsWith("Signal"));
                    if (signalLine != null)
                    {
                        var parts = signalLine.Split(':');
                        if (parts.Length > 1)
                        {
                            var valueStr = parts[1].Trim().Replace("%", "");
                            if (int.TryParse(valueStr, out int signal))
                            {
                                result.WifiSignalStrength = signal;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.DnsResolutionMessage =
                $"Network telemetry collection failed: {ex.Message}";

            return result;
        }
    }
}