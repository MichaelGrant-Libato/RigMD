using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RigMD.Infrastructure.Windows;

namespace RigMD.Api.Services;

public sealed record ConnectionStep(string Name, string State, string Detail, double? Milliseconds = null);
public sealed record ConnectionReading(DateTimeOffset CapturedAt, string? Adapter, ConnectionStep[] Steps);

public static class WindowsConnectionTest
{
    private const string TestHost = "www.microsoft.com";

    public static async Task<ConnectionReading> RunAsync(CancellationToken token)
    {
        var lookup = await ResolveAsync(token);
        var addresses = lookup.Addresses;
        var route = addresses.Select(WindowsNetworkRoute.Find).FirstOrDefault(candidate => candidate is not null);
        // These addresses are used for a route lookup only. Connecting a UDP socket sends no probe.
        route ??= WindowsNetworkRoute.Find(IPAddress.Parse("1.1.1.1"))
            ?? WindowsNetworkRoute.Find(IPAddress.Parse("2606:4700:4700::1111"));
        var routedToHost = route is not null && addresses.Contains(route.Destination);
        var interfaceStep = route is null
            ? new ConnectionStep("Network route", "unavailable", "Windows did not provide a usable route for the test.")
            : new ConnectionStep("Network route", "passed", route.Adapter.Name + (routedToHost ? " is routed to the test site." : " has an external route."));
        var gateway = TestGatewayAsync(route, token);
        var https = TestHttpsAsync(routedToHost ? route : null, token);
        await Task.WhenAll(gateway, https);
        return new(DateTimeOffset.UtcNow, route?.Adapter.Name, [interfaceStep, await gateway, lookup.Step, await https]);
    }

    private static async Task<(IPAddress[] Addresses, ConnectionStep Step)> ResolveAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var start = Stopwatch.GetTimestamp();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(TestHost, timeout.Token);
            return (addresses, new("DNS", addresses.Length > 0 ? "passed" : "failed",
                addresses.Length > 0 ? "Windows resolved " + TestHost + "." : "Windows returned no addresses.",
                Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return ([], new("DNS", "timeout", "Windows did not finish the lookup in time.")); }
        catch (SocketException)
        { return ([], new("DNS", "failed", "Windows could not resolve " + TestHost + ".")); }
    }

    private static async Task<ConnectionStep> TestGatewayAsync(WindowsNetworkRoute? route, CancellationToken token)
    {
        if (route?.Gateway is not { } gateway)
            return new("Gateway", "unavailable", "This interface supplied no gateway to ping.");
        using var ping = new Ping();
        try
        {
            var result = await ping.SendPingAsync(gateway, TimeSpan.FromSeconds(2), [0, 1, 2, 3],
                new PingOptions(), token);
            return result.Status == IPStatus.Success
                ? new("Gateway", "passed", "The gateway replied.", result.RoundtripTime)
                : new("Gateway", "no reply", "The gateway did not reply. Some networks block ping.");
        }
        catch (PingException)
        { return new("Gateway", "unavailable", "Windows could not send the gateway ping."); }
    }

    private static async Task<ConnectionStep> TestHttpsAsync(WindowsNetworkRoute? route, CancellationToken token)
    {
        if (route is null)
            return new("HTTPS", "unavailable", "The test site needs a resolved address and a usable route.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(route.Destination.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(route.Source, 0));
                    await socket.ConnectAsync(new IPEndPoint(route.Destination, 443), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Head, "https://" + TestHost + "/");
        var start = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return new("HTTPS", "passed", "The site replied over HTTPS using a direct connection.",
                Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new("HTTPS", "timeout", "The direct HTTPS request did not finish in time."); }
        catch (HttpRequestException)
        { return new("HTTPS", "failed", "The direct HTTPS request failed. A proxy or sign-in page can affect this test."); }
    }
}
