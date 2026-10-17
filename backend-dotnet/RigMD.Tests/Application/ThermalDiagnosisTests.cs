using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class ThermalDiagnosisTests
{
    [Theory]
    [InlineData(10d)]
    [InlineData(95d)]
    public void MissingSensor_DoesNotBecomeNormalThermalsOrWorkloadEstimatedHeat(double load)
    {
        var hardware = Hardware(null, load);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = "overheating-loud-fan", Hardware = hardware
        });
        Assert.Equal("Thermal telemetry unavailable", diagnosis.DiagnosedCategory);
        Assert.Equal("Low", diagnosis.ConfidenceLabel);
        Assert.Contains("could not be verified", diagnosis.PrimaryResult);
        Assert.Contains(diagnosis.Proof, p => p.Value.Contains("CPU temperature unavailable"));
    }

    [Fact]
    public void ThermalComponent_WithNoSensor_HasUnknownProofInsteadOfHealthyTemperature()
    {
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { "thermal" }, Hardware = Hardware(null)
        });
        Assert.Equal("Thermal telemetry unavailable", diagnosis.DiagnosedCategory);
        Assert.Contains(diagnosis.Proof, p => p.Label == "Processor (CPU)" && p.Status == "unknown");
        var recheck = new ResolutionService().CheckResolution(diagnosis.DiagnosedCategory, Hardware(null), componentIds: "[\"thermal\"]");
        Assert.Equal("needs_recheck", recheck.resolution_status);
        Assert.Contains("unknown", JsonSerializer.Serialize(recheck.resolution_proof));
    }

    [Fact]
    public void MissingTemperature_DoesNotResolveEvenWhenWorkloadIsLow()
    {
        var result = new ResolutionService().CheckResolution("Thermal condition", Hardware(null), scenarioId: "overheating-loud-fan");
        Assert.Equal("needs_recheck", result.resolution_status);
        Assert.Contains("unavailable", result.resolution_summary);
        Assert.DoesNotContain("Nominal", JsonSerializer.Serialize(result.resolution_proof));
    }

    [Fact]
    public void ThrottlingReported_StillRequiresAttentionWhenTemperatureIsUnavailable()
    {
        var hardware = Hardware(null);
        hardware.Cpu.IsThermallyThrottling = true;
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = "overheating-loud-fan", Hardware = hardware
        });
        Assert.Equal("Thermal condition", result.DiagnosedCategory);
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Thermal condition", hardware).resolution_status);
    }

    [Theory]
    [InlineData(84.9, "No Active Issue Detected", "resolved")]
    [InlineData(85d, "Thermal condition", "still_active")]
    [InlineData(110d, "Thermal condition", "still_active")]
    public void MeasuredTemperatureBoundary_AgreesBetweenScenarioAndRecheck(double temperature, string category, string status)
    {
        var hardware = Hardware(temperature);
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = "overheating-loud-fan", Hardware = hardware
        });
        Assert.Equal(category, result.DiagnosedCategory);
        Assert.Equal(status, new ResolutionService().CheckResolution("Thermal condition", hardware, scenarioId: "overheating-loud-fan").resolution_status);
    }

    [Fact]
    public void HighWorkload_WithMeasuredNormalTemperature_DoesNotProveOverheating()
    {
        var hardware = Hardware(50, 95);
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = "overheating-loud-fan", Hardware = hardware
        });
        Assert.Equal("No Active Issue Detected", result.DiagnosedCategory);
        Assert.Equal("resolved", new ResolutionService().CheckResolution("Thermal condition", hardware, scenarioId: "overheating-loud-fan").resolution_status);
    }

    [Fact]
    public void CpuLoadOnlyRecheck_DoesNotRequireAnUnrelatedTemperatureSensor()
    {
        var result = new ResolutionService().CheckResolution("Elevated CPU Utilization", Hardware(null), componentIds: "[\"cpu\"]");
        Assert.Equal("resolved", result.resolution_status);
        Assert.DoesNotContain("temperature", result.resolution_summary);
    }

    [Theory]
    [InlineData("component")]
    [InlineData("scenario")]
    public void MeasuredGpuHeat_RemainsGpuEvidence_WhenCpuSensorIsMissing(string mode)
    {
        var hardware = Hardware(null);
        hardware.Gpu.TemperatureCelsius = 90;
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = mode, ScenarioId = "overheating-loud-fan", ComponentIds = new[] { "thermal" }, Hardware = hardware
        });
        Assert.Equal("Thermal condition", result.DiagnosedCategory);
        Assert.Contains("90", result.PrimaryResult);
        Assert.Contains("unavailable", result.PrimaryResult);
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Thermal condition", hardware).resolution_status);
    }

    private static HardwareProfileDto Hardware(double? temperature, double load = 10) => new()
    {
        Cpu = new() { Name = "Test CPU", UsagePercent = load, TemperatureCelsius = temperature,
            TemperatureSource = temperature.HasValue ? "test CPU sensor" : "Unavailable" },
        Gpu = new() { Name = "Test GPU", HasGpu = true, TemperatureCelsius = 40 },
        Ram = new() { TotalGb = 16, UsedGb = 4, UsagePercent = 25 },
        StorageDrives = new() { new() { Model = "Test SSD", UsagePercent = 25 } }
    };
}
