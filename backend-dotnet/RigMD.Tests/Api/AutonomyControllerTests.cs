using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Controllers;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Domain.Entities;
using RigMD.Domain.Rules;

namespace RigMD.Tests.Api;

public class AutonomyControllerTests
{
    [Fact]
    public async Task DryRun_DoesNotPersistRemediationHistory()
    {
        var sessionId = Guid.NewGuid();
        var diagnostic = CreateDiagnostic();

        var orchestrator = new FakeOrchestrator
        {
            DryRunResult = new OrchestrationResult
            {
                Plan = CreatePlan(),
                Execution = new ExecutionResult
                {
                    Success = true,
                    Summary = "DRY RUN"
                },
                Attempts =
                {
                    new RemediationAttempt
                    {
                        Action = CreateAction(),
                        State = RemediationAttemptState.Completed,
                        Execution = new ExecutionResult
                        {
                            Success = true,
                            Summary = "DRY RUN"
                        }
                    }
                }
            }
        };

        var remediationRepository =
            new FakeRemediationRepository();

        var controller = CreateController(
            orchestrator,
            diagnostic,
            remediationRepository);

        var result = await controller.DryRun(
            new AutonomyController.DryRunRequest
            {
                SessionId = sessionId.ToString(),
                DiagnosedCategory =
                    diagnostic.DiagnosedCategory
            });

        var okResult = Assert.IsType<OkObjectResult>(result);

        var response =
            Assert.IsType<OrchestrationResult>(
                okResult.Value);

        Assert.Equal(
            sessionId.ToString(),
            response.Plan?.SessionId);

        Assert.Equal(
            0,
            remediationRepository.SaveCallCount);

        Assert.Null(
            remediationRepository.SavedRun);
    }

    [Fact]
    public async Task Execute_WhenActionReachedExecution_PersistsRemediationRun()
    {
        var sessionId = Guid.NewGuid();
        var diagnostic = CreateDiagnostic();

        var action = CreateAction();

        var execution = new ExecutionResult
        {
            Success = true,
            Summary = "Execution completed."
        };

        var orchestrator = new FakeOrchestrator
        {
            ExecutionResult = new OrchestrationResult
            {
                Plan = CreatePlan(),
                Safety = new SafetyEvaluation
                {
                    IsApproved = true
                },
                Execution = execution,
                Verification =
                    VerificationStatus.Resolved,
                Attempts =
                {
                    new RemediationAttempt
                    {
                        Action = action,
                        State =
                            RemediationAttemptState.Resolved,
                        Execution = execution,
                        Verification =
                            VerificationStatus.Resolved
                    }
                }
            }
        };

        var remediationRepository =
            new FakeRemediationRepository();

        var controller = CreateController(
            orchestrator,
            diagnostic,
            remediationRepository);

        var result = await controller.Execute(
            new AutonomyController.ExecuteRequest
            {
                SessionId = sessionId.ToString(),
                DiagnosedCategory =
                    diagnostic.DiagnosedCategory,
                UserConsentProvided = true
            });

        var okResult =
            Assert.IsType<OkObjectResult>(result);

        var response =
            Assert.IsType<OrchestrationResult>(
                okResult.Value);

        Assert.Equal(
            1,
            remediationRepository.SaveCallCount);

        var savedRun =
            Assert.IsType<RemediationRun>(
                remediationRepository.SavedRun);

        Assert.Equal(
            diagnostic.Id,
            savedRun.DiagnosticOutputId);

        Assert.Equal(
            "Resolved",
            savedRun.Status);

        var savedAttempt =
            Assert.Single(savedRun.ActionAttempts);

        Assert.Equal(
            action.Id,
            savedAttempt.ActionCode);

        Assert.NotNull(
            savedAttempt.Verification);

        Assert.True(
            savedAttempt.Verification!.IsSuccessful);

        Assert.Contains(
            "[PERSISTENCE] Remediation run saved:",
            response.Trace);

        Assert.Equal(
            sessionId.ToString(),
            response.Plan?.SessionId);
    }

    [Fact]
    public async Task Execute_WhenConsentStopsExecution_DoesNotPersistRemediationRun()
    {
        var sessionId = Guid.NewGuid();
        var diagnostic = CreateDiagnostic();

        var orchestrator = new FakeOrchestrator
        {
            ExecutionResult = new OrchestrationResult
            {
                Plan = CreatePlan(),
                Safety = new SafetyEvaluation
                {
                    IsApproved = false,
                    RequiresUserConfirmation = true,
                    RejectionReason =
                        "Explicit user consent is required."
                },
                Attempts =
                {
                    new RemediationAttempt
                    {
                        Action = CreateAction(),
                        State =
                            RemediationAttemptState.AwaitingConsent,
                        Execution = null,
                        Notes =
                            "Explicit user consent is required."
                    }
                }
            }
        };

        var remediationRepository =
            new FakeRemediationRepository();

        var controller = CreateController(
            orchestrator,
            diagnostic,
            remediationRepository);

        var result = await controller.Execute(
            new AutonomyController.ExecuteRequest
            {
                SessionId = sessionId.ToString(),
                DiagnosedCategory =
                    diagnostic.DiagnosedCategory,
                UserConsentProvided = false
            });

        Assert.IsType<OkObjectResult>(result);

        Assert.Equal(
            0,
            remediationRepository.SaveCallCount);

        Assert.Null(
            remediationRepository.SavedRun);
    }

    private static AutonomyController CreateController(
        FakeOrchestrator orchestrator,
        DiagnosticOutput diagnostic,
        FakeRemediationRepository remediationRepository)
    {
        return new AutonomyController(
            orchestrator,
            new FakeSystemProfileService(),
            new FakeDiagnosticSessionRepository(
                diagnostic),
            remediationRepository,
            NullLogger<AutonomyController>.Instance);
    }

    private static DiagnosticOutput CreateDiagnostic()
    {
        return new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosedCategory =
                "OS performance degradation",
            ActionCategory = "Maintain",
            ConfidenceLabel = "High"
        };
    }

    private static RemediationActionDef CreateAction()
    {
        return new RemediationActionDef
        {
            Id = "test_action",
            Name = "Test Action",
            Category =
                "OS performance degradation",
            RiskLevel = "Low",
            IsReversible = false
        };
    }

    private static RemediationPlan CreatePlan()
    {
        return new RemediationPlan
        {
            PlannedActions =
            {
                CreateAction()
            },
            StrategyReasoning =
                "Regression test plan."
        };
    }

    private sealed class FakeOrchestrator :
        IAutonomousOrchestrator
    {
        public OrchestrationResult DryRunResult
        {
            get;
            set;
        } = new();

        public OrchestrationResult ExecutionResult
        {
            get;
            set;
        } = new();

        public Task<OrchestrationResult>
            RunDryRunCycleAsync(
                DiagnosticOutput diagnostic,
                HardwareProfileDto hardware)
        {
            return Task.FromResult(
                DryRunResult);
        }

        public Task<OrchestrationResult>
            RunExecutionCycleAsync(
                DiagnosticOutput diagnostic,
                HardwareProfileDto hardware,
                bool userConsentProvided = false)
        {
            return Task.FromResult(
                ExecutionResult);
        }
    }

    private sealed class FakeSystemProfileService :
        IWindowsSystemProfileService
    {
        public HardwareProfileDto
            GetLiveSystemProfile()
        {
            return new HardwareProfileDto
            {
                OsVersion = "Windows 11"
            };
        }
    }

    private sealed class FakeRemediationRepository :
        IRemediationRepository
    {
        public int SaveCallCount
        {
            get;
            private set;
        }

        public RemediationRun? SavedRun
        {
            get;
            private set;
        }

        public Task<Guid> SaveRunAsync(
            RemediationRun run)
        {
            SaveCallCount++;
            SavedRun = run;

            return Task.FromResult(run.Id);
        }

        public Task<IReadOnlyList<RemediationRun>>
            GetRunsBySessionAsync(
                Guid diagnosticOutputId)
        {
            IReadOnlyList<RemediationRun> result =
                Array.Empty<RemediationRun>();

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<string>>
            GetFailedActionCodesAsync()
        {
            IReadOnlyList<string> result =
                Array.Empty<string>();

            return Task.FromResult(result);
        }
    }

    private sealed class FakeDiagnosticSessionRepository :
        IDiagnosticSessionRepository
    {
        private readonly DiagnosticOutput _diagnostic;

        public FakeDiagnosticSessionRepository(
            DiagnosticOutput diagnostic)
        {
            _diagnostic = diagnostic;
        }

        public Task<DiagnosticOutput?>
            GetDiagnosticOutputAsync(
                Guid sessionId)
        {
            return Task.FromResult<
                DiagnosticOutput?>(_diagnostic);
        }

        public Task<Guid> SaveDiagnosisAsync(
            DiagnosticSymptomPayload payload,
            HardwareProfileDto hardware,
            string diagnosedCategory,
            string actionCategory,
            string confidenceLabel,
            string aiExplanation,
            string clientId = "")
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<DiagnosticSessionDto>>
            GetSessionsAsync()
        {
            throw new NotSupportedException();
        }

        public Task<DiagnosticSessionDto?>
            GetSessionAsync(
                Guid sessionId)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateResolutionAsync(
            Guid sessionId,
            string resolutionStatus,
            string resolutionCheckedAt,
            string resolutionSummary,
            object[] resolutionProof)
        {
            throw new NotSupportedException();
        }

        public Task<bool> MarkNeedsRecheckAsync(
            Guid sessionId)
        {
            throw new NotSupportedException();
        }

        public Task<object>
            GetDashboardSummaryAsync()
        {
            throw new NotSupportedException();
        }

        public Task<object>
            GetRecurringPatternsAsync()
        {
            throw new NotSupportedException();
        }

        public Task<object>
            GetWarningSignsAsync()
        {
            throw new NotSupportedException();
        }

        public Task<object>
            GetProfilesAsync()
        {
            throw new NotSupportedException();
        }

        public Task<object> SaveProfileAsync(
            SaveProfilePayload payload)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RecurringSessionDto>>
            GetRecurringSessionsAsync()
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<string>>
            GetObservedWarningTextsAsync()
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RemediationRunDto>>
            GetRemediationHistoryAsync(
                Guid sessionId)
        {
            throw new NotSupportedException();
        }
    }
}