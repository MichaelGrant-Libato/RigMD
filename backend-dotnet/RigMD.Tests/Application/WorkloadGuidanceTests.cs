using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Ai;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Tests.Application;

public sealed class WorkloadGuidanceTests
{
    [Theory]
    [InlineData("Thermal condition")]
    [InlineData("Battery drain concern")]
    [InlineData("Elevated CPU Utilization")]
    public async Task WorkloadReadingDoesNotAuthorizeAnUnnamedRepair(string category)
    {
        var diagnostic = new DiagnosticOutput { DiagnosedCategory = category };
        var orchestrator = new AutonomousOrchestrator(null!);
        var preview = await orchestrator.RunDryRunCycleAsync(diagnostic, new HardwareProfileDto());
        var execution = await orchestrator.RunExecutionCycleAsync(diagnostic, new HardwareProfileDto(), true, requestedToolName: "terminate_processes");
        foreach (var result in new[] { preview, execution })
        {
            Assert.Empty(result.Plan!.PlannedActions); Assert.Null(result.Execution); Assert.False(result.Safety!.IsApproved);
            Assert.Contains("memory check", result.RootCauseAnalysis, StringComparison.OrdinalIgnoreCase);
        }
    }
    [Theory]
    [InlineData("overheating-loud-fan", "Thermal condition", "inspect_cpu_and_thermals")]
    [InlineData("rapid-battery-drain", "Battery concern", "inspect_battery_and_power")]
    public async Task LocalFallbackReturnsGuidanceRatherThanEmptyAppClosingArguments(string scenario, string category, string observation)
    {
        using var client = new HttpClient();
        var engine = new GeminiReActLlmClient(client, new ConfigurationBuilder().Build(), NullLogger<GeminiReActLlmClient>.Instance);
        var context = new ReActConversationContext { DiagnosisMode = "scenario", ScenarioId = scenario, DiagnosedCategory = category,
            History = [new() { ToolName = observation, ObservationSummary = "Synthetic workload observation", ObservationJson = "{}" }] };
        var decision = await engine.DecideNextTurnAsync(context, [new TerminateProcessesTool(NullLoggerFactory.Instance).GetFunctionDeclaration()]);
        Assert.NotNull(decision.FinalProposal); Assert.Null(decision.FinalProposal.RecommendedToolName);
        Assert.Contains("memory check", decision.FinalProposal.RemediationRationale, StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("overheating-loud-fan")]
    [InlineData("rapid-battery-drain")]
    [InlineData("app-crashes")]
    [InlineData("blue-screen-crash")]
    public void GuidanceOnlyScenarioDoesNotAdvertiseRepairTools(string scenario)
        => Assert.Empty(DiagnosticScopeMapper.GetAllowedRemediationToolsForScenario(scenario));
    [Theory]
    [InlineData("cpu")] [InlineData("thermal")] [InlineData("battery")]
    public void WorkloadComponentsDoNotOfferAppClosingFromThatScope(string component)
        => Assert.Empty(DiagnosticScopeMapper.GetAllowedToolsForComponents([component]).AllowedRemediationTools);
    [Fact]
    public void RealMemoryChecksRetainTheirExistingReviewScope()
    {
        Assert.Contains("terminate_processes", DiagnosticScopeMapper.GetAllowedRemediationTools("component", ["memory"], null, "High Memory Pressure"));
        Assert.Null(RepairSupportPolicy.ReviewOnlyReason("High Memory Pressure"));
    }
    [Theory]
    [InlineData("Thermal condition")]
    [InlineData("Battery concern")]
    [InlineData("Elevated CPU Utilization")]
    public void ASeparateVerifiedStorageFindingInAFullCheckRetainsItsOwnReview(string category)
    {
        var diagnostic = new DiagnosticOutput { DiagnosedCategory = category, Session = new() { Answers =
            [new() { QuestionKey = "diagnosis_mode", AnswerValue = "full" },
             new() { QuestionKey = "proof", AnswerValue = "[{\"metric_id\":\"storage_usage_C:\",\"status\":\"high\",\"value\":\"90% used\",\"evidence_kind\":\"measurement\"}]" }] } };
        var selected = RepairSupportPolicy.SelectStorageFinding(diagnostic, "storage_usage_C:", "clear_temp_files");
        Assert.NotNull(selected); Assert.Null(RepairSupportPolicy.ReviewOnlyReason(selected.DiagnosedCategory));
        Assert.Equal(category, diagnostic.DiagnosedCategory);
    }
}
