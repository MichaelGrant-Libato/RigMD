using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class DeviceErrorAttributionTests
{
    [Theory]
    [InlineData("USB")]
    [InlineData("Net")]
    [InlineData("MEDIA")]
    [InlineData("")]
    public void GpuOnly_DoesNotBlameUnrelatedOrUnclassifiedDeviceErrors(string pnpClass)
    {
        var hardware = Hardware(Error("GPU-sounding USB device", pnpClass));
        var diagnosis = Diagnose(hardware, "gpu");

        Assert.Equal("No Active Issue Detected", diagnosis.DiagnosedCategory);
        Assert.DoesNotContain(diagnosis.Proof, p => p.Value.Contains("PnP Device Error"));
        var recheck = new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"gpu\"]");
        Assert.Equal("resolved", recheck.resolution_status);
        Assert.DoesNotContain("all hardware drivers", JsonSerializer.Serialize(recheck.resolution_proof), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Display", "")]
    [InlineData("display", "")]
    [InlineData("", "{4D36E968-E325-11CE-BFC1-08002BE10318}")]
    [InlineData("", "4d36e968-e325-11ce-bfc1-08002be10318")]
    public void GpuOnly_ReportsActualDisplayAdapterErrors_InDiagnosisAndRecheck(string pnpClass, string classGuid)
    {
        var hardware = Hardware(Error("USB controller", "USB"), Error("Adapter failure", pnpClass, classGuid));
        var diagnosis = Diagnose(hardware, "gpu");

        Assert.Equal("Device status warning", diagnosis.DiagnosedCategory);
        Assert.Contains("Adapter failure", diagnosis.PrimaryResult);
        Assert.DoesNotContain("USB controller", diagnosis.PrimaryResult);
        Assert.Contains(diagnosis.Proof, p => p.Label == "Adapter failure" && p.Value == "Device Manager code 43" && p.Status == "high");
        var recheck = new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"gpu\"]");
        Assert.Equal("still_active", recheck.resolution_status);
    }

    [Fact]
    public void DisplayAndGpuComponents_UseTheirOwnDeviceClasses()
    {
        var hardware = Hardware(Error("Monitor failure", "Monitor"));
        Assert.Equal("No Active Issue Detected", Diagnose(hardware, "gpu").DiagnosedCategory);
        Assert.Equal("Device status warning", Diagnose(hardware, "display").DiagnosedCategory);
        Assert.Equal("resolved", new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"gpu\"]").resolution_status);
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"display\"]").resolution_status);
    }

    [Fact]
    public void DisplayScenario_IgnoresUsbErrors_ButNotMonitorErrorsOrMissingDisplays()
    {
        var hardware = Hardware(Error("USB controller", "USB"));
        // Detection is not a picture/cable test, even when device status has no warning.
        Assert.Equal("Scanner evidence unavailable", Scenario(hardware, "no-display").DiagnosedCategory);
        Assert.Equal("needs_recheck", new ResolutionService().CheckResolution("Display driver behavior", hardware, scenarioId: "no-display").resolution_status);

        hardware.DeviceErrors.Add(Error("Monitor failure", "", "{4d36e96e-e325-11ce-bfc1-08002be10318}"));
        Assert.Equal("Device status warning", Scenario(hardware, "no-display").DiagnosedCategory);
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Display driver behavior", hardware, scenarioId: "no-display").resolution_status);

        hardware.DeviceErrors.Clear();
        hardware.ConnectedDisplays = 0;
        Assert.Equal("Scanner evidence unavailable", Scenario(hardware, "no-display").DiagnosedCategory);
        Assert.Equal("needs_recheck", new ResolutionService().CheckResolution("Display driver behavior", hardware, scenarioId: "no-display").resolution_status);
    }

    [Fact]
    public void FullSystemAndDriverChecks_RetainUnrelatedAndUnclassifiedErrors()
    {
        var hardware = Hardware(Error("USB controller", "USB"), Error("Unclassified failure", ""));
        var full = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput { Mode = "full", Hardware = hardware });
        Assert.Equal("Device status warning", full.DiagnosedCategory);
        Assert.Equal("Device status warning", Diagnose(hardware, "drivers").DiagnosedCategory);
        Assert.Equal("Device status warning", Diagnose(hardware, "gpu", "drivers").DiagnosedCategory);
        Assert.Equal("Device status warning", Scenario(hardware, "driver-error").DiagnosedCategory);
        Assert.Equal(2, full.Proof.Count(p => p.MetricId.StartsWith("device_error_") && p.Status == "high"));
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"gpu\",\"drivers\"]").resolution_status);
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Driver conflict", hardware, scenarioId: "driver-error").resolution_status);
    }

    [Fact]
    public void GpuPlusCpuCheck_DoesNotImportAnUnrelatedDriverFailure()
    {
        var hardware = Hardware(Error("USB controller", "USB"));
        Assert.Equal("No Active Issue Detected", Diagnose(hardware, "gpu", "cpu").DiagnosedCategory);
        hardware.DeviceErrors.Add(Error("Adapter failure", "Display"));
        var diagnosis = Diagnose(hardware, "gpu", "cpu");
        Assert.Equal("Device status warning", diagnosis.DiagnosedCategory);
        var service = new ResolutionService();
        Assert.Equal("still_active", service.CheckResolution(diagnosis.DiagnosedCategory, hardware, componentIds: "[\"gpu\",\"cpu\"]").resolution_status);
        hardware.DeviceErrors.RemoveAt(1);
        Assert.Equal("resolved", service.CheckResolution(diagnosis.DiagnosedCategory, hardware, componentIds: "[\"gpu\",\"cpu\"]").resolution_status);
    }

    [Fact]
    public void GpuScope_UsesClassGuidRatherThanAContradictoryFriendlyClassName()
    {
        var hardware = Hardware(Error("Display adapter", "Display", "{4d36e972-e325-11ce-bfc1-08002be10318}"));
        Assert.Equal("No Active Issue Detected", Diagnose(hardware, "gpu").DiagnosedCategory);
        Assert.Equal("resolved", new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"gpu\"]").resolution_status);
    }

    [Fact]
    public void ZeroErrorCodes_DoNotBecomeActiveGraphicsErrors()
    {
        var healthyAdapter = Error("Healthy adapter", "Display");
        healthyAdapter.ErrorCode = 0;
        var hardware = Hardware(healthyAdapter);
        Assert.Equal("No Active Issue Detected", Diagnose(hardware, "gpu").DiagnosedCategory);
        Assert.Equal("resolved", new ResolutionService().CheckResolution("Driver conflict", hardware, componentIds: "[\"gpu\"]").resolution_status);
    }

    private static AutomaticDiagnosisResult Diagnose(HardwareProfileDto hardware, params string[] components) =>
        new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput { Mode = "component", ComponentIds = components, Hardware = hardware });

    private static AutomaticDiagnosisResult Scenario(HardwareProfileDto hardware, string scenario) =>
        new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput { Mode = "scenario", ScenarioId = scenario, Hardware = hardware });

    private static DeviceErrorDto Error(string name, string pnpClass, string classGuid = "") =>
        new() { Name = name, DeviceId = name, PnpClass = pnpClass, ClassGuid = classGuid, ErrorCode = 43 };

    private static HardwareProfileDto Hardware(params DeviceErrorDto[] errors) => new()
    {
        DeviceErrorsAvailable = true,
        Cpu = new() { Name = "Test CPU", UsagePercent = 10, TemperatureCelsius = 45 },
        Ram = new() { TotalGb = 16, UsedGb = 4, UsagePercent = 25 },
        Gpu = new() { HasGpu = true, Name = "Test GPU", Driver = "Test driver", TemperatureCelsius = 45 },
        ConnectedDisplays = 1,
        PrimaryStorageType = "SSD",
        StorageDrives = new() { new() { Model = "Test SSD", UsagePercent = 25 } },
        DeviceErrors = errors.ToList()
    };
}
