using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigMD.Api.Middleware;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public class LocalApiTransportTests
{
    [Fact]
    public async Task HttpAndSignalR_RequireCredential_AndAcceptDesktopTransports()
    {
        const string token = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(new LocalApiSession(token));
        builder.Services.AddSignalR();

        // This host has no production providers, database, or remediation services.
        await using var app = builder.Build();
        app.UseMiddleware<LocalApiAuthenticationMiddleware>();
        app.MapGet("/api/probe", (System.Security.Claims.ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier));
        app.MapHub<SecurityProbeHub>("/hubs/probe");
        await app.StartAsync();

        var address = new Uri(app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

        using var missing = await client.GetAsync("/api/probe", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using var missingHub = await client.PostAsync("/hubs/probe/negotiate?negotiateVersion=1", null, timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, missingHub.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Client-ID", "spoofed");
        Assert.Equal("local", await client.GetStringAsync("/api/probe", timeout.Token));
        using var negotiation = await client.PostAsync("/hubs/probe/negotiate?negotiateVersion=1", null, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, negotiation.StatusCode);

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "http://localhost:5273");
        var socketUrl = new UriBuilder(address) { Scheme = "ws", Path = "/hubs/probe", Query = $"access_token={token}" }.Uri;
        await socket.ConnectAsync(socketUrl, timeout.Token);
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        await socket.SendAsync(new ArraySegment<byte>(handshake), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[1024];
        var response = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.StartsWith("{}\u001e", Encoding.UTF8.GetString(buffer, 0, response.Count));
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Test completed", timeout.Token);
        await app.StopAsync(timeout.Token);
    }

    public sealed class SecurityProbeHub : Hub;
}
