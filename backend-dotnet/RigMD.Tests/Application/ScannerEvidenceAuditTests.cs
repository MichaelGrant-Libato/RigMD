using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Application;

public class ScannerEvidenceAuditTests
{
    public static TheoryData<string, string> Checks => new()
    {
        { "component", "cpu" }, { "component", "memory" }, { "component", "gpu" }, { "component", "storage" },
        { "component", "os" }, { "component", "startup" }, { "component", "drivers" }, { "component", "thermal" },
        { "component", "battery" }, { "component", "network" }, { "component", "display" }, { "component", "peripherals" }, { "component", "audio" },
        { "scenario", "slow-system" }, { "scenario", "slow-boot" }, { "scenario", "blue-screen-crash" },
        { "scenario", "driver-error" }, { "scenario", "no-display" }, { "scenario", "overheating-loud-fan" },
        { "scenario", "network-problem" }, { "scenario", "app-crashes" }, { "scenario", "stuttering-freezing" },
        { "scenario", "storage-problem" }, { "scenario", "rapid-battery-drain" }
    };

    [Theory]
    [MemberData(nameof(Checks))]
    public void EveryCheck_MissingEvidenceIsNotAnAllClear(string mode, string id)
    {
        var hardware = new HardwareProfileDto
        {
            Cpu = new() { UsageAvailable = false }, Ram = new() { UsageAvailable = false },
            DeviceErrorsAvailable = false, Network = new() { QuerySucceeded = false },
            StabilityEvents = new() { QuerySucceeded = false }, Battery = new() { QuerySucceeded = false }
        };
        var result = Diagnose(mode, id, hardware);
        Assert.NotEqual("No Active Issue Detected", result.DiagnosedCategory);
        Assert.Contains(result.Proof, p => p.Status == "unknown");
        Assert.DoesNotContain(result.Proof, p => p.Status is "high" or "elevated");
        Assert.Equal("needs_recheck", Recheck(mode, id, hardware).resolution_status);
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public void EveryCheck_SameEvidenceAgreesAcrossInitialAndRecheck(string mode, string id)
    {
        var hardware = KnownReadings();
        var initial = Diagnose(mode, id, hardware);
        var latest = Recheck(mode, id, hardware);
        Assert.DoesNotContain(initial.Proof, p => p.Status is "high" or "elevated");
        var expected = initial.Proof.Any(p => p.Status == "unknown") ? "needs_recheck" : "resolved";
        Assert.Equal(expected, latest.resolution_status);
        var evidence = JsonSerializer.SerializeToElement(latest.resolution_proof).EnumerateArray().ToArray();
        foreach (var reading in initial.Proof.Where(p => !string.IsNullOrEmpty(p.MetricId)))
            Assert.Contains(evidence, p => p.GetProperty("metric_id").GetString() == reading.MetricId &&
                p.GetProperty("value").GetString() == reading.Value && p.GetProperty("status").GetString() == reading.Status);
        Assert.DoesNotContain("operating normally", initial.PrimaryResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleFailuresOnOneNetwork_AreAllRetainedAndScoped()
    {
        var hw = KnownReadings();
        hw.Network.HasDefaultGateway = false;
        hw.Network.DnsResolutionSucceeded = false;
        hw.Network.PingLatencyMs = 170;
        hw.Network.PacketLossPercent = 25;
        hw.Ram.UsagePercent = 95;
        var result = Diagnose("scenario", "network-problem", hw);
        foreach (var id in new[] { "network_gateway", "network_dns", "network_latency", "network_loss" })
            Assert.Contains(result.Proof, p => p.MetricId == id && p.Status == "high");
        Assert.DoesNotContain(result.Proof, p => p.MetricId == "memory_usage");
        Assert.Equal("still_active", Recheck("scenario", "network-problem", hw).resolution_status);
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public void EveryCheck_RetainsRealWarningsEvenWithIncompleteCoverage(string mode, string id)
    {
        var hw = KnownReadings();
        hw.Cpu.UsagePercent = 95; hw.Cpu.TemperatureCelsius = 95; hw.Ram.UsagePercent = 95;
        hw.Gpu.TemperatureCelsius = 95; hw.AllDisks[0].UsagePercent = 95;
        hw.StorageDrives[0].IsFailingSmart = true;
        hw.Battery!.EstimatedChargeRemaining = 5;
        hw.Network.DnsResolutionSucceeded = false; hw.Network.PingLatencyMs = 180; hw.Network.PacketLossPercent = 25;
        hw.DeviceErrors = new[] { "Display", "Monitor", "MEDIA", "USB" }.Select(c => new DeviceErrorDto { Name = c, PnpClass = c, ErrorCode = 43 }).ToList();
        hw.StabilityEvents.ApplicationCrashEvents.Add(new() { EventId = 1000, TimeUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
        hw.StabilityEvents.SystemCrashEvents.Add(new() { EventId = 41, TimeUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
        var result = Diagnose(mode, id, hw);
        Assert.Contains(result.Proof, p => p.Status is "high" or "elevated");
        Assert.NotEqual("No Active Issue Detected", result.DiagnosedCategory);
        Assert.Equal("still_active", Recheck(mode, id, hw).resolution_status);
    }

    [Fact]
    public void FullScan_RetainsConcurrentWarningsAndUnknownCoverageWithoutInventingAFix()
    {
        var hw = KnownReadings();
        hw.Cpu.UsagePercent = 95; hw.Ram.UsagePercent = 95; hw.Cpu.TemperatureCelsius = null;
        hw.StorageDrives[0].IsFailingSmart = true; hw.AllDisks[0].UsagePercent = 95;
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = "full", Hardware = hw });
        foreach (var id in new[] { "cpu_usage", "memory_usage", "storage_usage", "storage_health" })
            Assert.Contains(result.Proof, p => p.MetricId == id && p.Status == "high");
        Assert.Contains(result.Proof, p => p.MetricId == "cpu_temperature" && p.Status == "unknown");
        Assert.Equal("Escalate", result.ActionCategory);
        Assert.Contains("hardware inspection", result.RecommendedNextStep);
    }

    [Theory]
    [InlineData(149, "normal")]
    [InlineData(150, "high")]
    public void NetworkLatencyPolicy_IsTheSameInComponentAndScenario(int ping, string status)
    {
        var hw = KnownReadings(); hw.Network.PingLatencyMs = ping;
        Assert.Equal(status, Diagnose("component", "network", hw).Proof.Single(p => p.MetricId == "network_latency").Status);
        Assert.Equal(status, Diagnose("scenario", "network-problem", hw).Proof.Single(p => p.MetricId == "network_latency").Status);
    }

    [Theory]
    [InlineData("audio", "MEDIA", "USB")]
    [InlineData("peripherals", "USB", "MEDIA")]
    public void DeviceFamilies_DoNotImportUnrelatedWarnings(string component, string included, string excluded)
    {
        var hw = KnownReadings();
        hw.DeviceErrors = [new() { Name = "Selected device", PnpClass = included, ErrorCode = 28 },
            new() { Name = "Unrelated device", PnpClass = excluded, ErrorCode = 43 }];
        var result = Diagnose("component", component, hw);
        Assert.Contains(result.Proof, p => p.Label == "Selected device" && p.Status == "high");
        Assert.DoesNotContain(result.Proof, p => p.Label == "Unrelated device");
        Assert.Equal("Device status warning", result.DiagnosedCategory);
    }

    [Theory]
    [InlineData("slow-boot", "startup_impact")]
    [InlineData("rapid-battery-drain", "battery_drain")]
    public void AHealthySnapshotDoesNotVerifyAnUnmeasuredScenario(string scenario, string missingId)
    {
        var result = Diagnose("scenario", scenario, KnownReadings());
        Assert.Contains(result.Proof, p => p.MetricId == missingId && p.Status == "unknown");
        Assert.NotEqual("No Active Issue Detected", result.DiagnosedCategory);
        Assert.DoesNotContain("failure", result.PrimaryResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoricalErrorsAreNotRecountedAsNewAndFailedCoverageIsNotZero()
    {
        var hw = KnownReadings(); var since = DateTimeOffset.UtcNow.AddMinutes(-10);
        hw.StabilityEvents.ApplicationCrashEvents = [new() { TimeUtc = since.AddMinutes(-2), EventId = 1000 }];
        var clear = new ResolutionService().CheckResolution("Application crash history requires review", hw,
            scenarioId: "app-crashes", sessionCreatedAt: since);
        Assert.Equal("resolved", clear.resolution_status);
        hw.StabilityEvents.QuerySucceeded = false;
        Assert.Equal("needs_recheck", new ResolutionService().CheckResolution("Application crash history requires review", hw,
            scenarioId: "app-crashes", sessionCreatedAt: since).resolution_status);
        hw.StabilityEvents.ApplicationCrashEvents.Add(new() { TimeUtc = since.AddMinutes(1), EventId = 1000 });
        var partial = new ResolutionService().CheckResolution("Application crash history requires review", hw,
            scenarioId: "app-crashes", sessionCreatedAt: since);
        Assert.Equal("still_active", partial.resolution_status);
        Assert.Contains("unknown", JsonSerializer.Serialize(partial.resolution_proof));
    }

    [Theory]
    [InlineData("Unknown", false)] [InlineData("Starting", false)] [InlineData("Service", false)]
    [InlineData("OK", false)] [InlineData(null, false)] [InlineData("Pred Fail", true)]
    [InlineData("Degraded", true)] [InlineData("Error", true)]
    public void DriveStatus_IsNotInterpretedAsASmartFailureByDefault(string? status, bool warning) =>
        Assert.Equal(warning, WmiStorageProvider.ReportsHealthWarning(status));

    [Fact]
    public void UnsupportedSelectionNeverInventsADiagnosis()
    {
        foreach (var mode in new[] { "component", "scenario", "unsupported" })
        {
            var result = Diagnose(mode, "unknown-input", KnownReadings());
            Assert.Equal("Scanner evidence unavailable", result.DiagnosedCategory);
            Assert.Single(result.Proof);
            Assert.Equal("scanner_scope", result.Proof[0].MetricId);
        }
    }

    [Fact]
    public void ConfirmedAbsentBatteryDoesNotCreateAMissingDrainTestInAMixedCheck()
    {
        var hw = KnownReadings(); hw.Battery = new() { QuerySucceeded = true, HasBattery = false };
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = "component", ComponentIds = ["cpu", "battery"], Hardware = hw });
        Assert.DoesNotContain(result.Proof, p => p.MetricId == "battery_drain");
        Assert.Equal("No Active Issue Detected", result.DiagnosedCategory);
        Assert.Contains("skipped", result.IncidentalWarning);
    }

    private static AutomaticDiagnosisResult Diagnose(string mode, string id, HardwareProfileDto hardware) =>
        new AutomaticDiagnosisService().Diagnose(new() { Mode = mode, ComponentIds = [id], ScenarioId = id, Hardware = hardware });

    private static ResolutionResultDto Recheck(string mode, string id, HardwareProfileDto hardware) =>
        new ResolutionService().CheckResolution("Saved category must not change scope", hardware,
            scenarioId: mode == "scenario" ? id : null, componentIds: mode == "component" ? $"[\"{id}\"]" : null, diagnosisMode: mode);

    internal static HardwareProfileDto KnownReadings() => new()
    {
        DeviceErrorsAvailable = true, Cpu = new() { UsageAvailable = true, UsagePercent = 20, TemperatureCelsius = 45, Name = "CPU" },
        Gpu = new() { Name = "GPU", HasGpu = true, TemperatureCelsius = 40 },
        Ram = new() { UsageAvailable = true, TotalGb = 16, UsedGb = 4, UsagePercent = 25 },
        AllDisks = [new() { Drive = "C:", UsagePercent = 25, TotalGb = 256, UsedGb = 64 }],
        StorageDrives = [new() { Model = "SSD", UsagePercent = 25 }],
        Battery = new() { QuerySucceeded = true, ChargeAvailable = true, HasBattery = true, EstimatedChargeRemaining = 75, ChargePercent = 75 },
        Network = new() { QuerySucceeded = true, HasActiveAdapter = true, HasIpv4Address = true, HasDefaultGateway = true,
            HasDnsServers = true, DnsResolutionSucceeded = true, PingLatencyMs = 20, PacketLossPercent = 0 },
        StabilityEvents = new() { QuerySucceeded = true }, ConnectedDisplays = 1
    };
}
