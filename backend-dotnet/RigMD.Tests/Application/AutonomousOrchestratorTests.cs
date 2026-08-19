using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Application;

public class AutonomousOrchestratorTests
{
    [Fact]
    public async Task RunDryRunCycleAsync_UsesDryRunExecutorOnly()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor);

        var diagnostic = CreateDiagnostic();
        var hardware = CreateHardware();

        var result = await orchestrator.RunDryRunCycleAsync(
            diagnostic,
            hardware);

        Assert.True(dryRunExecutor.WasCalled);
        Assert.False(realExecutor.WasCalled);

        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.Success);
        Assert.Contains("DRY RUN", result.Execution.Summary);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_UsesRealExecutorOnly()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor);

        var diagnostic = CreateDiagnostic();
        var hardware = CreateHardware();

        var result = await orchestrator.RunExecutionCycleAsync(
            diagnostic,
            hardware);

        Assert.False(dryRunExecutor.WasCalled);
        Assert.True(realExecutor.WasCalled);

        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.Success);
        Assert.Contains("REAL EXECUTION", result.Execution.Summary);
    }

    [Fact]
    public async Task RunDryRunCycleAsync_WhenSafetyRejectsPlan_DoesNotCallAnyExecutor()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new RejectingSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor);

        var result = await orchestrator.RunDryRunCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.False(dryRunExecutor.WasCalled);
        Assert.False(realExecutor.WasCalled);

        Assert.NotNull(result.Safety);
        Assert.False(result.Safety.IsApproved);
        Assert.Null(result.Execution);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenConsentRequiredAndNoConsent_RejectsExecution()
    {
        var action = CreateAction();
        var planner = new FakePlanner(action);
        var safetyPolicy = new RequiresConsentSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware(),
            userConsentProvided: false);

        Assert.False(realExecutor.WasCalled);
        Assert.NotNull(result.Safety);
        Assert.False(result.Safety.IsApproved);
        Assert.Contains("requires explicit user consent", result.Safety.RejectionReason);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenConsentRequiredAndConsentGiven_ExecutesAction()
    {
        var action = CreateAction();
        var planner = new FakePlanner(action);
        var safetyPolicy = new RequiresConsentSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware(),
            userConsentProvided: true);

        Assert.True(realExecutor.WasCalled);
        Assert.NotNull(result.Safety);
        Assert.True(result.Safety.IsApproved);
        Assert.True(result.Execution.Success);
    }

    private static RemediationActionDef CreateAction()
    {
        return new RemediationActionDef
        {
            Id = "test_action",
            Name = "Test Action",
            Category = "OS performance degradation",
            RiskLevel = "Low",
            IsReversible = true
        };
    }

    private static DiagnosticOutput CreateDiagnostic()
    {
        return new DiagnosticOutput
        {
            DiagnosedCategory = "OS performance degradation",
            ActionCategory = "Maintain",
            ConfidenceLabel = "High"
        };
    }

    private static HardwareProfileDto CreateHardware()
    {
        return new HardwareProfileDto
        {
            OsVersion = "Windows 11"
        };
    }

    private sealed class FakePlanner : IRemediationPlanner
    {
        private readonly RemediationActionDef _action;

        public FakePlanner(RemediationActionDef action)
        {
            _action = action;
        }

        public RemediationPlan CreatePlan(DiagnosticOutput diagnosticOutput)
        {
            return new RemediationPlan
            {
                PlannedActions = new List<RemediationActionDef>
                {
                    _action
                },
                StrategyReasoning = "Test plan"
            };
        }
    }

    private sealed class FakeSafetyPolicy : ISafetyPolicy
    {
        public SafetyEvaluation Evaluate(
            RemediationPlan plan,
            HardwareProfileDto systemState)
        {
            return new SafetyEvaluation
            {
                IsApproved = true
            };
        }
    }

    private sealed class RejectingSafetyPolicy : ISafetyPolicy
    {
        public SafetyEvaluation Evaluate(
            RemediationPlan plan,
            HardwareProfileDto systemState)
        {
            return new SafetyEvaluation
            {
                IsApproved = false,
                RejectionReason = "Rejected for test."
            };
        }
    }

    private sealed class RequiresConsentSafetyPolicy : ISafetyPolicy
    {
        public SafetyEvaluation Evaluate(
            RemediationPlan plan,
            HardwareProfileDto systemState)
        {
            return new SafetyEvaluation
            {
                IsApproved = true,
                RequiresUserConfirmation = true
            };
        }
    }

    private sealed class FakeDryRunExecutor : IDryRunRemediationExecutor
    {
        public bool WasCalled { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            RemediationActionDef action)
        {
            WasCalled = true;

            return Task.FromResult(new ExecutionResult
            {
                Success = true,
                Summary = "DRY RUN: simulated execution"
            });
        }
    }

    private sealed class FakeRealExecutor : IRemediationExecutor
    {
        public bool WasCalled { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            RemediationActionDef action)
        {
            WasCalled = true;

            return Task.FromResult(new ExecutionResult
            {
                Success = true,
                Summary = "REAL EXECUTION: test executor"
            });
        }
    }
}