using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Controllers;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Domain.Entities;
using RigMD.Domain.Rules;
using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Api.Services;
using RigMD.Infrastructure.Remediation.Tools;
using System.Text.Json;

namespace RigMD.Tests.Api;

public class AutonomyControllerTests
{
    [Fact]
    public async Task Preview_DoesNotPersistRemediationHistory()
    {
        var sessionId = Guid.NewGuid();
        var diagnostic = CreateDiagnostic();

        var orchestrator = new FakeOrchestrator
        {
            PreviewResult = new OrchestrationResult
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

        var result = await controller.Preview(
            new AutonomyController.PreviewRequest
            {
                SessionId = sessionId.ToString(),
                DiagnosedCategory = diagnostic.DiagnosedCategory
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

        var approvals = new RemediationApprovalStore(TimeProvider.System);
        var approval = approvals.Issue(RemediationApprovalStore.CreateScope("remediation", sessionId, action.Id, "{}"));
        var controller = CreateController(
            orchestrator,
            diagnostic,
            remediationRepository, approvals: approvals);

        var result = await controller.Execute(
            new AutonomyController.ExecuteRequest
            {
                SessionId = sessionId.ToString(),
                DiagnosedCategory =
                    diagnostic.DiagnosedCategory,
                UserConsentProvided = true,
                ToolName = action.Id,
                ToolArgumentsJson = "{}",
                ApprovalToken = approval.Token
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

        Assert.IsType<BadRequestObjectResult>(result);

        Assert.Equal(
            0,
            remediationRepository.SaveCallCount);

        Assert.Null(
            remediationRepository.SavedRun);
    }

    private static AutonomyController CreateController(
        FakeOrchestrator orchestrator,
        DiagnosticOutput diagnostic,
        FakeRemediationRepository remediationRepository,
        RemediationApprovalStore? approvals = null,
        IRigMdAgentToolRegistry? registry = null)
    {
        return new AutonomyController(
            orchestrator,
            new FakeSystemProfileService(),
            new FakeDiagnosticSessionRepository(
                diagnostic),
            remediationRepository,
            new FakeHubContext(),
            NullLogger<AutonomyController>.Instance, registry, approvals: approvals);
    }

    [Fact]
    public async Task PreviewApproval_AuthorizesExactlyOneExecution()
    {
        var tool = new LocalApiSecurityTests.FakeTool(ToolSafetyTier.Tier1_SafeReversible) { CanPreviewExecute = true };
        var approvals = new RemediationApprovalStore(TimeProvider.System);
        var controller = CreateController(new FakeOrchestrator(), CreateDiagnostic(), new FakeRemediationRepository(),
            approvals, new RigMdAgentToolRegistry(new[] { tool }));
        var sessionId = Guid.NewGuid();
        using var arguments = JsonDocument.Parse("{}");
        var previewResponse = Assert.IsType<OkObjectResult>(await controller.PreviewTool(new()
        {
            SessionId = sessionId.ToString(), ToolName = tool.Name, Arguments = arguments.RootElement
        }, CancellationToken.None));
        var preview = Assert.IsType<ToolDryRunPreview>(previewResponse.Value);
        Assert.NotNull(preview.ApprovalToken);
        var request = new AutonomyController.ExecuteRequest
        {
            SessionId = sessionId.ToString(), UserConsentProvided = true,
            ToolName = tool.Name, ToolArgumentsJson = "{}", ApprovalToken = preview.ApprovalToken
        };
        Assert.IsType<OkObjectResult>(await controller.Execute(request));
        Assert.IsType<ConflictObjectResult>(await controller.Execute(request));
        // The fake orchestrator is inert; no real tool was invoked by this controller test.
        Assert.Equal(0, tool.ExecutionCount);
    }

    [Fact]
    public async Task Execute_WithoutPreviewApproval_IsRejectedEvenWithConsent()
    {
        var controller = CreateController(new FakeOrchestrator(), CreateDiagnostic(), new FakeRemediationRepository());
        Assert.IsType<ConflictObjectResult>(await controller.Execute(new()
        {
            SessionId = Guid.NewGuid().ToString(), UserConsentProvided = true,
            ToolName = "clear_temp_files", ToolArgumentsJson = "{}"
        }));
    }

    [Fact]
    public async Task CloseSelectedApp_WithoutPreviewApproval_IsRejectedEvenWithConfirmation()
    {
        var controller = CreateController(new FakeOrchestrator(), CreateDiagnostic(), new FakeRemediationRepository());
        Assert.IsType<ConflictObjectResult>(await controller.CloseSelectedApp(new()
        {
            SessionId = Guid.NewGuid().ToString(), Confirmed = true,
            ProcessNames = new() { "notepad" }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainPreview_BindsApprovalToTheArgumentsActuallyShown(bool changeArguments)
    {
        var tool = new LocalApiSecurityTests.FakeTool(ToolSafetyTier.Tier1_SafeReversible);
        var orchestrator = new FakeOrchestrator
        {
            PreviewResult = new OrchestrationResult
            {
                Plan = CreatePlan(), Safety = new SafetyEvaluation { IsApproved = true },
                DryRunPreview = new ToolDryRunPreview { CanExecute = true },
                ProposedTool = new ProposedToolInvocation { ToolName = tool.Name, ArgumentsJson = "{\"target\":\"previewed\"}" }
            }
        };
        var controller = CreateController(orchestrator, CreateDiagnostic(), new FakeRemediationRepository(),
            registry: new RigMdAgentToolRegistry(new[] { tool }));
        var sessionId = Guid.NewGuid();
        var response = Assert.IsType<OkObjectResult>(await controller.Preview(new() { SessionId = sessionId.ToString() }));
        var preview = Assert.IsType<OrchestrationResult>(response.Value).DryRunPreview!;
        Assert.NotNull(preview.ApprovalToken);
        Assert.Equal(tool.Name, preview.ToolName);
        var execution = await controller.Execute(new()
        {
            SessionId = sessionId.ToString(), UserConsentProvided = true,
            ToolName = tool.Name, ApprovalToken = preview.ApprovalToken,
            ToolArgumentsJson = changeArguments ? "{\"target\":\"changed\"}" : "{\"target\":\"previewed\"}"
        });
        if (changeArguments) Assert.IsType<ConflictObjectResult>(execution);
        else Assert.IsType<OkObjectResult>(execution);
    }

    [Fact]
    public async Task SelectedAppPreview_DoesNotCloseProcesses_AndBindsTheSelection()
    {
        var approvals = new RemediationApprovalStore(TimeProvider.System);
        var controller = CreateController(new FakeOrchestrator(), CreateDiagnostic(), new FakeRemediationRepository(), approvals);
        var sessionId = Guid.NewGuid();
        var response = Assert.IsType<OkObjectResult>(await controller.PreviewCloseSelectedApp(new()
        {
            SessionId = sessionId.ToString(), ProcessNames = new() { "chrome" }
        }));
        var preview = Assert.IsType<ToolDryRunPreview>(response.Value);
        Assert.Equal(new[] { "chrome" }, preview.AffectedTargets);
        Assert.NotNull(preview.ApprovalToken);
        // A changed selection is rejected before CloseApp can enumerate or close any process.
        Assert.IsType<ConflictObjectResult>(await controller.CloseSelectedApp(new()
        {
            SessionId = sessionId.ToString(), ProcessNames = new() { "calculator" },
            Confirmed = true, ApprovalToken = preview.ApprovalToken
        }));
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
            SupportedDiagnosisCategories = new List<string>
            {
                "OS performance degradation"
            },
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
        public OrchestrationResult PreviewResult
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
                HardwareProfileDto hardware,
                Action<string>? progressReporter = null,
                Action<ReActTraceStep>? stepReporter = null,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                PreviewResult);
        }

        public Task<OrchestrationResult>
            RunExecutionCycleAsync(
                DiagnosticOutput diagnostic,
                HardwareProfileDto hardware,
                bool userConsentProvided = false,
                Action<string>? progressCallback = null,
                string? requestedToolName = null,
                string? requestedArgumentsJson = null,
                Action<ReActTraceStep>? stepReporter = null,
                CancellationToken cancellationToken = default)
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
                OsVersion = "Windows 11",
                ProcessInsights = new ProcessInsightsDto
                {
                    TopMemoryApps = new() { new() { Name = "chrome", MemoryMb = 256, ProcessCount = 1 } }
                }
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

        public Task<Guid> SaveAutomaticDiagnosisAsync(
            HardwareProfileDto hardware,
            string diagnosisMode,
            IReadOnlyList<string> componentIds,
            string? scenarioId,
            Guid commandId,
            string agentId,
            string diagnosedCategory,
            string actionCategory,
            string confidenceLabel,
            string explanation,
            string clientId = "",
            string? primaryResult = null,
            string? incidentalWarning = null,
            string? componentStatus = null)
        {
            return Task.FromResult(Guid.NewGuid());
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

        public Task<bool> DeleteSessionAsync(
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

    private sealed class FakeHubContext : IHubContext<RemediationHub>
{
    public IHubClients Clients { get; } = new FakeHubClients();

    public IGroupManager Groups { get; } = new FakeGroupManager();
}

private sealed class FakeHubClients : IHubClients
{
    private static readonly IClientProxy Proxy = new FakeClientProxy();

    public IClientProxy All => Proxy;

    public IClientProxy AllExcept(
        IReadOnlyList<string> excludedConnectionIds)
        => Proxy;

    public IClientProxy Client(
        string connectionId)
        => Proxy;

    public IClientProxy Clients(
        IReadOnlyList<string> connectionIds)
        => Proxy;

    public IClientProxy Group(
        string groupName)
        => Proxy;

    public IClientProxy GroupExcept(
        string groupName,
        IReadOnlyList<string> excludedConnectionIds)
        => Proxy;

    public IClientProxy Groups(
        IReadOnlyList<string> groupNames)
        => Proxy;

    public IClientProxy User(
        string userId)
        => Proxy;

    public IClientProxy Users(
        IReadOnlyList<string> userIds)
        => Proxy;
}

private sealed class FakeClientProxy : IClientProxy
{
    public Task SendCoreAsync(
        string method,
        object?[] args,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

private sealed class FakeGroupManager : IGroupManager
{
    public Task AddToGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
}
