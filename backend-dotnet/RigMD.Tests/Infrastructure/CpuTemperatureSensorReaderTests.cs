using System.Text.Json;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Tools.Diagnostic;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class CpuTemperatureSensorReaderTests
{
    [Fact]
    public void SelectReading_UsesCpuParentAndSensorType_NotFriendlyNamesOrOtherHardware()
    {
        var reading = CpuTemperatureSensorReader.SelectReading(new[]
        {
            Sample(95, "GpuNvidia", "/gpu-nvidia/0", "/gpu-nvidia/0/temperature/0"),
            Sample(90, "SuperIO", "/lpc/0", "/lpc/0/temperature/0"),
            Sample(92, "Cpu", "/intelcpu/0", "/gpu-nvidia/0/temperature/0"),
            Sample(99, sensorType: "Load"),
            Sample(56.25), Sample(61.75, identifier: "/intelcpu/0/temperature/1")
        }, @"root\LibreHardwareMonitor");

        Assert.NotNull(reading);
        Assert.Equal(61.8, reading.Celsius);
        Assert.Equal(@"root\LibreHardwareMonitor", reading.Source);
        Assert.Equal("/intelcpu/0/temperature/1", reading.SensorIdentifier);
        Assert.InRange(reading.CapturedAtUtc, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(151d)]
    public void InvalidOrMissingValue_IsUnavailable(double? value)
    {
        Assert.Null(CpuTemperatureSensorReader.SelectReading(new[] { Sample(value) }, "test"));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(110d)]
    [InlineData(140d)]
    public void MeasuredZeroAndHighTemperatures_AreNotReplacedByAnEstimate(double value)
    {
        Assert.Equal(value, CpuTemperatureSensorReader.SelectReading(new[] { Sample(value) }, "test")!.Celsius);
    }

    [Fact]
    public void MissingProvidersOrSensors_ReturnNullWithoutWorkloadFallback()
    {
        Assert.Null(CpuTemperatureSensorReader.Read(_ => throw new UnauthorizedAccessException()));
        Assert.Null(CpuTemperatureSensorReader.Read(_ => Array.Empty<HardwareTemperatureSample>()));
        Assert.Null(CpuTemperatureSensorReader.Read(_ => new[] { Sample(60, "Motherboard") }));
    }

    [Fact]
    public void FallbackProvider_MustStillIdentifyTheCpuSensor()
    {
        var reading = CpuTemperatureSensorReader.Read(source => source.EndsWith("LibreHardwareMonitor")
            ? throw new InvalidOperationException()
            : new[] { Sample(53, "CPU", "/amdcpu/0", "/amdcpu/0/temperature/0") });
        Assert.Equal(@"root\OpenHardwareMonitor", reading!.Source);
        Assert.Equal(53, reading.Celsius);
    }

    [Fact]
    public void NewUnavailableReading_ClearsOldValueAndProvenance()
    {
        var cpu = new CpuStatsDto();
        cpu.SetTemperatureReading(new CpuTemperatureReading(57, "test sensor", "/intelcpu/0/temperature/0", DateTimeOffset.UtcNow));
        Assert.Equal(57, cpu.TemperatureCelsius);
        cpu.SetTemperatureReading(CpuTemperatureSensorReader.Read(_ => Array.Empty<HardwareTemperatureSample>()));
        Assert.Null(cpu.TemperatureCelsius);
        Assert.Equal("Unavailable", cpu.TemperatureSource);
        Assert.Null(cpu.TemperatureSensorIdentifier);
        Assert.Null(cpu.TemperatureCapturedAtUtc);
    }

    [Fact]
    public async Task CpuTool_ReturnsProvenance_AndDoesNotReuseAnOldValueAfterSensorLoss()
    {
        var provider = new StubCpuProvider();
        var monitor = new StubMonitor();
        var tool = new InspectCpuAndThermalsTool(provider, monitor);
        using var arguments = JsonDocument.Parse("{}");
        var measured = await tool.ExecuteAsync(arguments.RootElement);
        using (var payload = JsonDocument.Parse(measured.DataJson!))
        {
            Assert.Equal(49, payload.RootElement.GetProperty("temperatureCelsius").GetDouble());
            Assert.Equal("test CPU sensor", payload.RootElement.GetProperty("temperatureSource").GetString());
            Assert.Equal("/intelcpu/0/temperature/0", payload.RootElement.GetProperty("temperatureSensorIdentifier").GetString());
        }

        monitor.Reading = null;
        var unavailable = await tool.ExecuteAsync(arguments.RootElement);
        using var missingPayload = JsonDocument.Parse(unavailable.DataJson!);
        Assert.Equal(JsonValueKind.Null, missingPayload.RootElement.GetProperty("temperatureCelsius").ValueKind);
        Assert.Equal("Unavailable", missingPayload.RootElement.GetProperty("temperatureSource").GetString());
        Assert.Contains("not estimated", unavailable.Summary);
    }

    private static HardwareTemperatureSample Sample(double? value, string hardwareType = "Cpu",
        string parent = "/intelcpu/0", string identifier = "/intelcpu/0/temperature/0", string sensorType = "Temperature") =>
        new(hardwareType, parent, identifier, sensorType, value);

    private sealed class StubCpuProvider : ICpuProvider
    {
        public CpuStatsDto GetCpuStats() => new() { Name = "Test CPU", UsagePercent = 10, TemperatureCelsius = 70 };
    }

    private sealed class StubMonitor : IHardwareMonitorService
    {
        public CpuTemperatureReading? Reading { get; set; } =
            new(49, "test CPU sensor", "/intelcpu/0/temperature/0", DateTimeOffset.UtcNow);
        public void Tick() { }
        public CpuTemperatureReading? GetCpuTemperatureReading() => Reading;
        public double? GetCpuTemperature() => 99;
        public double? GetGpuTemperature() => null;
        public double? GetGpuDedicatedMemoryUsedGb() => null;
        public double? GetGpuDedicatedMemoryTotalGb() => null;
        public double? GetCpuLoad() => 10;
        public double? GetRamLoad() => null;
        public double? GetGpuLoad() => null;
    }
}
