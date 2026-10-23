using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class LiveUsageSamplingTests
{
    [Fact]
    public void DirectAppObservation_UsesACompleteNameQuery_NotTheRankedMemoryList()
    {
        var result = SelectedAppObservation.Read("RigMDMissingApp" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, result.ProcessCount);
        Assert.Equal(0, result.MemoryMb);
    }
    [Fact]
    public void LongSamplingGapRequiresNewWarmup_AndPublishesActualWindow()
    {
        var time = new Clock();
        var sampler = new CpuUsageSampler(() => 25, time);
        sampler.Read();
        time.Milliseconds = 1500;
        Assert.Equal(25, sampler.Read());
        Assert.Equal(1.5, sampler.WindowSeconds);
        time.Milliseconds = 8000;
        Assert.Null(sampler.Read());
        Assert.Null(sampler.CapturedAt);
        time.Milliseconds = 9000;
        Assert.Equal(25, sampler.Read());
        Assert.Equal(1, sampler.WindowSeconds);
    }
    [Fact]
    public void NativeFallbackUsesKernelIncludingIdle_WithoutDoubleCountingIdle()
    {
        Assert.Equal(50, CpuUsageSampler.BusyPercent(100, 200, 100, 200, 350, 150));
        Assert.Equal(0, CpuUsageSampler.BusyPercent(100, 200, 100, 300, 400, 100));
        Assert.Equal(100, CpuUsageSampler.BusyPercent(100, 200, 100, 100, 300, 200));
        Assert.Null(CpuUsageSampler.BusyPercent(100, 200, 100, 100, 200, 100));
        Assert.Null(CpuUsageSampler.BusyPercent(100, 200, 100, 0, 300, 200));
        Assert.Null(CpuUsageSampler.BusyPercent(100, 200, 100, 400, 300, 200));
    }
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(Milliseconds);
    }

    [Fact]
    public void WarmupIsUnavailable_AndShortRepeatedCallsDoNotCreateNewReadings()
    {
        var time = new Clock();
        var calls = 0;
        var sampler = new CpuUsageSampler(() => ++calls == 1 ? 100 : 53, time);
        Assert.Null(sampler.Read());
        Assert.Null(sampler.Read());
        Assert.Null(sampler.CapturedAt);
        Assert.Equal(1, calls);
        time.Milliseconds = 1000;
        Assert.Equal(53, sampler.Read());
        Assert.Equal(time.GetUtcNow(), sampler.CapturedAt);
        Assert.Equal(53, sampler.Read());
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(0, 0d)]
    [InlineData(100, 100d)]
    [InlineData(125, null)]
    [InlineData(-1, null)]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    public void RealValuesArePreserved_AndInvalidValuesNeverBecomeZero(double counter, double? expected)
    {
        var time = new Clock();
        var sampler = new CpuUsageSampler(() => counter, time);
        Assert.Null(sampler.Read());
        time.Milliseconds = 1000;
        Assert.Equal(expected, sampler.Read());
        Assert.Equal(expected.HasValue, sampler.CapturedAt.HasValue);
    }

    [Fact]
    public void CounterFailureClearsOldReading_AndRequiresFreshWarmup()
    {
        var time = new Clock();
        var fail = false;
        var sampler = new CpuUsageSampler(() => fail ? throw new InvalidOperationException() : 25, time);
        sampler.Read();
        time.Milliseconds = 1000;
        Assert.Equal(25, sampler.Read());
        fail = true;
        time.Milliseconds = 2000;
        Assert.Null(sampler.Read());
        Assert.Null(sampler.CapturedAt);
        fail = false;
        Assert.Null(sampler.Read());
        time.Milliseconds = 3000;
        Assert.Equal(25, sampler.Read());
    }

    [Theory]
    [InlineData(0UL, 0UL, false, 0d)]
    [InlineData(1024UL, 2048UL, false, 0d)]
    [InlineData(17179869184UL, 4294967296UL, true, 75d)]
    [InlineData(17179869184UL, 17179869184UL, true, 0d)]
    public void PhysicalMemoryUsesAvailableBytes_NotInstalledCapacity(ulong total, ulong available, bool valid, double percent)
    {
        var sample = WmiMemoryProvider.FromPhysicalBytes(total, available);
        Assert.Equal(valid, sample.UsageAvailable);
        if (valid) Assert.Equal(percent, sample.UsagePercent);
    }
}
