using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class CpuLoadConsistencyTests
{
    [Theory]
    [InlineData("full", 79.9, false)]
    [InlineData("full", 79.99, false)]
    [InlineData("full", 80d, true)]
    [InlineData("full", 80.1, true)]
    [InlineData("full", 89.9, true)]
    [InlineData("full", 90d, true)]
    [InlineData("component", 79.9, false)]
    [InlineData("component", 79.99, false)]
    [InlineData("component", 80d, true)]
    [InlineData("component", 80.1, true)]
    [InlineData("component", 89.9, true)]
    [InlineData("component", 90d, true)]
    public void ElevatedCpu_InitialScanAndRecheckAgreeAtTheSameLoad(string mode, double load, bool active)
    {
        var hardware = Hardware(load);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = mode, ComponentIds = new[] { "cpu" }, Hardware = hardware
        });
        var recheck = new ResolutionService().CheckResolution("Elevated CPU Utilization", hardware,
            componentIds: mode == "component" ? "[\"cpu\"]" : null);

        Assert.Equal(active, diagnosis.Proof.Any(p => p.MetricId == "cpu_usage" && p.Status == "high"));
        Assert.Equal(active ? "still_active" : "resolved", recheck.resolution_status);
    }

    [Theory]
    [InlineData(85d, "still_active")]
    [InlineData(90d, "still_active")]
    public void CpuLoadConsistencyChange_PreservesTheSeparateBatteryDrainCheck(double load, string status)
    {
        var hardware = Hardware(load);
        hardware.Battery = new() { HasBattery = true, ChargePercent = 50, EstimatedChargeRemaining = 50 };
        var result = new ResolutionService().CheckResolution("Battery issue", hardware, scenarioId: "rapid-battery-drain");

        Assert.Equal(status, result.resolution_status);
    }

    public static IEnumerable<object[]> ScenarioBoundaries()
    {
        foreach (var (scenario, threshold) in new[]
        {
            ("slow-system", 80d), ("stuttering-freezing", 80d), ("slow-boot", 80d),
            ("app-crashes", 80d), ("blue-screen-crash", 80d)
        })
        {
            yield return new object[] { scenario, threshold - 0.1, false };
            yield return new object[] { scenario, threshold, true };
            yield return new object[] { scenario, threshold + 0.1, true };
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioBoundaries))]
    public void ScenarioCpuBoundary_IsPreservedDuringRecheck(string scenario, double load, bool active)
    {
        var hardware = Hardware(load);
        var diagnosis = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario", ScenarioId = scenario, Hardware = hardware
        });
        var recheck = new ResolutionService().CheckResolution(diagnosis.DiagnosedCategory, hardware,
            scenarioId: scenario);

        Assert.Equal(active, diagnosis.Proof.Any(p => p.Status is "high" or "elevated"));
        Assert.Equal(active ? "still_active" : scenario == "slow-boot" ? "needs_recheck" : "resolved", recheck.resolution_status);
    }

    private static HardwareProfileDto Hardware(double load) => new()
    {
        DeviceErrorsAvailable = true,
        Cpu = new() { Name = "Test CPU", UsagePercent = load, TemperatureCelsius = 45,
            TemperatureSource = "test CPU sensor" },
        Gpu = new() { Name = "Test GPU", HasGpu = true, TemperatureCelsius = 40 },
        Ram = new() { TotalGb = 16, UsedGb = 4, UsagePercent = 25 },
        StorageDrives = new() { new() { Model = "Test SSD", SizeGb = 256, UsedGb = 64, UsagePercent = 25 } },
        AllDisks = new() { new() { Drive = "C:\\", TotalGb = 256, UsedGb = 64, UsagePercent = 25 } },
        Network = new() { QuerySucceeded = true, HasActiveAdapter = true, HasIpv4Address = true, HasDefaultGateway = true,
            HasDnsServers = true, DnsResolutionSucceeded = true, PingLatencyMs = 20, PacketLossPercent = 0 },
        StabilityEvents = new() { QuerySucceeded = true }
    };
}
