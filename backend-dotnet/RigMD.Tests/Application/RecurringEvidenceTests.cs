using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class RecurringEvidenceTests
{
    [Fact]
    public void CrashRecurrenceHistoryIsNotClearedByAShortFollowUpWindow()
    {
        var historical = new { metric_id = "application_crash_history", label = "Application crash records (last 7 days)", value = "2 found", status = "high", window_kind = "rolling_history" };
        var one = Session(1, historical); var two = Session(2, historical);
        two.ResolutionStatus = "resolved"; two.ResolutionCheckedAt = "2026-10-02T00:01:00Z";
        two.ResolutionProof = [new { metric_id = "application_crash_history", label = "Application crash records since diagnosis", value = "0 found", status = "normal", window_kind = "since_diagnosis" }];
        var pattern = Assert.Single(new RecurringPatternService().BuildPatterns([one, two]).patterns);
        Assert.Equal("No new matching records in latest follow-up", pattern.current_condition);
        Assert.DoesNotContain("Not detected", pattern.current_condition);
        Assert.Equal(2, pattern.occurrence_count);
    }

    [Fact]
    public void DeviceWarningIdentityIsIndependentOfListOrder_AndIncludesErrorCode()
    {
        var camera = new DeviceErrorDto { DeviceId = "USB\\Camera", Name = "Camera", ErrorCode = 43 };
        var id = DeviceErrorAttribution.MetricId(camera);
        Assert.Equal(id, DeviceErrorAttribution.MetricId(new() { DeviceId = "usb\\camera", Name = "Renamed camera", ErrorCode = 43 }));
        camera.ErrorCode = 28;
        Assert.NotEqual(id, DeviceErrorAttribution.MetricId(camera));
    }

    [Fact]
    public void StaleCpuSampleIsUnavailable_NotAWarningOrAnAllClear()
    {
        var now = DateTimeOffset.UtcNow;
        var hardware = new HardwareProfileDto { Cpu = new() { UsageAvailable = true, UsagePercent = 100,
            UsageCapturedAtUtc = now.AddSeconds(-8), TemperatureCelsius = 45 } };
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = "component", ComponentIds = ["cpu"], Hardware = hardware, CapturedAt = now });
        var activity = Assert.Single(result.Proof, row => row.MetricId == "cpu_usage");
        Assert.Equal("unknown", activity.Status);
        Assert.Equal("Unavailable", activity.Value);
        Assert.DoesNotContain(result.Proof, row => row.Status == "high");
        Assert.NotEqual("No Active Issue Detected", result.DiagnosedCategory);
    }
    private static object Row(string metric, string status = "high", string? label = null) =>
        new { metric_id = metric, label = label ?? metric, value = "90%", status };
    private static RecurringSessionDto Session(int day, params object[] proof) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc), Proof = proof,
        SymptomType = "Device check", DiagnosedCategory = "High Memory Pressure", ActionCategory = "Troubleshoot"
    };

    [Fact]
    public void SameGenericCheckWithDifferentWarnings_IsNotOneRecurringProblem()
    {
        var result = new RecurringPatternService().BuildPatterns([Session(1, Row("memory_usage")), Session(2, Row("cpu_usage"))]);
        Assert.Empty(result.patterns);
    }

    [Fact]
    public void EveryRepeatedWarningIsRetained_AndRelatedChecksAreNotDoubleCounted()
    {
        var one = Session(1, Row("memory_usage"), Row("cpu_usage"));
        var two = Session(2, Row("memory_usage"), Row("cpu_usage"));
        var result = new RecurringPatternService().BuildPatterns([one, two, one]);
        Assert.Equal(2, result.patterns.Count);
        Assert.All(result.patterns, p => Assert.Equal(2, p.occurrence_count));
        Assert.Equal(2, result.metrics.total_occurrences);
    }

    [Fact]
    public void HealthyAndUnavailableResultsNeverBecomeRepeatedFaults()
    {
        var result = new RecurringPatternService().BuildPatterns([
            Session(1, Row("memory_usage", "normal"), Row("cpu_temperature", "unknown")),
            Session(2, Row("memory_usage", "normal"), Row("cpu_temperature", "unknown"))]);
        Assert.Empty(result.patterns);
    }

    [Fact]
    public void DataRiskOutranksMoreFrequentWorkloadWarnings()
    {
        var sessions = Enumerable.Range(1, 10).Select(day => Session(day, Row("cpu_usage"))).ToList();
        sessions.Add(Session(1, Row("storage_health")));
        sessions.Add(Session(2, Row("storage_health")));
        var result = new RecurringPatternService().BuildPatterns(sessions);
        Assert.Equal("storage_health", result.patterns[0].pattern_key);
        Assert.Equal("Protect files first", result.patterns[0].risk_level);
        Assert.Contains("Back up", result.patterns[0].recommended_next_step);
    }

    [Fact]
    public void AdviceEscalationDoesNotInventWorseningTrend_AndIdsSurviveInputOrder()
    {
        var one = Session(1, Row("memory_usage"));
        var two = Session(2, Row("memory_usage"));
        two.ActionCategory = "Escalate";
        var service = new RecurringPatternService();
        var pattern = Assert.Single(service.BuildPatterns([one, two]).patterns);
        Assert.Equal("Repeated observations", pattern.status);
        Assert.False(pattern.action_escalated);
        Assert.Equal(pattern.id, Assert.Single(service.BuildPatterns([two, one]).patterns).id);
    }

    [Fact]
    public void RechecksUpdateConditionWithoutIncreasingOccurrenceCount()
    {
        var one = Session(1, Row("memory_usage"));
        var two = Session(2, Row("memory_usage"));
        two.ResolutionCheckedAt = "2026-10-02T00:10:00Z";
        two.ResolutionProof = [Row("memory_usage", "normal")];
        var pattern = Assert.Single(new RecurringPatternService().BuildPatterns([one, two]).patterns);
        Assert.Equal(2, pattern.occurrence_count);
        Assert.Equal("Not detected in latest saved check", pattern.current_condition);
        Assert.Contains("do not prove separate failures", pattern.evidence_summary);
    }

    [Fact]
    public void DifferentDeviceNamesAreNotMergedByTheirListIndex()
    {
        var result = new RecurringPatternService().BuildPatterns([
            Session(1, Row("device_error_0", label: "Camera")), Session(2, Row("device_error_0", label: "Audio"))]);
        Assert.Empty(result.patterns);
    }

    [Fact]
    public void LegacyReportsAreExplicitlyUnverified_NotHardwareFaults()
    {
        var pattern = Assert.Single(new RecurringPatternService().BuildPatterns([Session(1), Session(2)]).patterns);
        Assert.Equal("Needs verification", pattern.risk_level);
        Assert.Equal("Not verified", pattern.current_condition);
        Assert.Contains("measurements are missing", pattern.evidence_summary);
    }

    [Theory]
    [InlineData("normal", "no longer detected in this reading")]
    [InlineData("unknown", "could not be verified")]
    public void OriginalWarningComparisonIsSeparateFromNewWarnings(string memoryStatus, string expectedMessage)
    {
        var hardware = new HardwareProfileDto
        {
            Cpu = new() { UsageAvailable = true, UsagePercent = 100 },
            Ram = new() { UsageAvailable = memoryStatus != "unknown", UsagePercent = 70.6, TotalGb = 15.7, UsedGb = 11.1 }
        };
        var result = new ResolutionService().CheckResolution("High Memory Pressure", hardware,
            componentIds: "cpu,memory", diagnosisMode: "component", originalProof: [Row("memory_usage"), Row("cpu_usage", "normal")]);
        Assert.Equal("still_active", result.resolution_status);
        Assert.Contains(expectedMessage, result.resolution_summary);
        Assert.Contains("Other warnings now detected", result.resolution_summary);
    }
}
