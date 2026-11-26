using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class CpuTemperatureSamplerTests
{
    [Fact]
    public void LibreComputerEnablesOnlyCpuHardware()
    {
        var computer = LibreCpuTemperatureProbe.CreateCpuComputer();
        Assert.True(computer.IsCpuEnabled);
        Assert.False(computer.IsGpuEnabled);
        Assert.False(computer.IsMotherboardEnabled);
        Assert.False(computer.IsMemoryEnabled);
        Assert.False(computer.IsStorageEnabled);
        Assert.False(computer.IsNetworkEnabled);
        Assert.False(computer.IsControllerEnabled);
        Assert.False(computer.IsBatteryEnabled);
        Assert.False(computer.IsPsuEnabled);
        Assert.False(computer.IsPowerMonitorEnabled);
    }

    [Theory]
    [InlineData("/intelcpu/0", "CPU Package")]
    [InlineData("/amdcpu/0", "Core (Tctl/Tdie)")]
    [InlineData("/amdcpu/0", "Core (Tdie)")]
    public void PackageOrDieSensorWinsOverOtherCpuReadings(string parent, string label)
    {
        var selected = CpuTemperatureSensorReader.SelectReading([
            Sample(75, parent, "Core #1", 0),
            Sample(65, parent, label, 1),
            Sample(95, parent, "Core (Tctl)", 2),
            new("Motherboard", "/lpc/0", "/lpc/0/temperature/0", "Temperature", 90, "CPU Package")
        ], CpuTemperatureSampler.Source);
        Assert.Equal(65, selected!.Celsius);
        Assert.Equal(parent + "/temperature/1", selected.SensorIdentifier);
        Assert.Contains(label, selected.Source);
    }

    [Fact]
    public void SharedSamplingIsRateLimitedAndPreservesTheCaptureTime()
    {
        var clock = new Clock();
        var probe = new Probe();
        using var sampler = new CpuTemperatureSampler(() => probe, () => true, clock);
        var first = sampler.Read();
        clock.Milliseconds = 1499;
        Assert.Same(first, sampler.Read());
        Assert.Equal(1, probe.ReadCount);
        clock.Milliseconds = 1500;
        Assert.Equal(clock.GetUtcNow(), sampler.Read()!.CapturedAtUtc);
        Assert.Equal(2, probe.ReadCount);
    }

    [Fact]
    public async Task ConcurrentReadersShareOneProbeAndSamplingWindow()
    {
        var probe = new Probe();
        var opened = 0;
        using var sampler = new CpuTemperatureSampler(() => { opened++; return probe; }, () => true, new Clock());
        var readings = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(sampler.Read)));
        Assert.Equal(1, opened);
        Assert.Equal(1, probe.ReadCount);
        Assert.All(readings, reading => Assert.Same(readings[0], reading));
    }

    [Fact]
    public void MissingDriverDoesNotOpenHardware_AndCanRecoverWhenAccessBecomesAvailable()
    {
        var clock = new Clock();
        var available = false;
        var probe = new Probe();
        var opened = 0;
        using var sampler = new CpuTemperatureSampler(() => { opened++; return probe; }, () => available, clock);
        Assert.Null(sampler.Read());
        Assert.Equal(0, opened);
        available = true;
        clock.Milliseconds = 1500;
        Assert.Equal(55, sampler.Read()!.Celsius);
        Assert.Equal(1, opened);
        available = false;
        clock.Milliseconds = 3000;
        Assert.Null(sampler.Read());
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void SensorFailureClearsTheOldReadingAndRetriesWithANewProbe()
    {
        var clock = new Clock();
        var first = new Probe();
        var recovered = new Probe { Celsius = 62 };
        var opened = 0;
        using var sampler = new CpuTemperatureSampler(() => ++opened == 1 ? first : recovered, () => true, clock);
        Assert.Equal(55, sampler.Read()!.Celsius);
        first.Fail = true;
        clock.Milliseconds = 1500;
        Assert.Null(sampler.Read());
        Assert.Equal(1, first.DisposeCount);
        clock.Milliseconds = 3000;
        Assert.Null(sampler.Read());
        Assert.Equal(1, opened);
        clock.Milliseconds = 31500;
        Assert.Equal(62, sampler.Read()!.Celsius);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void ResetAndDisposeReleaseHardwareAndDoNotReuseTheCachedSample()
    {
        var clock = new Clock();
        var first = new Probe();
        var next = new Probe { Celsius = null };
        var opened = 0;
        var sampler = new CpuTemperatureSampler(() => ++opened == 1 ? first : next, () => true, clock);
        Assert.NotNull(sampler.Read());
        sampler.Reset();
        Assert.Equal(1, first.DisposeCount);
        Assert.Null(sampler.Read());
        Assert.Equal(1, next.DisposeCount);
        sampler.Dispose();
        clock.Milliseconds = 60000;
        Assert.Null(sampler.Read());
        Assert.Equal(2, opened);
    }

    private static HardwareTemperatureSample Sample(double value, string parent, string label, int index) =>
        new("Cpu", parent, $"{parent}/temperature/{index}", "Temperature", value, label);

    private sealed class Probe : ICpuTemperatureProbe
    {
        public int ReadCount;
        public int DisposeCount;
        public double? Celsius = 55;
        public bool Fail;
        public IReadOnlyList<HardwareTemperatureSample> Read()
        {
            ReadCount++;
            if (Fail) throw new InvalidOperationException("Sensor unavailable");
            return [new("Cpu", "/amdcpu/0", "/amdcpu/0/temperature/0", "Temperature", Celsius, "Core (Tctl/Tdie)")];
        }
        public void Dispose() => DisposeCount++;
    }

    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(Milliseconds);
    }
}
