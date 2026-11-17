using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class GpuActivityReaderTests
{
    private static readonly DateTimeOffset Captured = DateTimeOffset.Parse("2026-11-17T06:00:00Z");
    private static readonly GpuAdapterIdentity[] Inventory =
    [
        new(1, "Integrated GPU", "Integrated"),
        new(2, "Dedicated GPU", "Dedicated")
    ];

    [Fact]
    public void ProcessesShareAnEngine_WhileOtherEnginesAndAdaptersStaySeparate()
    {
        var result = GpuActivityReader.BuildReadings(Inventory,
        [
            (Engine(1, 0, 0, 1), 20),
            (Engine(1, 0, 0, 2), 30),
            (Engine(1, 0, 1, 1), 40),
            (Engine(1, 1, 0, 1), 45),
            (Engine(2, 0, 0, 1), 80)
        ], Captured);

        Assert.Equal(50, result[0].UsagePercent);
        Assert.Equal("Integrated GPU", result[0].Name);
        Assert.Equal("Integrated", result[0].Type);
        Assert.Equal(80, result[1].UsagePercent);
        Assert.Equal("Dedicated GPU", result[1].Name);
        Assert.All(result, adapter => Assert.Equal(Captured, adapter.CapturedAtUtc));
    }

    [Fact]
    public void BusyThenIdleThenUnavailable_DoesNotRetainOldActivity()
    {
        var busy = GpuActivityReader.BuildReadings(Inventory, [(Engine(1), 60)], Captured);
        var idle = GpuActivityReader.BuildReadings(Inventory, [(Engine(1), 0)], Captured.AddSeconds(1));
        var missing = GpuActivityReader.BuildReadings(Inventory, [], Captured.AddSeconds(2));

        Assert.Equal(60, busy[0].UsagePercent);
        Assert.Equal(0, idle[0].UsagePercent);
        Assert.NotNull(idle[0].CapturedAtUtc);
        Assert.Null(idle[1].UsagePercent);
        Assert.Null(idle[1].CapturedAtUtc);
        Assert.All(missing, adapter => Assert.Null(adapter.UsagePercent));
        Assert.All(missing, adapter => Assert.Null(adapter.CapturedAtUtc));
    }

    [Fact]
    public void InvalidOrUnidentifiedCountersCannotBecomeZeroOrAnotherGpusActivity()
    {
        var result = GpuActivityReader.BuildReadings(Inventory,
        [
            (Engine(1), double.NaN),
            (Engine(1), double.PositiveInfinity),
            (Engine(1), -1),
            (Engine(1), 101),
            ("unknown", 90),
            (Engine(2), 100),
            ("pid_1_luid_0x12345678_0x9abcdef0_phys_0_eng_0_engtype_Copy", 25)
        ], Captured);

        Assert.Null(result[0].UsagePercent);
        Assert.Equal(100, result[1].UsagePercent);
        var unidentified = Assert.Single(result, adapter => adapter.Id == "123456789abcdef0");
        Assert.Equal(25, unidentified.UsagePercent);
        Assert.Equal("Unknown", unidentified.Type);
        Assert.DoesNotContain("Dedicated GPU", unidentified.Name);
    }

    [Fact]
    public void SummedProcessActivityIsBoundedToOneHundredPercent()
    {
        var result = GpuActivityReader.BuildReadings(Inventory,
            [(Engine(1, process: 1), 65), (Engine(1, process: 2), 50)], Captured);
        Assert.Equal(100, result[0].UsagePercent);
    }

    [Fact]
    public void SoftwareRenderingCountersAreExcludedFromPhysicalGpuCards()
    {
        GpuAdapterIdentity[] inventory = [.. Inventory, new(3, "Software renderer", "Unknown", false)];
        var result = GpuActivityReader.BuildReadings(inventory,
            [(Engine(1), 0), (Engine(2), 25), (Engine(3), 80)], Captured);
        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].UsagePercent);
        Assert.Equal(25, result[1].UsagePercent);
    }

    private static string Engine(uint luid, int physical = 0, int engine = 0, int process = 1) =>
        $"pid_{process}_luid_0x00000000_0x{luid:x8}_phys_{physical}_eng_{engine}_engtype_3D";
}
