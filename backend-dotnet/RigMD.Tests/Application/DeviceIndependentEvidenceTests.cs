using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class DeviceIndependentEvidenceTests
{
    public static IEnumerable<object[]> WindowsProfiles()
    {
        foreach (var total in new[] { 4d, 8d, 16d, 64d, 512d })
        foreach (var name in new[] { "Intel processor", "AMD processor", "ARM64 processor", "Virtual processor" })
        foreach (var usage in new[] { 0d, 75d, 81.99, 82d, 100d })
            yield return [total, name, usage];
    }

    [Theory]
    [MemberData(nameof(WindowsProfiles))]
    public void RamPolicyIsIndependentOfDeviceCapacityProcessorAndBrowserSize(double total, string name, double usage)
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings();
        hw.Cpu.Name = name;
        hw.Ram.TotalGb = total; hw.Ram.UsedGb = total * usage / 100; hw.Ram.UsagePercent = usage;
        hw.ProcessInsights = new() { BrowserMemoryMb = total * 100, BrowserProcessCount = 100 };
        foreach (var mode in new[] { "full", "component", "scenario" })
        {
            var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = mode,
                ComponentIds = ["memory"], ScenarioId = "slow-system", Hardware = hw });
            Assert.Equal(usage >= 82 ? "high" : "normal", result.Proof.Single(p => p.MetricId == "memory_usage").Status);
        }
    }

    [Theory]
    [MemberData(nameof(ScannerEvidenceAuditTests.Checks), MemberType = typeof(ScannerEvidenceAuditTests))]
    public void EachScannerReportsEvidenceCoverageWithoutClaimingDiagnosticCertainty(string mode, string id)
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings();
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = mode, ComponentIds = [id], ScenarioId = id, Hardware = hw });
        Assert.Contains(result.ConfidenceLabel, new[] { "Available", "Partial", "Unavailable", "ContextOnly", "NotApplicable" });
        Assert.All(result.Proof, p => Assert.False(string.IsNullOrWhiteSpace(p.EvidenceKind)));
        if (result.Proof.Any(p => p.Status == "unknown"))
            Assert.Contains(result.ConfidenceLabel, new[] { "Partial", "Unavailable" });
        Assert.Contains("not confidence in a cause", result.Explanation);
    }

    [Fact]
    public void MissingTemperatureDoesNotChangeTheSeparateMemoryVerdictAndProofSurvivesRecheck()
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings(); hw.Cpu.TemperatureCelsius = null; hw.Ram.UsagePercent = 90;
        var initial = new AutomaticDiagnosisService().Diagnose(new() { Mode = "component", ComponentIds = ["cpu", "memory"], Hardware = hw });
        Assert.Equal("Partial", initial.ConfidenceLabel);
        hw.Ram.UsagePercent = 60;
        var latest = new ResolutionService().CheckResolution(initial.DiagnosedCategory, hw, componentIds: "[\"cpu\",\"memory\"]",
            originalProof: initial.Proof.Select(p => (object)JsonSerializer.SerializeToElement(p, new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToArray());
        var rows = JsonSerializer.SerializeToElement(latest.resolution_proof).EnumerateArray().ToArray();
        Assert.Equal("normal", rows.Single(p => p.GetProperty("metric_id").GetString() == "memory_usage").GetProperty("status").GetString());
        Assert.Equal("unknown", rows.Single(p => p.GetProperty("metric_id").GetString() == "cpu_temperature").GetProperty("status").GetString());
        Assert.Equal("measurement", rows.Single(p => p.GetProperty("metric_id").GetString() == "memory_usage").GetProperty("evidence_kind").GetString());
        Assert.Equal("windows-review-v2", rows[0].GetProperty("rule_version").GetString());
        Assert.Contains("no longer detected", latest.resolution_summary);
        Assert.DoesNotContain("repaired", latest.resolution_summary);
    }

    [Fact]
    public void IndependentAppWarningDoesNotMakeLowRamUsageHigh()
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings(); hw.ProcessInsights = new() { MemoryLeakWarning = "App warning reported" };
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = "component", ComponentIds = ["memory"], Hardware = hw });
        Assert.Equal("normal", result.Proof.Single(p => p.MetricId == "memory_usage").Status);
        Assert.Equal("high", result.Proof.Single(p => p.MetricId == "memory_app_warning").Status);
        Assert.Equal("Available", result.ConfidenceLabel);
    }

    [Fact]
    public void AbsentBatteryIsNotLowConfidenceAndFailedGraphicsDetectionIsNotHealthy()
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings(); hw.Battery = new() { HasBattery = false, QuerySucceeded = true };
        var service = new AutomaticDiagnosisService();
        Assert.Equal("NotApplicable", service.Diagnose(new() { Mode = "component", ComponentIds = ["battery"], Hardware = hw }).ConfidenceLabel);
        Assert.Equal("not_applicable", new ResolutionService().CheckResolution("Component Not Present", hw, componentIds: "[\"battery\"]").resolution_status);
        hw.Gpu = new() { HasGpu = false };
        var gpu = service.Diagnose(new() { Mode = "component", ComponentIds = ["gpu"], Hardware = hw });
        Assert.Equal("Unavailable", gpu.ConfidenceLabel);
        Assert.DoesNotContain(gpu.Proof, p => p.Status == "normal");
    }

    [Fact]
    public void EvidenceAvailabilityIsNotIncreasedByAWarningOrItsMagnitude()
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings();
        var service = new AutomaticDiagnosisService();
        string Rating() => service.Diagnose(new() { Mode = "component", ComponentIds = ["cpu"], Hardware = hw }).ConfidenceLabel;
        Assert.Equal("Available", Rating());
        hw.Cpu.UsagePercent = 100;
        Assert.Equal("Available", Rating());
        hw.Cpu.TemperatureCelsius = null;
        Assert.Equal("Partial", Rating());
        hw.Cpu.UsageAvailable = false;
        Assert.Equal("Unavailable", Rating());
    }
}
