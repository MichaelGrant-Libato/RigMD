using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class MemoryConsistencyTests
{
    [Theory]
    [InlineData("full", 81.99, 5000d, false)]
    [InlineData("full", 82d, 0d, true)]
    [InlineData("full", 82.01, 0d, true)]
    [InlineData("component", 81.99, 500d, false)]
    [InlineData("component", 82d, 0d, true)]
    [InlineData("component", 74.99, 6000d, false)]
    [InlineData("component", 75d, 1799.99, false)]
    [InlineData("component", 75d, 1800d, false)]
    [InlineData("component", 76d, 1900d, false)]
    [InlineData("component", 0d, 0d, false)]
    public void InitialScanAndRecheck_UseTheSameMemoryPressureConditions(
        string mode, double usage, double browserMb, bool active)
    {
        var hardware = Hardware(usage, browserMb);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = mode, ComponentIds = new[] { "memory" }, Hardware = hardware
        });
        var category = active ? diagnosis.DiagnosedCategory : "High Memory Pressure";
        var recheck = new ResolutionService().CheckResolution(category, hardware,
            componentIds: mode == "component" ? "[\"memory\"]" : null);

        Assert.Equal(active, diagnosis.Proof.Any(p => p.Status is "high" or "elevated"));
        Assert.Equal(active ? "still_active" : "resolved", recheck.resolution_status);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("component")]
    public void TextMemoryWarning_KeepsTheMemoryFindingActiveAtLowRamUsage(string mode)
    {
        var hardware = Hardware(25, 500);
        hardware.ProcessInsights.MemoryLeakWarning = "An application has unusual memory growth.";
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = mode, ComponentIds = new[] { "memory" }, Hardware = hardware
        });
        var recheck = new ResolutionService().CheckResolution(diagnosis.DiagnosedCategory, hardware,
            componentIds: mode == "component" ? "[\"memory\"]" : null);

        Assert.NotEqual("No Active Issue Detected", diagnosis.DiagnosedCategory);
        Assert.Equal("still_active", recheck.resolution_status);
        hardware.ProcessInsights.MemoryLeakWarning = null;
        Assert.Equal("resolved", new ResolutionService().CheckResolution(diagnosis.DiagnosedCategory, hardware,
            componentIds: mode == "component" ? "[\"memory\"]" : null).resolution_status);
    }

    [Theory]
    [InlineData("slow-system", 74.99, false)]
    [InlineData("slow-system", 75d, false)]
    [InlineData("slow-system", 81.99, false)]
    [InlineData("slow-system", 82d, true)]
    [InlineData("stuttering-freezing", 74.99, false)]
    [InlineData("stuttering-freezing", 75d, false)]
    [InlineData("stuttering-freezing", 82d, true)]
    [InlineData("app-crashes", 79.99, false)]
    [InlineData("app-crashes", 80d, false)]
    [InlineData("app-crashes", 82d, true)]
    [InlineData("blue-screen-crash", 92d, false)]
    public void ScenarioRamBoundary_RemainsConsistentWithRecheck(string scenario, double usage, bool active)
    {
        var hardware = Hardware(usage, 5000);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = scenario, Hardware = hardware
        });
        var recheck = new ResolutionService().CheckResolution(diagnosis.DiagnosedCategory, hardware, scenarioId: scenario);

        Assert.Equal(active, diagnosis.DiagnosedCategory != "No Active Issue Detected");
        Assert.Equal(active ? "still_active" : "resolved", recheck.resolution_status);
        if (scenario == "blue-screen-crash")
            Assert.DoesNotContain(recheck.resolution_proof, p => System.Text.Json.JsonSerializer.Serialize(p).Contains("Memory pressure"));
    }

    [Theory]
    [InlineData("slow-system", double.NaN)]
    [InlineData("slow-system", 101d)]
    [InlineData("stuttering-freezing", -1d)]
    [InlineData("app-crashes", double.PositiveInfinity)]
    [InlineData("app-crashes", double.NaN)]
    public void ScenarioWithInvalidRam_CannotConfirmHealthyMemory(string scenario, double usage)
    {
        var hardware = Hardware(usage, 500);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = scenario, Hardware = hardware
        });
        Assert.Equal("Memory telemetry unavailable", diagnosis.DiagnosedCategory);
        Assert.Equal("needs_recheck", new ResolutionService().CheckResolution(
            diagnosis.DiagnosedCategory, hardware, scenarioId: scenario).resolution_status);
    }

    [Theory]
    [InlineData("slow-system", 80d)]
    [InlineData("app-crashes", 80d)]
    public void KnownCpuRisk_RemainsActiveEvenWhenRamIsInvalid(string scenario, double cpuLoad)
    {
        var hardware = Hardware(double.NaN, 500);
        hardware.Cpu.UsagePercent = cpuLoad;
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = scenario, Hardware = hardware
        });
        Assert.NotEqual("No Active Issue Detected", diagnosis.DiagnosedCategory);
        Assert.NotEqual("Memory telemetry unavailable", diagnosis.DiagnosedCategory);
        Assert.Equal("still_active", new ResolutionService().CheckResolution(
            diagnosis.DiagnosedCategory, hardware, scenarioId: scenario).resolution_status);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(101d)]
    public void InvalidRamUsage_IsUnavailableInsteadOfHealthy(double usage)
    {
        var hardware = Hardware(usage, 500);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { "memory" }, Hardware = hardware
        });
        var recheck = new ResolutionService().CheckResolution("High Memory Pressure", hardware,
            componentIds: "[\"memory\"]");

        Assert.Equal("Memory telemetry unavailable", diagnosis.DiagnosedCategory);
        Assert.Contains(diagnosis.Proof, p => p.Label == "Physical Memory (RAM)" && p.Status == "unknown");
        Assert.Equal("needs_recheck", recheck.resolution_status);
    }

    [Fact]
    public void SavedWorkloadCategory_RechecksUsingTheSameCurrentRamPolicy()
    {
        Assert.Equal("resolved", new ResolutionService().CheckResolution(
            "Elevated Memory Pressure From Active Workloads", Hardware(76, 1900)).resolution_status);
    }

    private static HardwareProfileDto Hardware(double usage, double browserMb) => new()
    {
        DeviceErrorsAvailable = true,
        StabilityEvents = new() { QuerySucceeded = true },
        Cpu = new() { Name = "Test CPU", UsagePercent = 10, TemperatureCelsius = 45 },
        Ram = new() { TotalGb = 16, UsedGb = 4, UsagePercent = usage },
        Gpu = new() { Name = "Test GPU", TemperatureCelsius = 40 },
        ProcessInsights = new() { BrowserMemoryMb = browserMb, BrowserProcessCount = 100, BrowserHeavy = false },
        StorageDrives = new() { new() { Model = "Test SSD", UsagePercent = 25 } },
        AllDisks = new() { new() { Drive = "C:\\", UsagePercent = 25 } },
        Network = new() { QuerySucceeded = true, HasActiveAdapter = true, HasIpv4Address = true, HasDefaultGateway = true,
            HasDnsServers = true, DnsResolutionSucceeded = true, PingLatencyMs = 20, PacketLossPercent = 0 }
    };
}
