using System.Collections.Generic;
using System.Threading.Tasks;
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
        var verificationService = new FakeVerificationService();
        var rollbackManager = new FakeRollbackManager();
        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunDryRunCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.True(dryRunExecutor.WasCalled);
        Assert.False(realExecutor.WasCalled);
        Assert.Equal(0, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.Success);
        Assert.Contains("DRY RUN", result.Execution.Summary);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.Completed,
            attempt.State);

        Assert.Null(result.Verification);
        Assert.False(result.Escalated);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_UsesRealExecutorOnly()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Resolved
        };

        var rollbackManager = new FakeRollbackManager();
        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.False(dryRunExecutor.WasCalled);
        Assert.True(realExecutor.WasCalled);
        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.Success);

        Assert.Contains(
            "REAL EXECUTION",
            result.Execution.Summary);

        Assert.Equal(
            VerificationStatus.Resolved,
            result.Verification);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.Resolved,
            attempt.State);

        Assert.False(result.Escalated);
    }

    [Fact]
    public async Task RunDryRunCycleAsync_WhenSafetyRejectsPlan_DoesNotCallAnyExecutor()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new RejectingSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();
        var verificationService = new FakeVerificationService();
        var rollbackManager = new FakeRollbackManager();
        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunDryRunCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.False(dryRunExecutor.WasCalled);
        Assert.False(realExecutor.WasCalled);
        Assert.Equal(0, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.NotNull(result.Safety);
        Assert.False(result.Safety.IsApproved);
        Assert.Null(result.Execution);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.SafetyRejected,
            attempt.State);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenConsentRequiredAndNoConsent_RejectsExecution()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new RequiresConsentSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();
        var verificationService = new FakeVerificationService();
        var rollbackManager = new FakeRollbackManager();
        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware(),
            userConsentProvided: false);

        Assert.False(realExecutor.WasCalled);
        Assert.False(dryRunExecutor.WasCalled);
        Assert.Equal(0, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.NotNull(result.Safety);
        Assert.False(result.Safety.IsApproved);

        Assert.Contains(
            "requires explicit user consent",
            result.Safety.RejectionReason);

        Assert.Null(result.Execution);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.AwaitingConsent,
            attempt.State);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenConsentRequiredAndConsentGiven_ExecutesAction()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new RequiresConsentSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Resolved
        };

        var rollbackManager = new FakeRollbackManager();
        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware(),
            userConsentProvided: true);

        Assert.False(dryRunExecutor.WasCalled);
        Assert.True(realExecutor.WasCalled);
        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.NotNull(result.Safety);
        Assert.True(result.Safety.IsApproved);

        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.Success);

        Assert.Equal(
            VerificationStatus.Resolved,
            result.Verification);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.Resolved,
            attempt.State);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenVerificationIsUnresolvedWithoutRollback_EscalatesWithoutPivot()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Unresolved
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = false
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.True(realExecutor.WasCalled);
        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.Equal(
            VerificationStatus.Unresolved,
            result.Verification);

        Assert.True(result.Escalated);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.Unresolved,
            attempt.State);

        Assert.Contains(
            "no verified rollback handler",
            attempt.Notes);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenVerificationIsWorseWithoutRollback_EscalatesWithoutPivot()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Worse
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = false
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.True(realExecutor.WasCalled);
        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.Equal(
            VerificationStatus.Worse,
            result.Verification);

        Assert.True(result.Escalated);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.Worse,
            attempt.State);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenVerificationIsUnknown_EscalatesWithoutRollbackOrPivot()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Unknown
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = true
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.True(realExecutor.WasCalled);
        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.Equal(
            VerificationStatus.Unknown,
            result.Verification);

        Assert.True(result.Escalated);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.VerificationUnknown,
            attempt.State);

        Assert.Null(attempt.RollbackResult);

        Assert.Contains(
            "Further autonomous remediation was stopped",
            attempt.Notes);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenExecutionFails_DoesNotRunVerificationOrRollback()
    {
        var action = CreateAction();

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();

        var realExecutor = new FakeRealExecutor
        {
            ShouldSucceed = false
        };

        var verificationService = new FakeVerificationService();

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = true
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.True(realExecutor.WasCalled);
        Assert.Equal(0, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(1, pivotEngine.CallCount);

        Assert.NotNull(result.Execution);
        Assert.False(result.Execution.Success);

        Assert.Null(result.Verification);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.ExecutionFailed,
            attempt.State);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenUnresolvedAndIrreversible_EscalatesWithoutRollbackOrPivot()
    {
        var action = CreateAction();
        action.IsReversible = false;

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Unresolved
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = true
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.True(result.Escalated);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.Unresolved,
            attempt.State);

        Assert.Null(attempt.RollbackResult);

        Assert.Contains(
            "irreversible",
            attempt.Notes);

        Assert.Contains(
            "Further autonomous remediation was stopped",
            attempt.Notes);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenUnresolvedAndRollbackSupported_RollsBackThenPivots()
    {
        var action = CreateAction();
        action.IsReversible = true;

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Unresolved
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = true,
            RollbackShouldSucceed = true
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(1, rollbackManager.CallCount);
        Assert.Equal(1, pivotEngine.CallCount);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.RolledBack,
            attempt.State);

        Assert.NotNull(attempt.RollbackResult);
        Assert.True(attempt.RollbackResult.Success);

        Assert.Contains(
            "Pivoted to next action",
            attempt.Notes);

        Assert.True(result.Escalated);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenWorseAndRollbackFails_EscalatesWithoutPivot()
    {
        var action = CreateAction();
        action.IsReversible = true;

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Worse
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = true,
            RollbackShouldSucceed = false
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(1, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.True(result.Escalated);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.RollbackFailed,
            attempt.State);

        Assert.NotNull(attempt.RollbackResult);
        Assert.False(attempt.RollbackResult.Success);

        Assert.Contains(
            "Rollback failed",
            attempt.Notes);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_WhenUnknownAndRollbackSupported_DoesNotRollbackOrPivot()
    {
        var action = CreateAction();
        action.IsReversible = true;

        var planner = new FakePlanner(action);
        var safetyPolicy = new FakeSafetyPolicy();
        var dryRunExecutor = new FakeDryRunExecutor();
        var realExecutor = new FakeRealExecutor();

        var verificationService = new FakeVerificationService
        {
            StatusToReturn = VerificationStatus.Unknown
        };

        var rollbackManager = new FakeRollbackManager
        {
            CanRollbackResult = true
        };

        var pivotEngine = new FakePivotEngine();

        var orchestrator = new AutonomousOrchestrator(
            planner,
            safetyPolicy,
            dryRunExecutor,
            realExecutor,
            verificationService,
            rollbackManager,
            pivotEngine);

        var result = await orchestrator.RunExecutionCycleAsync(
            CreateDiagnostic(),
            CreateHardware());

        Assert.Equal(1, verificationService.CallCount);
        Assert.Equal(0, rollbackManager.CallCount);
        Assert.Equal(0, pivotEngine.CallCount);

        Assert.True(result.Escalated);

        Assert.Single(result.Attempts);

        var attempt = result.Attempts[0];

        Assert.Equal(
            RemediationAttemptState.VerificationUnknown,
            attempt.State);

        Assert.Null(attempt.RollbackResult);
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

        public FakePlanner(
            RemediationActionDef action)
        {
            _action = action;
        }

        public Task<RemediationPlan> CreatePlanAsync(
            DiagnosticOutput diagnosticOutput)
        {
            return Task.FromResult(new RemediationPlan
            {
                PlannedActions = new List<RemediationActionDef>
                {
                    _action
                },
                StrategyReasoning = "Test plan"
            });
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

            return Task.FromResult(
                new ExecutionResult
                {
                    Success = true,
                    Summary = "DRY RUN: simulated execution"
                });
        }
    }

    private sealed class FakeRealExecutor : IRemediationExecutor
    {
        public bool WasCalled { get; private set; }

        public bool ShouldSucceed { get; set; } = true;

        public Task<ExecutionResult> ExecuteAsync(
            RemediationActionDef action)
        {
            WasCalled = true;

            return Task.FromResult(
                new ExecutionResult
                {
                    Success = ShouldSucceed,
                    Summary = ShouldSucceed
                        ? "REAL EXECUTION: test executor"
                        : "REAL EXECUTION: simulated failure"
                });
        }
    }

    private sealed class FakeVerificationService : IVerificationService
    {
        public int CallCount { get; private set; }

        public VerificationStatus StatusToReturn { get; set; }
            = VerificationStatus.Resolved;

        public Task<VerificationStatus> VerifyAsync(
            RemediationActionDef action,
            ExecutionResult executionResult,
            HardwareProfileDto currentSystemState)
        {
            CallCount++;

            return Task.FromResult(
                StatusToReturn);
        }
    }

    private sealed class FakeRollbackManager : IRollbackManager
    {
        public int CallCount { get; private set; }

        public bool CanRollbackResult { get; set; }

        public bool RollbackShouldSucceed { get; set; } = true;

        public bool CanRollback(
            RemediationActionDef action)
        {
            return CanRollbackResult;
        }

        public Task<ExecutionResult> RollbackAsync(
            RemediationActionDef action,
            ExecutionResult originalExecution)
        {
            CallCount++;

            return Task.FromResult(
                new ExecutionResult
                {
                    Success = RollbackShouldSucceed,
                    Summary = RollbackShouldSucceed
                        ? "Rollback successful."
                        : "Rollback failed."
                });
        }
    }

    private sealed class FakePivotEngine : IPivotEngine
    {
        public int CallCount { get; private set; }

        public RemediationPlan Pivot(
            DiagnosticOutput diagnosticOutput,
            RemediationPlan failedPlan,
            RemediationActionDef failedAction,
            HardwareProfileDto currentSystemState)
        {
            CallCount++;

            return new RemediationPlan
            {
                SessionId = failedPlan.SessionId,
                PlannedActions = new List<RemediationActionDef>(),
                StrategyReasoning = failedPlan.StrategyReasoning
            };
        }
    }
    [Fact]
        public async Task RunDryRunCycleAsync_WhenConsentWouldBeRequired_StillSimulatesWithoutRealExecution()
        {
            var action = CreateAction();
            action.IsReversible = false;

            var planner =
                new FakePlanner(action);

            var safetyPolicy =
                new RequiresConsentSafetyPolicy();

            var dryRunExecutor =
                new FakeDryRunExecutor();

            var realExecutor =
                new FakeRealExecutor();

            var verificationService =
                new FakeVerificationService();

            var rollbackManager =
                new FakeRollbackManager();

            var pivotEngine =
                new FakePivotEngine();

            var orchestrator =
                new AutonomousOrchestrator(
                    planner,
                    safetyPolicy,
                    dryRunExecutor,
                    realExecutor,
                    verificationService,
                    rollbackManager,
                    pivotEngine);

            var result =
                await orchestrator
                    .RunDryRunCycleAsync(
                        CreateDiagnostic(),
                        CreateHardware());

            Assert.True(
                dryRunExecutor.WasCalled);

            Assert.False(
                realExecutor.WasCalled);

            Assert.NotNull(
                result.Execution);

            Assert.True(
                result.Execution.Success);

            Assert.NotNull(
                result.Safety);

            Assert.True(
                result.Safety
                    .RequiresUserConfirmation);

            Assert.True(
                result.Safety.IsApproved);

            Assert.Single(
                result.Attempts);

            Assert.Equal(
                RemediationAttemptState.Completed,
                result.Attempts[0].State);

            Assert.Contains(
                "No real system changes were made",
                result.Attempts[0].Notes);

            Assert.Contains(
                "Real execution will require explicit user consent",
                result.Trace);
        }
}