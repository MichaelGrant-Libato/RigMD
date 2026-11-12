using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RigMD.Api.Services;
using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Api;

public sealed class TelemetryStartupTests
{
    [Fact]
    public async Task SlowSensorInitializationDoesNotHoldUpTheHttpListener()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var subscriptions = new TelemetrySubscriptions(); subscriptions.Add("test-subscriber");
        builder.Services.AddSingleton(subscriptions);
        builder.Services.AddSingleton<IHardwareMonitorService>(_ =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Sensor gate was not released");
            return new Monitor();
        });
        builder.Services.AddSingleton(services => new Lazy<IHardwareMonitorService>(services.GetRequiredService<IHardwareMonitorService>));
        builder.Services.AddHostedService<TelemetryBackgroundService>();
        await using var app = builder.Build();
        app.MapGet("/", () => "ready");
        try
        {
            Assert.False(app.Services.GetRequiredService<Lazy<IHardwareMonitorService>>().IsValueCreated);
            await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            Assert.Equal("ready", await client.GetStringAsync(Assert.Single(app.Urls)));
            Assert.False(release.IsSet);
        }
        finally
        {
            release.Set();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.StopAsync(budget.Token);
        }
    }

    private sealed class Monitor : IHardwareMonitorService
    {
        public void Tick() { }
        public double? GetCpuTemperature() => null;
        public CpuTemperatureReading? GetCpuTemperatureReading() => null;
        public double? GetGpuTemperature() => null;
        public double? GetGpuDedicatedMemoryUsedGb() => null;
        public double? GetGpuDedicatedMemoryTotalGb() => null;
        public double? GetCpuLoad() => null;
        public double? GetRamLoad() => null;
        public double? GetGpuLoad() => null;
    }
}

