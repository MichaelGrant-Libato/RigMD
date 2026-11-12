using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RigMD.Api.Controllers;
using RigMD.Api.Middleware;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Api;

public sealed class EssentialHomeTests
{
    [Fact]
    public async Task OverviewRequiresLocalAuthenticationAndDoesNotResolveFullInventoryOrSensors()
    {
        var reader = new Reader();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(HomeOverviewController).Assembly);
        builder.Services.AddSingleton(new LocalApiSession(new string('A', 64)));
        builder.Services.AddSingleton<IHomeOverviewReader>(reader);
        builder.Services.AddSingleton<TelemetrySubscriptions>(); builder.Services.AddSignalR();
        builder.Services.AddSingleton<IHardwareMonitorService>(_ => throw new InvalidOperationException("Full sensors must stay deferred"));
        builder.Services.AddSingleton(services => new Lazy<IHardwareMonitorService>(services.GetRequiredService<IHardwareMonitorService>));
        builder.Services.AddScoped<IWindowsSystemProfileService>(_ => throw new InvalidOperationException("Full inventory must stay deferred"));
        builder.Services.AddHostedService<TelemetryBackgroundService>();
        await using var app = builder.Build(); app.UseMiddleware<LocalApiAuthenticationMiddleware>(); app.MapControllers();
        await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)), Timeout = TimeSpan.FromSeconds(3) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/home/overview")).StatusCode); Assert.Equal(0, reader.Calls);
            client.DefaultRequestHeaders.Authorization = new("Bearer", new string('A', 64));
            client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/home/overview")).StatusCode); Assert.Equal(0, reader.Calls);
            client.DefaultRequestHeaders.Remove("Origin");
            using var response = await client.GetAsync("/api/home/overview"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("essential", json.RootElement.GetProperty("scope").GetString());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("memory").GetProperty("usagePercent").ValueKind);
            Assert.Equal(1, reader.Calls); Assert.False(app.Services.GetRequiredService<Lazy<IHardwareMonitorService>>().IsValueCreated);
        }
        finally { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await app.StopAsync(budget.Token); }
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(100, -1)] [InlineData(100, 101)]
    public void InvalidVolumeCapacityRemainsUnavailable(long total, long free)
    {
        var volume = WindowsHomeOverviewReader.VolumeFromBytes("X:\\", total, free);
        Assert.Null(volume.TotalGb); Assert.Null(volume.UsedGb); Assert.Null(volume.UsagePercent);
    }

    [Theory]
    [InlineData(100, 100, 0)] [InlineData(100, 0, 100)] [InlineData(100, 25, 75)]
    public void MeasuredEmptyAndFullVolumesArePreserved(long total, long free, double expected)
    {
        Assert.Equal(expected, WindowsHomeOverviewReader.VolumeFromBytes("X:\\", total, free).UsagePercent);
    }

    [Fact]
    public async Task SubscriptionWaitTracksMultipleConnectionsAndCancelsWithoutCreatingASample()
    {
        var subscriptions = new TelemetrySubscriptions(); using var cancellation = new CancellationTokenSource();
        var waiting = subscriptions.WaitAsync(cancellation.Token); Assert.False(waiting.IsCompleted);
        subscriptions.Add("one"); await waiting.WaitAsync(TimeSpan.FromSeconds(1));
        subscriptions.Add("one"); subscriptions.Add("two"); subscriptions.Remove("one");
        Assert.True(subscriptions.WaitAsync(cancellation.Token).IsCompletedSuccessfully);
        subscriptions.Remove("unknown"); subscriptions.Remove("two");
        var idle = subscriptions.WaitAsync(cancellation.Token); Assert.False(idle.IsCompleted);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => idle);
    }

    [Theory]
    [InlineData("startup.desktop", true)] [InlineData("startup.dashboard.desktop", true)] [InlineData("startup.summary.desktop", true)]
    [InlineData("history.ui", false)] [InlineData("report.ui", false)]
    public void EssentialScopeIsExplicitAndRestrictedToStartup(string operation, bool expected)
    {
        Assert.Equal(expected, PerformanceRules.IsValid(new(Guid.NewGuid(), operation, "completed", 1000, Mode: "essential_home")));
    }

    private sealed class Reader : IHomeOverviewReader
    {
        public int Calls;
        public void Prime() { }
        public HomeOverviewSnapshot Read() { Calls++; return new("essential", DateTimeOffset.UtcNow, "TEST-PC",
            new("Test CPU", null, "unavailable", "Unavailable", null, null), new(null, null, null), false, [], new(false, null, null, null, null, null, null)); }
    }
}
