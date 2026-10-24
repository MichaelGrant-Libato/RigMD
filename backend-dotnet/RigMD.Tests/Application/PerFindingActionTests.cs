using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Application;

public class PerFindingActionTests
{
    private const string Warning = "[{\"metric_id\":\"memory_usage\",\"status\":\"high\",\"value\":\"84.7%\"}]";

    private static DiagnosticOutput Diagnostic(string proof, string? latest = null) => new()
    {
        DiagnosedCategory = "High Memory Pressure",
        Session = new() { Answers = new List<SessionAnswer>
        {
            new() { QuestionKey = "proof", AnswerValue = proof },
            new() { QuestionKey = "resolution_proof", AnswerValue = latest ?? "[]" },
            new() { QuestionKey = "diagnosis_mode", AnswerValue = "component" },
            new() { QuestionKey = "component_ids", AnswerValue = "[\"memory\"]" }
        } }
    };

    [Theory]
    [InlineData(4, 82, true)]
    [InlineData(16, 84.7, true)]
    [InlineData(512, 99, true)]
    [InlineData(16, 81.99, false)]
    [InlineData(16, double.NaN, false)]
    [InlineData(16, 101, false)]
    public void MemoryCheckActionRequiresCurrentEvidence(double capacity, double usage, bool allowed)
    {
        var hardware = new HardwareProfileDto { Ram = new() { TotalGb = capacity, UsagePercent = usage } };
        Assert.Equal(allowed, RepairSupportPolicy.SelectedAppBlockReason(Diagnostic(Warning), hardware) == null);
    }

    [Theory]
    [InlineData("full", "[]", "", true)]
    [InlineData("component", "[\"memory\"]", "", true)]
    [InlineData("component", "[\"cpu\",\"ram\"]", "", true)]
    [InlineData("component", "[\"cpu\"]", "", false)]
    [InlineData("component", "[\"storage\"]", "", false)]
    [InlineData("component", "[\"memory\"", "", false)]
    [InlineData("scenario", "[]", "slow-system", true)]
    [InlineData("scenario", "[]", "stuttering-freezing", true)]
    [InlineData("scenario", "[]", "app-crashes", false)]
    [InlineData("scenario", "[]", "blue-screen-crash", false)]
    [InlineData("scenario", "[]", "network-problem", false)]
    [InlineData("unknown", "[\"memory\"]", "", false)]
    public void SavedScopeControlsMemoryActions_NotTheHighestPriorityCategory(string mode, string components, string scenario, bool allowed)
    {
        var diagnostic = Diagnostic(Warning);
        foreach (var (key, value) in new[] { ("diagnosis_mode", mode), ("component_ids", components), ("scenario_id", scenario) })
        {
            var answer = diagnostic.Session.Answers.FirstOrDefault(a => a.QuestionKey == key);
            if (answer == null) diagnostic.Session.Answers.Add(new() { QuestionKey = key, AnswerValue = value });
            else answer.AnswerValue = value;
        }
        Assert.Equal(allowed, RepairSupportPolicy.SelectedAppBlockReason(diagnostic, new() { Ram = new() { UsagePercent = 90 } }) == null);
        Assert.Equal(allowed, RepairSupportPolicy.ScopeBlockReason(diagnostic, "terminate_processes") == null);
    }

    [Fact]
    public void AnEmptyExplicitComponentScopeDoesNotFallBackToCategoryTools()
    {
        Assert.Empty(RigMD.Application.Services.Autonomy.DiagnosticScopeMapper.GetAllowedRemediationTools("component", [], null, "OS performance degradation"));
        Assert.Empty(RigMD.Application.Services.Autonomy.DiagnosticScopeMapper.GetAllowedRemediationTools("scenario", [], "unsupported", "Elevated Storage Utilization"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("[null]")]
    [InlineData("[{\"metric_id\":\"application_crash_history\",\"status\":\"high\",\"value\":\"2 found\"}]")]
    [InlineData("[{\"metric_id\":\"memory_usage\",\"status\":\"unknown\",\"value\":\"Unavailable\"}]")]
    [InlineData("[{\"metric_id\":\"memory_usage\",\"status\":\"high\",\"value\":\"78.8%\"}]")]
    public void CrashHistoryOrMissingEvidenceCannotUnlockAppClosing(string proof)
        => Assert.NotNull(RepairSupportPolicy.SelectedAppBlockReason(Diagnostic(proof), new() { Ram = new() { UsagePercent = 90 } }));

    [Theory]
    [InlineData("[{\"metric_id\":\"memory_usage\",\"status\":\"normal\",\"value\":\"62%\"}]")]
    [InlineData("[{\"metric_id\":\"memory_usage\",\"status\":\"unknown\",\"value\":\"Unavailable\"}]")]
    [InlineData("corrupt")]
    public void NewerSavedEvidenceTakesPrecedenceOverTheOriginalWarning(string latest)
        => Assert.NotNull(RepairSupportPolicy.SelectedAppBlockReason(Diagnostic(Warning, latest), new() { Ram = new() { UsagePercent = 90 } }));

    [Fact]
    public void UnsupportedUsageAndDriveWarningsRemainBlocked()
    {
        var hardware = new HardwareProfileDto { Ram = new() { UsagePercent = 90, UsageAvailable = false } };
        var finding = Diagnostic(Warning);
        Assert.NotNull(RepairSupportPolicy.SelectedAppBlockReason(finding, hardware));
        hardware.Ram.UsageAvailable = true;
        finding.DiagnosedCategory = "Storage health behavior";
        Assert.NotNull(RepairSupportPolicy.SelectedAppBlockReason(finding, hardware));
    }
}
