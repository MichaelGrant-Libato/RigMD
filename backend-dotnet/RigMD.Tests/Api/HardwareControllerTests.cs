using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Controllers;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Tests.Api;

public class HardwareControllerTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void NetworkQueryAndRouteFailuresRemainDistinctFromAHealthyConnection(bool collected, bool adapter)
    {
        var profile = new HardwareProfileDto { Network = new() { QuerySucceeded = collected, HasActiveAdapter = adapter } };
        var response = Assert.IsType<OkObjectResult>(new HardwareController(new StubProfileService(profile)).GetLiveHardware());
        var network = JsonSerializer.SerializeToElement(response.Value).GetProperty("network");
        Assert.Equal(collected, network.GetProperty("query_succeeded").GetBoolean());
        Assert.Equal(adapter, network.GetProperty("has_active_adapter").GetBoolean());
        Assert.False(network.GetProperty("dns_resolution_succeeded").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UsageSamplingFailure_IsNull_NotMeasuredZero(bool available)
    {
        var profile = new HardwareProfileDto();
        profile.Cpu.UsageAvailable = available;
        profile.Ram.UsageAvailable = available;
        var result = Assert.IsType<OkObjectResult>(new HardwareController(new StubProfileService(profile)).GetLiveHardware());
        var data = JsonSerializer.SerializeToElement(result.Value);
        foreach (var key in new[] { "cpu", "ram" })
        {
            var usage = data.GetProperty(key).GetProperty("usage_percent");
            Assert.Equal(available ? JsonValueKind.Number : JsonValueKind.Null, usage.ValueKind);
            if (available) Assert.Equal(0, usage.GetDouble());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LiveHardware_PreservesCpuTemperatureProvenanceAndUnavailableValues(bool hasReading)
    {
        var capturedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var profile = new HardwareProfileDto();
        profile.Cpu.SetTemperatureReading(hasReading
            ? new CpuTemperatureReading(61.5, @"root\LibreHardwareMonitor", "/intelcpu/0/temperature/1", capturedAt)
            : null);
        var controller = new HardwareController(new StubProfileService(profile));

        var result = Assert.IsType<OkObjectResult>(controller.GetLiveHardware());
        var cpu = JsonSerializer.SerializeToElement(result.Value).GetProperty("cpu");

        if (hasReading)
        {
            Assert.Equal(61.5, cpu.GetProperty("temperature_celsius").GetDouble());
            Assert.Equal(@"root\LibreHardwareMonitor", cpu.GetProperty("temperature_source").GetString());
            Assert.Equal("/intelcpu/0/temperature/1", cpu.GetProperty("temperature_sensor_identifier").GetString());
            Assert.Equal(capturedAt, cpu.GetProperty("temperature_captured_at_utc").GetDateTimeOffset());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, cpu.GetProperty("temperature_celsius").ValueKind);
            Assert.Equal("Unavailable", cpu.GetProperty("temperature_source").GetString());
            Assert.Equal(JsonValueKind.Null, cpu.GetProperty("temperature_sensor_identifier").ValueKind);
            Assert.Equal(JsonValueKind.Null, cpu.GetProperty("temperature_captured_at_utc").ValueKind);
        }
    }

    private sealed class StubProfileService(HardwareProfileDto profile) : IWindowsSystemProfileService
    {
        public HardwareProfileDto GetLiveSystemProfile() => profile;
    }
}
