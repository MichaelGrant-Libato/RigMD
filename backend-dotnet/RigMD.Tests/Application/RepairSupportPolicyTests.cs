using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Application;

public class RepairSupportPolicyTests
{
    [Theory]
    [InlineData("Storage health behavior", true)]
    [InlineData("S.M.A.R.T. warning", true)]
    [InlineData("High Memory Pressure", false)]
    [InlineData("Elevated CPU Utilization", false)]
    [InlineData("Thermal telemetry unavailable", false)]
    public void PhysicalDamageIsNotInferredFromHighWorkloadOrMissingSensors(string category, bool expected)
        => Assert.Equal(expected, RepairSupportPolicy.RequiresHardwareInspection(category));

    [Fact]
    public async Task HardwareWarningCannotExecute_EvenWhenCallingTheOrchestratorDirectlyWithConsent()
    {
        var orchestrator = new AutonomousOrchestrator(null!);
        var diagnostic = new DiagnosticOutput { DiagnosedCategory = "Storage health behavior" };
        var preview = await orchestrator.RunDryRunCycleAsync(diagnostic, new HardwareProfileDto());
        var execution = await orchestrator.RunExecutionCycleAsync(diagnostic, new HardwareProfileDto(),
            userConsentProvided: true, requestedToolName: "clear_temp_files");
        foreach (var result in new[] { preview, execution })
        {
            Assert.True(result.Escalated);
            Assert.False(result.Safety!.IsApproved);
            Assert.Empty(result.Plan!.PlannedActions);
            Assert.Null(result.Execution);
            Assert.Contains("Back up important files", result.RootCauseAnalysis);
        }
    }
}
