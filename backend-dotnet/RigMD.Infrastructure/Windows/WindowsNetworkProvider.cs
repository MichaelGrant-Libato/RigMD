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
            var activeAdapter =
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
                    .FirstOrDefault(item =>
                        item.Properties
                            .UnicastAddresses
                            .Any(address =>
                                address.Address.AddressFamily ==
                                    AddressFamily.InterNetwork));

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
            }
            catch (Exception ex)
            {
                result.DnsResolutionSucceeded =
                    false;

                result.DnsResolutionMessage =
                    $"DNS resolution failed: {ex.Message}";
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