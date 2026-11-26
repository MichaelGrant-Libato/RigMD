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

    public NetworkStatsDto GetNetworkStats() => ReadNetwork(includeTests: true);

    public NetworkStatsDto GetAdapterSnapshot() => ReadNetwork(includeTests: false);

    private static NetworkStatsDto ReadNetwork(bool includeTests)
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
                                address.Address.AddressFamily == AddressFamily.InterNetwork ||
                                (!includeTests && address.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                    !address.Address.IsIPv6LinkLocal)))
                    .ToList();

            var probeTarget = IPAddress.Parse("8.8.8.8");
            var route = WindowsNetworkRoute.Find(probeTarget);
            if (route is null && !includeTests)
                route = WindowsNetworkRoute.Find(IPAddress.Parse("2606:4700:4700::1111"));
            var activeAdapter =
                candidates.FirstOrDefault(item => item.Adapter.Id == route?.Adapter.Id)
                ?? candidates.FirstOrDefault(item =>
                    item.Properties.GatewayAddresses.Any(gateway =>
                        gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(gateway.Address)))
                ?? candidates.FirstOrDefault();
            result.QuerySucceeded = true;

            if (activeAdapter == null)
            {
                result.DnsResolutionMessage =
                    includeTests ? "No active IPv4 network adapter was detected." : "No active IP adapter was detected.";

                return result;
            }

            result.HasActiveAdapter =
                true;
            result.AdapterId = activeAdapter.Adapter.Id;
            result.IsWifi = activeAdapter.Adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

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
            else if (!includeTests && route?.Adapter.Id == activeAdapter.Adapter.Id)
            {
                result.IpAddress = route.Source.ToString();
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

            if (!includeTests) return result;

            try
            {
                using var dnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var addresses = Dns.GetHostAddressesAsync(DnsTestHost, dnsTimeout.Token).GetAwaiter().GetResult();

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
                            var reply = ping.Send(probeTarget, timeout, buffer);
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
            catch (OperationCanceledException)
            {
                result.QuerySucceeded = false;
                result.DnsResolutionMessage = "Windows did not finish the DNS lookup in time.";
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
                    using var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                            Arguments = "wlan show interfaces",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(2000))
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { }
                        return result;
                    }
                    var output = outputTask.GetAwaiter().GetResult();

                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var guidText = activeAdapter.Adapter.Id.Trim('{', '}');
                    var interfaceStart = Array.FindIndex(lines, line => line.Contains(guidText, StringComparison.OrdinalIgnoreCase));
                    if (interfaceStart >= 0)
                    {
                        var signalLine = lines.Skip(interfaceStart + 1)
                            .TakeWhile(line => !line.TrimStart().StartsWith("Name", StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault(line => line.TrimStart().StartsWith("Signal", StringComparison.OrdinalIgnoreCase));
                        if (signalLine is not null && int.TryParse(signalLine.Split(':').Last().Trim().TrimEnd('%'), out var signal)
                            && signal is >= 0 and <= 100) result.WifiSignalStrength = signal;
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
            result.QuerySucceeded = false;
            result.DnsResolutionMessage =
                $"Network telemetry collection failed: {ex.Message}";

            return result;
        }
    }
}
