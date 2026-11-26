using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public sealed class DeviceDiagnosticsTests
{
    [Fact]
    public async Task SlowCollectorDropsLateResultThenRecoversWithoutStartingDuplicateReads()
    {
        var clock = new ManualClock();
        using var firstRead = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var collector = new TelemetryCollector<string>("GPU", () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstRead.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                return "late sample";
            }
            return "recovered sample";
        }, NullLogger.Instance, clock);

        try
        {
            Assert.Null(collector.Read(clock.Now, CancellationToken.None));
            Assert.True(firstRead.Wait(TimeSpan.FromSeconds(5)));
            clock.Now = clock.Now.AddSeconds(6);
            Assert.Null(collector.Read(clock.Now, CancellationToken.None));
            Assert.Equal("timed out", collector.Status(clock.Now).State);
            Assert.Equal(1, Volatile.Read(ref calls));

            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            collector.AfterPending(() => finished.SetResult());
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(collector.Read(clock.Now, CancellationToken.None));
            var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            collector.AfterPending(() => recovered.SetResult());
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("recovered sample", collector.Read(clock.Now, new CancellationToken(canceled: true)));
            Assert.Equal("live", collector.Status(clock.Now).State);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task OverlappingRequestsForTheSameReadAreRejectedButOtherViewsStayAvailable()
    {
        var runner = new DeviceDiagnosticRunner();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = runner.TryRunAsync("drive", _ => release.Task, CancellationToken.None);
        Assert.Null(await runner.TryRunAsync("drive", _ => Task.FromResult("duplicate"), CancellationToken.None));
        Assert.Equal("network", await runner.TryRunAsync("network", _ => Task.FromResult("network"), CancellationToken.None));
        release.SetResult("drive");
        Assert.Equal("drive", await first);
        Assert.Equal("fresh", await runner.TryRunAsync("drive", _ => Task.FromResult("fresh"), CancellationToken.None));
    }

    [Fact]
    public void BatteryReportPreservesNamedMeasurementsAndSkipsUnknownOrOlderEntries()
    {
        var report = XDocument.Parse("""
            <BatteryReport xmlns="http://schemas.microsoft.com/windows/2012/batteryreport">
              <Batteries><Battery><Id>Battery model</Id><DesignCapacity>48000</DesignCapacity>
              <FullChargeCapacity>36000</FullChargeCapacity><CycleCount>4294967295</CycleCount></Battery></Batteries>
              <RecentUsage><UsageEntry Timestamp="2026-11-25T12:00:00Z" ChargeCapacity="31000" Discharge="2300"/>
              <UsageEntry Timestamp="2026-11-20T12:00:00Z" ChargeCapacity="47000"/>
              <UsageEntry Timestamp="2026-11-26T12:00:00Z" ChargeCapacity="0"/></RecentUsage>
            </BatteryReport>
            """);
        var result = WindowsHardwareHealth.ParseBatteryReport(report, DateTimeOffset.Parse("2026-11-26T13:00:00Z"));
        var battery = Assert.Single(result.Batteries);
        Assert.Equal(48000, battery.DesignMwh);
        Assert.Equal(36000, battery.FullChargeMwh);
        Assert.Null(battery.CycleCount);
        var recent = Assert.Single(result.Recent);
        Assert.Equal(31000, recent.RemainingMwh);
        Assert.Equal(2300, recent.DrainedMwh);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
