using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Application;

public class CrashFollowUpSemanticsTests
{
    [Theory]
    [InlineData("full")]
    [InlineData("component")]
    [InlineData("scenario")]
    public async Task MemoryWarningRejectsCleanupRegardlessOfSelectedScope(string mode)
    {
        var finding = new DiagnosticOutput
        {
            DiagnosedCategory = "High Memory Pressure",
            Session = new DiagnosticSession { Answers = new List<SessionAnswer>
            {
                new() { QuestionKey = "diagnosis_mode", AnswerValue = mode },
                new() { QuestionKey = "component_ids", AnswerValue = "[\"memory\",\"storage\"]" },
                new() { QuestionKey = "scenario_id", AnswerValue = "slow-system" }
            } }
        };
        var service = new AutonomousOrchestrator(null!);
        var result = await service.RunExecutionCycleAsync(finding, new(), true, requestedToolName: "clear_temp_files");
        Assert.Null(result.Execution);
        Assert.False(result.Safety!.IsApproved);
    }

    [Theory]
    [InlineData("app-crashes", "application_crash_history")]
    [InlineData("blue-screen-crash", "system_crash_history")]
    public void HistoricalRecordsCannotBeClearedByANoNewRecordsWindow(string scenario, string metric)
    {
        var hw = ScannerEvidenceAuditTests.KnownReadings(); hw.Cpu.TemperatureCelsius = null;
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        var records = scenario == "app-crashes" ? hw.StabilityEvents.ApplicationCrashEvents : hw.StabilityEvents.SystemCrashEvents;
        records.Add(new() { TimeUtc = since.AddHours(-1), EventId = scenario == "app-crashes" ? 1000 : 41 });
        var first = new AutomaticDiagnosisService().Diagnose(new() { Mode = "scenario", ScenarioId = scenario, Hardware = hw, CapturedAt = since });
        var original = first.Proof.Select(p => (object)JsonSerializer.SerializeToElement(p, new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToArray();
        var latest = new ResolutionService().CheckResolution(first.DiagnosedCategory, hw, scenarioId: scenario, sessionCreatedAt: since, originalProof: original);
        Assert.Equal("resolved", latest.resolution_status); // UI: no new matching records, not repaired.
        Assert.Contains("No new matching crash records", latest.resolution_summary);
        Assert.DoesNotContain("Original warning", latest.resolution_summary);
        Assert.DoesNotContain("no longer detected", latest.resolution_summary);
        var row = JsonSerializer.SerializeToElement(latest.resolution_proof).EnumerateArray().Single(p => p.GetProperty("metric_id").GetString() == metric);
        Assert.Equal("since_diagnosis", row.GetProperty("window_kind").GetString());
        Assert.Equal(since, row.GetProperty("window_start").GetDateTimeOffset());
        Assert.Contains(first.Proof, p => p.MetricId == metric && p.Value == "1 found" && p.WindowKind == "rolling_history");
        Assert.Contains(first.Proof, p => p.Label == "OS & Power State" && p.Status == "info");
    }

    [Theory]
    [InlineData("Application crash history requires review")]
    [InlineData("System crash or stop error requires review")]
    [InlineData("Scanner evidence unavailable")]
    [InlineData("No Active Issue Detected")]
    public async Task UnsupportedCauseCannotOfferOrExecuteAGenericRepair_EvenWithConsent(string category)
    {
        var service = new AutonomousOrchestrator(null!);
        var finding = new DiagnosticOutput { DiagnosedCategory = category };
        foreach (var result in new[] { await service.RunDryRunCycleAsync(finding, new()),
            await service.RunExecutionCycleAsync(finding, new(), true, requestedToolName: "run_system_file_checker") })
        {
            Assert.Empty(result.Plan!.PlannedActions);
            Assert.Null(result.Execution);
            Assert.Null(result.ProposedTool);
            Assert.False(result.Safety!.IsApproved);
        }
    }
}
