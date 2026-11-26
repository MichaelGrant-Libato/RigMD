using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class WindowsGpuTemperatureReaderTests
{
    [Theory]
    [InlineData(0, 510u, 51d)]
    [InlineData(0, 725u, 72.5d)]
    [InlineData(0, 1500u, 150d)]
    [InlineData(0, 0u, null)]
    [InlineData(0, 1501u, null)]
    [InlineData(0, uint.MaxValue, null)]
    [InlineData(unchecked((int)0xc000000d), 510u, null)]
    public void NativeValuesUseDocumentedUnits_AndUnsupportedResultsStayUnavailable(int status, uint raw, double? expected)
    {
        Assert.Equal(expected, WindowsGpuTemperatureReader.DecodeTemperature(status, raw));
    }

    [Fact]
    public void TemperatureIsIndependentOfUsage_AndStaysWithItsAdapter()
    {
        GpuAdapterReading[] adapters =
        [
            new("0000000000000001", "Integrated", "Integrated", 90, DateTimeOffset.UtcNow),
            new("0000000000000002", "Radeon", "Dedicated", null, null)
        ];
        var readings = GpuActivityReader.ReadTemperatures(adapters, luid => luid == 2 ? 51 : null);
        Assert.Null(readings[0].TemperatureCelsius);
        Assert.Equal(90, readings[0].UsagePercent);
        Assert.Equal("Unavailable", readings[0].TemperatureSource);
        Assert.Null(readings[0].TemperatureCapturedAtUtc);
        Assert.Equal(51, readings[1].TemperatureCelsius);
        Assert.Null(readings[1].UsagePercent);
        Assert.Equal(WindowsGpuTemperatureReader.Source, readings[1].TemperatureSource);
        Assert.NotNull(readings[1].TemperatureCapturedAtUtc);

        var missing = GpuActivityReader.ReadTemperatures(readings, _ => null);
        Assert.All(missing, adapter => Assert.Null(adapter.TemperatureCelsius));
        Assert.All(missing, adapter => Assert.Null(adapter.TemperatureCapturedAtUtc));
    }

    [Fact]
    public void SelectedGpuCannotBorrowAnotherAdaptersTemperature_OrKeepAReadingAfterSensorLoss()
    {
        var profile = new GpuStatsDto { Name = "Radeon" };
        GpuAdapterReading integrated = new("i", "Integrated", "Integrated", 40, DateTimeOffset.UtcNow)
        {
            TemperatureCelsius = 65,
            TemperatureSource = WindowsGpuTemperatureReader.Source,
            TemperatureCapturedAtUtc = DateTimeOffset.UtcNow
        };
        GpuAdapterReading radeon = new("d", "Radeon", "Dedicated", 0, DateTimeOffset.UtcNow)
        {
            TemperatureCelsius = 51,
            TemperatureSource = WindowsGpuTemperatureReader.Source,
            TemperatureCapturedAtUtc = DateTimeOffset.UtcNow
        };
        profile.SetAdapterReadings([integrated, radeon]);
        Assert.Equal("d", profile.AdapterId);
        Assert.Equal(51, profile.TemperatureCelsius);

        profile.SetAdapterReadings([integrated, radeon with { TemperatureCelsius = null, TemperatureSource = "Unavailable", TemperatureCapturedAtUtc = null }]);
        Assert.Equal("d", profile.AdapterId);
        Assert.Null(profile.TemperatureCelsius);
        Assert.Null(profile.TemperatureCapturedAtUtc);

        profile.SetAdapterReadings([integrated]);
        Assert.Null(profile.AdapterId);
        Assert.Null(profile.TemperatureCelsius);

        profile.SetAdapterReadings([radeon, radeon with { Id = "another-radeon" }]);
        Assert.Null(profile.AdapterId);
        Assert.Null(profile.TemperatureCelsius);
    }

    [Fact]
    public void ExistingVendorReadingOnlyFillsTheSameAdaptersMissingTemperatureWhileFresh()
    {
        var now = DateTimeOffset.UtcNow;
        GpuAdapterReading fallback = new("nvidia", "NVIDIA GPU", "Dedicated", null, null)
        {
            TemperatureCelsius = 65,
            TemperatureSource = "NVIDIA display-driver utility",
            TemperatureCapturedAtUtc = now
        };
        GpuAdapterReading other = new("integrated", "Integrated", "Integrated", 30, now);
        var readings = GpuActivityReader.ApplyTemperatureFallback([other, fallback with { TemperatureCelsius = null }], fallback, now);
        Assert.Null(readings[0].TemperatureCelsius);
        Assert.Equal(65, readings[1].TemperatureCelsius);
        Assert.Null(readings[1].UsagePercent);

        var preferred = fallback with { TemperatureCelsius = 60, TemperatureSource = WindowsGpuTemperatureReader.Source };
        Assert.Equal(60, GpuActivityReader.ApplyTemperatureFallback([preferred], fallback, now)[0].TemperatureCelsius);
        var missing = fallback with { TemperatureCelsius = null, TemperatureCapturedAtUtc = null };
        Assert.Null(GpuActivityReader.ApplyTemperatureFallback([missing], fallback, now.AddSeconds(6))[0].TemperatureCelsius);
        Assert.Null(GpuActivityReader.ApplyTemperatureFallback([missing], fallback, now.AddSeconds(-1))[0].TemperatureCelsius);
    }
}
