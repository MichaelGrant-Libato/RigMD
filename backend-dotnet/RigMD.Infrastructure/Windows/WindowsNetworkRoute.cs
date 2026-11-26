using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RigMD.Infrastructure.Windows;

public sealed record WindowsNetworkRoute(NetworkInterface Adapter, IPAddress Source, IPAddress Destination, IPAddress? Gateway)
{
    // Selecting a route with a UDP socket does not send traffic.
    public static WindowsNetworkRoute? Find(IPAddress destination)
    {
        try
        {
            using var socket = new Socket(destination.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(destination, 443));
            if (socket.LocalEndPoint is not IPEndPoint local || IPAddress.IsLoopback(local.Address)) return null;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                var properties = adapter.GetIPProperties();
                if (!properties.UnicastAddresses.Any(address => address.Address.Equals(local.Address))) continue;
                var gateway = properties.GatewayAddresses.Select(item => item.Address).FirstOrDefault(item =>
                    item.AddressFamily == destination.AddressFamily && !item.Equals(IPAddress.Any) && !item.Equals(IPAddress.IPv6Any));
                return new(adapter, local.Address, destination, gateway);
            }
        }
        catch (Exception ex) when (ex is SocketException or NetworkInformationException) { }
        return null;
    }
}
