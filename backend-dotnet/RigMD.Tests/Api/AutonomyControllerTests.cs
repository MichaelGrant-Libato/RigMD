using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    [Theory]
    [InlineData(false, "normal", "40%", "superseded")]
    [InlineData(true, "normal", "40%", "superseded")]
    [InlineData(true, "high", "95%", "superseded")]
    [InlineData(false, "unknown", "Unavailable", "recheck_required")]
    public async Task NewMatchingCheckBlocksAllRepairPreviewsAndPreviouslyIssuedApproval(
        bool genericPreview, string status, string value, string expectedState)
    {
        var current = RigMD.Tests.Application.RemediationEligibilityPolicyTests.Check(0);
        current.ComponentIds = "[\"storage\"]";
        current.Proof = [RigMD.Tests.Application.RemediationEligibilityPolicyTests.Row("storage_usage", "high", "90%")];
        var diagnostic = CreateDiagnostic();
        var repo = new FakeDiagnosticSessionRepository(diagnostic) { Current = current };
        var tool = new LocalApiSecurityTests.FakeTool(ToolSafetyTier.Tier1_SafeReversible) { CanPreviewExecute = true };
        var orchestrator = new FakeOrchestrator { PreviewResult = new()
        {
            Safety = new() { IsApproved = true }, DryRunPreview = new() { CanExecute = true },
            ProposedTool = new() { ToolName = tool.Name, ArgumentsJson = "{}" }
        } };
        var controller = CreateController(orchestrator, diagnostic, new FakeRemediationRepository(),
            registry: new RigMdAgentToolRegistry(new[] { tool }), sessionRepository: repo);
        var invocation = new AutonomyController.ToolInvocationRequest
        {
            SessionId = current.SessionId, ToolName = tool.Name, Arguments = JsonSerializer.SerializeToElement(new { })
        };
        var initial = Assert.IsType<OkObjectResult>(genericPreview
            ? await controller.PreviewTool(invocation, CancellationToken.None)
            : await controller.Preview(new() { SessionId = current.SessionId }));
        var preview = genericPreview ? Assert.IsType<ToolDryRunPreview>(initial.Value)
            : Assert.IsType<OrchestrationResult>(initial.Value).DryRunPreview!;
        Assert.NotNull(preview.ApprovalToken);
        var newer = RigMD.Tests.Application.RemediationEligibilityPolicyTests.Check(1);
        newer.ComponentIds = current.ComponentIds;
        newer.Proof = [RigMD.Tests.Application.RemediationEligibilityPolicyTests.Row("storage_usage", status, value)];
        repo.History.Add(newer);
        var callsBeforeBlock = orchestrator.Calls;
        AssertGuard(await controller.Execute(new()
        {
            SessionId = current.SessionId, UserConsentProvided = true, ToolName = tool.Name,
            ApprovalToken = preview.ApprovalToken, ToolArgumentsJson = "{}"
        }), expectedState);
        AssertGuard(await controller.Preview(new() { SessionId = current.SessionId }), expectedState);
        AssertGuard(await controller.PreviewTool(invocation, CancellationToken.None), expectedState);
        var eligibility = Assert.IsType<RigMD.Application.Services.RemediationEligibility>(
            Assert.IsType<OkObjectResult>(await controller.GetRemediationEligibility(current.SessionId)).Value);
        Assert.Equal(expectedState, eligibility.State);
        var wire = JsonSerializer.SerializeToElement(eligibility);
        Assert.Equal(expectedState, wire.GetProperty("state").GetString());
        Assert.True(wire.TryGetProperty("message", out _));
        Assert.True(wire.TryGetProperty("newer_session_id", out _));
        Assert.True(wire.TryGetProperty("checked_at", out _));
        Assert.Equal(callsBeforeBlock, orchestrator.Calls);
        Assert.Equal(0, tool.ExecutionCount);
        var bypass = Assert.IsType<ObjectResult>(await controller.ExecuteTool(invocation, CancellationToken.None));
        Assert.Equal(403, bypass.StatusCode);
    }

    [Theory]
    [InlineData("normal", "40%", "superseded")]
    [InlineData("high", "95%", "superseded")]
    [InlineData("unknown", "Unavailable", "recheck_required")]
    public async Task NewMatchingMemoryCheckBlocksSelectedAppPreviewAndExistingApproval(string status, string value, string state)
    {
        var current = RigMD.Tests.Application.RemediationEligibilityPolicyTests.Check(0);
        var diagnostic = CreateMemoryDiagnostic();
        var repo = new FakeDiagnosticSessionRepository(diagnostic) { Current = current };
        var controller = CreateController(new(), diagnostic, new(), sessionRepository: repo,
            hardware: new() { Ram = new() { UsagePercent = 90 }, ProcessInsights = new()
            { TopMemoryApps = [new() { Name = "chrome", MemoryMb = 256, ProcessCount = 1 }] } });
        var request = new AutonomyController.CloseSelectedAppRequest { SessionId = current.SessionId, ProcessNames = ["chrome"] };
        var preview = Assert.IsType<ToolDryRunPreview>(Assert.IsType<OkObjectResult>(await controller.PreviewCloseSelectedApp(request)).Value);
        repo.History.Add(RigMD.Tests.Application.RemediationEligibilityPolicyTests.Check(1, status, value));
        AssertGuard(await controller.PreviewCloseSelectedApp(request), state);
        request.Confirmed = true; request.ApprovalToken = preview.ApprovalToken;
        AssertGuard(await controller.CloseSelectedApp(request), state);
        // Both requests stop before enumerating processes or sending normal-close requests.
    }

    [Fact]
    public async Task EligibilityRefreshHonorsLaterSameSessionFollowupAndReadOnlyToolsRemainAvailable()
    {
        var current = RigMD.Tests.Application.RemediationEligibilityPolicyTests.Check(0);
        var diagnostic = CreateMemoryDiagnostic();
        var repo = new FakeDiagnosticSessionRepository(diagnostic) { Current = current };
        repo.History.Add(RigMD.Tests.Application.RemediationEligibilityPolicyTests.Check(1, "unknown", "Unavailable"));
        var readOnly = new LocalApiSecurityTests.FakeTool(ToolSafetyTier.Tier0_ReadOnly);
        var controller = CreateController(new(), diagnostic, new(), registry: new RigMdAgentToolRegistry(new[] { readOnly }), sessionRepository: repo);
        var before = Assert.IsType<RigMD.Application.Services.RemediationEligibility>(
            Assert.IsType<OkObjectResult>(await controller.GetRemediationEligibility(current.SessionId)).Value);
        Assert.Equal("recheck_required", before.State);
        Assert.IsType<OkObjectResult>(await controller.ExecuteTool(new()
        {
            SessionId = current.SessionId, ToolName = readOnly.Name, Arguments = JsonSerializer.SerializeToElement(new { })
        }, CancellationToken.None));
        current.ResolutionCheckedAt = RigMD.Tests.Application.RemediationEligibilityPolicyTests.At(2);
        current.ResolutionStatus = "still_active";
        current.ResolutionProof = [RigMD.Tests.Application.RemediationEligibilityPolicyTests.Row("memory_usage", "high", "91%")];
        var after = Assert.IsType<RigMD.Application.Services.RemediationEligibility>(
            Assert.IsType<OkObjectResult>(await controller.GetRemediationEligibility(current.SessionId)).Value);
        Assert.Equal("allowed", after.State);
    }

    private static void AssertGuard(IActionResult result, string state)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
        var body = JsonSerializer.SerializeToElement(conflict.Value);
        Assert.Equal(state, body.GetProperty("remediation_guard").GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData(0, "App closed")]
    [InlineData(6, "App still running in background")]
    [InlineData(null, "App state not verified")]
    public async Task SelectedAppEvidenceIsHonestAndSurvivesHistoryRoundTrip(int? remaining, string expected)
    {
        // Exercise result construction and persistence without enumerating or closing any real app.
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var outcomeType = typeof(AutonomyController).GetNestedType("CloseAppOutcome", System.Reflection.BindingFlags.NonPublic)!;
        var outcome = Activator.CreateInstance(outcomeType, nonPublic: true)!;
        void Set(string name, object? value) => outcomeType.GetProperty(name)!.SetValue(outcome, value);
        Set("ProcessName", "discord"); Set("DisplayName", "Discord");
        Set("BeforeMemoryMb", 850d); Set("BeforeProcessCount", 6);
        Set("AfterMemoryMb", 100d); Set("AfterProcessCount", remaining); Set("ClosedWindows", 1);
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(outcomeType))!;
        list.Add(outcome);
        var result = (OrchestrationResult)typeof(AutonomyController).GetMethod("BuildSelectedAppResult", flags)!
            .Invoke(null, new object[] { Guid.NewGuid(), list })!;
        Assert.Equal(expected, Assert.Single(result.Execution!.Proof).Status);
        Assert.Equal(VerificationStatus.Unknown, result.Verification);
        Assert.Same(result.Execution, Assert.Single(result.Attempts).Execution);

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RigMD.Infrastructure.Persistence.RigMdDbContext>()
            .UseSqlite("DataSource=:memory:").Options;
        await using var db = new RigMD.Infrastructure.Persistence.RigMdDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var profile = new SystemProfile { CpuModel = "Test CPU", OsVersion = "Test Windows" };
        var session = new DiagnosticSession { Profile = profile, SystemProfileId = profile.Id };
        var diagnostic = CreateDiagnostic(); diagnostic.DiagnosticSessionId = session.Id;
        session.Output = diagnostic;
        db.DiagnosticSessions.Add(session);
        await db.SaveChangesAsync();
        var run = (RemediationRun)typeof(AutonomyController).GetMethod("BuildRemediationRun", flags)!
            .Invoke(null, new object[] { diagnostic.Id, result, new HardwareProfileDto() })!;
        await new RigMD.Infrastructure.Persistence.RemediationRepository(db).SaveRunAsync(run);
        db.ChangeTracker.Clear();
        var stored = await db.VerificationResults.SingleAsync();
        Assert.False(stored.IsSuccessful);
        Assert.Equal("Unknown", stored.FailureReason);
        Assert.Contains(expected, stored.ObservedState);
        Assert.Contains("no force close", stored.ObservedState);
        var history = await new RigMD.Infrastructure.Persistence.DiagnosticSessionRepository(db, new HistoryClientProvider())
            .GetRemediationHistoryAsync(session.Id);
        var attempt = Assert.Single(Assert.Single(history).Attempts);
        Assert.Equal(expected, Assert.Single(attempt.Proof).Status);
        Assert.Contains("Review each app", attempt.ExecutionSummary);
    }

    private sealed class HistoryClientProvider : RigMD.Application.Contracts.Common.ICurrentClientProvider
    {
        public string GetCurrentClientId() => string.Empty;
    }

    [Fact]
    public async Task CrashMemoryFindingMustOpenANewMemoryScopeBeforeObtainingApproval()
    {
        var diagnostic = CreateDiagnostic(); diagnostic.DiagnosedCategory = "Application stability risk needs review";
        diagnostic.Session = new() { Answers = new List<SessionAnswer> { new()
        {
            QuestionKey = "proof", AnswerValue = "[{\"metric_id\":\"memory_usage\",\"status\":\"high\",\"value\":\"84.7% (13.3 / 15.7 GB)\"}]"
        } } };
        var hardware = new HardwareProfileDto
        {
            Ram = new() { UsagePercent = 84.7, TotalGb = 15.7 },
            ProcessInsights = new() { TopMemoryApps = new() { new() { Name = "chrome", MemoryMb = 256, ProcessCount = 1 } } }
        };
        var orchestrator = new FakeOrchestrator();
        var controller = CreateController(orchestrator, diagnostic, new FakeRemediationRepository(), hardware: hardware);
        var request = new AutonomyController.CloseSelectedAppRequest { SessionId = Guid.NewGuid().ToString(), ProcessNames = new() { "chrome" } };
        Assert.IsType<ConflictObjectResult>(await controller.PreviewCloseSelectedApp(request));
        Assert.IsType<ConflictObjectResult>(await controller.CloseSelectedApp(new()
        {
            SessionId = request.SessionId, ProcessNames = request.ProcessNames, Confirmed = true, ApprovalToken = "invented"
        }));
        Assert.IsType<ConflictObjectResult>(await controller.Execute(new()
        {
            SessionId = request.SessionId, ToolName = "run_system_file_checker", UserConsentProvided = true
        }));
        var memoryController = CreateController(orchestrator, CreateMemoryDiagnostic(), new FakeRemediationRepository(), hardware: hardware);
        request.SessionId = Guid.NewGuid().ToString();
        var preview = Assert.IsType<ToolDryRunPreview>(Assert.IsType<OkObjectResult>(await memoryController.PreviewCloseSelectedApp(request)).Value);
        Assert.NotNull(preview.ApprovalToken);
        hardware.Ram.UsagePercent = 60;
        Assert.IsType<ConflictObjectResult>(await memoryController.CloseSelectedApp(new()
        {
            SessionId = request.SessionId, ProcessNames = request.ProcessNames, Confirmed = true, ApprovalToken = preview.ApprovalToken
        }));
        Assert.IsType<ConflictObjectResult>(await memoryController.PreviewCloseSelectedApp(request));
        Assert.Equal(0, orchestrator.Calls);
        // Execution is rejected before the method that enumerates or closes real processes.
    }

    [Fact]
    public async Task MemoryFindingCannotObtainCleanupApprovalOrExecuteAnIncompatibleTool()
    {
        var tool = new LocalApiSecurityTests.FakeTool(ToolSafetyTier.Tier1_SafeReversible) { CanPreviewExecute = true };
        var diagnostic = CreateDiagnostic(); diagnostic.DiagnosedCategory = "High Memory Pressure";
        var orchestrator = new FakeOrchestrator();
        var controller = CreateController(orchestrator, diagnostic, new FakeRemediationRepository(),
            registry: new RigMdAgentToolRegistry(new[] { tool }));
        Assert.IsType<ConflictObjectResult>(await controller.PreviewTool(new()
        {
            SessionId = Guid.NewGuid().ToString(), ToolName = tool.Name,
            Arguments = JsonSerializer.SerializeToElement(new { })
        }, CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.Execute(new()
        {
            SessionId = Guid.NewGuid().ToString(), ToolName = tool.Name, UserConsentProvided = true
        }));
        Assert.Equal(0, orchestrator.Calls);
        Assert.Equal(0, tool.ExecutionCount);
    }

    [Theory]
    [InlineData("Application crash history requires review")]
    [InlineData("Scanner evidence unavailable")]
    public async Task UnverifiedCauseCannotObtainOrUseGenericRepairApproval(string category)
    {
        var diagnostic = CreateDiagnostic(); diagnostic.DiagnosedCategory = category;
        var orchestrator = new FakeOrchestrator();
        var controller = CreateController(orchestrator, diagnostic, new FakeRemediationRepository());
        Assert.IsType<ConflictObjectResult>(await controller.Preview(new() { SessionId = Guid.NewGuid().ToString() }));
        Assert.IsType<ConflictObjectResult>(await controller.Execute(new() { SessionId = Guid.NewGuid().ToString(), UserConsentProvided = true, ToolName = "run_system_file_checker" }));
        Assert.IsType<ConflictObjectResult>(await controller.PreviewCloseSelectedApp(new() { SessionId = Guid.NewGuid().ToString(), ProcessNames = ["chrome"] }));
        Assert.Equal(0, orchestrator.Calls);
    }

    [Theory]
    [InlineData(0, 6, 850.23, false)]
    [InlineData(0, 6, 100d, false)]
    [InlineData(0, null, null, false)]
    [InlineData(1, 6, 850.23, true)]
    [InlineData(0, 0, 0d, true)]
    public void AppClosureOutcome_DoesNotTreatMemoryFluctuationAsAnExecutedClose(int requests, int? remaining, double? after, bool expected)
    {
        var type = typeof(AutonomyController).GetNestedType("CloseAppOutcome", System.Reflection.BindingFlags.NonPublic)!;
        var outcome = Activator.CreateInstance(type, nonPublic: true)!;
        void Set(string name, object? value) => type.GetProperty(name)!.SetValue(outcome, value);
        Set("BeforeMemoryMb", 850.31); Set("BeforeProcessCount", 6);
        Set("AfterMemoryMb", after); Set("AfterProcessCount", remaining); Set("ClosedWindows", requests);
        Assert.Equal(expected, type.GetProperty("Success")!.GetValue(outcome));
    }

    [Fact]
    public async Task HardwareWarningBlocksSoftwareRepair_EvenWithConsentAndAnExplicitTool()
    {
        var diagnostic = CreateDiagnostic();
        diagnostic.DiagnosedCategory = "Storage health behavior";
        var orchestrator = new FakeOrchestrator();
        var controller = CreateController(orchestrator, diagnostic, new FakeRemediationRepository());
        var preview = Assert.IsType<ConflictObjectResult>(await controller.Preview(new AutonomyController.PreviewRequest { SessionId = Guid.NewGuid().ToString() }));
        Assert.Contains("hardware inspection", JsonSerializer.Serialize(preview.Value));
        var execute = Assert.IsType<ConflictObjectResult>(await controller.Execute(new AutonomyController.ExecuteRequest
        {
            SessionId = Guid.NewGuid().ToString(), UserConsentProvided = true, ToolName = "clear_temp_files"
        }));
        Assert.Contains("cannot repair physical damage", JsonSerializer.Serialize(execute.Value));
        Assert.Equal(0, orchestrator.Calls);
    }
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
        IRigMdAgentToolRegistry? registry = null,
        HardwareProfileDto? hardware = null,
        FakeDiagnosticSessionRepository? sessionRepository = null)
    {
        return new AutonomyController(
            orchestrator,
            new FakeSystemProfileService { Hardware = hardware },
            sessionRepository ?? new FakeDiagnosticSessionRepository(
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
        var controller = CreateController(new FakeOrchestrator(), CreateMemoryDiagnostic(), new FakeRemediationRepository(), approvals,
            hardware: new() { Ram = new() { UsagePercent = 90 }, ProcessInsights = new() { TopMemoryApps = [new() { Name = "chrome", MemoryMb = 256 }] } });
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

    private static DiagnosticOutput CreateMemoryDiagnostic() => new()
    {
        DiagnosedCategory = "High Memory Pressure", ActionCategory = "Troubleshoot",
        Session = new() { Answers = new List<SessionAnswer>
        {
            new() { QuestionKey = "diagnosis_mode", AnswerValue = "component" },
            new() { QuestionKey = "component_ids", AnswerValue = "[\"memory\"]" },
            new() { QuestionKey = "proof", AnswerValue = "[{\"metric_id\":\"memory_usage\",\"status\":\"high\",\"value\":\"90%\",\"evidence_kind\":\"measurement\"}]" }
        } }
    };

    [Fact]
    public async Task ExplicitScopeRejectsAnUnrelatedToolBeforeIssuingOrConsumingApproval()
    {
        var diagnostic = CreateDiagnostic();
        diagnostic.Session = new() { Answers = new List<SessionAnswer>
        {
            new() { QuestionKey = "diagnosis_mode", AnswerValue = "component" },
            new() { QuestionKey = "component_ids", AnswerValue = "[\"network\"]" }
        } };
        var tool = new LocalApiSecurityTests.FakeTool(ToolSafetyTier.Tier1_SafeReversible) { CanPreviewExecute = true };
        var orchestrator = new FakeOrchestrator();
        var approvals = new RemediationApprovalStore(TimeProvider.System);
        var controller = CreateController(orchestrator, diagnostic, new FakeRemediationRepository(), approvals,
            new RigMdAgentToolRegistry(new[] { tool }));
        var id = Guid.NewGuid();
        Assert.IsType<ConflictObjectResult>(await controller.PreviewTool(new()
        {
            SessionId = id.ToString(), ToolName = tool.Name, Arguments = JsonSerializer.SerializeToElement(new { })
        }, CancellationToken.None));
        var approval = approvals.Issue(RemediationApprovalStore.CreateScope("remediation", id, tool.Name, "{}"));
        Assert.IsType<ConflictObjectResult>(await controller.Execute(new()
        {
            SessionId = id.ToString(), ToolName = tool.Name, ToolArgumentsJson = "{}", ApprovalToken = approval.Token,
            UserConsentProvided = true
        }));
        Assert.Equal(0, orchestrator.Calls);
        Assert.Equal(0, tool.ExecutionCount);
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
        public int Calls { get; private set; }
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
            Calls++;
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
            Calls++;
            return Task.FromResult(
                ExecutionResult);
        }
    }

    private sealed class FakeSystemProfileService :
        IWindowsSystemProfileService
    {
        public HardwareProfileDto? Hardware { get; init; }
        public HardwareProfileDto
            GetLiveSystemProfile()
        {
            return Hardware ?? new HardwareProfileDto
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

        public DiagnosticSessionDto? Current { get; set; }
        public List<DiagnosticSessionDto> History { get; } = new();

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
            string? componentStatus = null,
            IReadOnlyList<AutomaticDiagnosisProof>? proof = null,
            string? recommendedNextStep = null)
        {
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<IReadOnlyList<DiagnosticSessionDto>>
            GetSessionsAsync()
        {
            return Task.FromResult<IReadOnlyList<DiagnosticSessionDto>>(History.ToArray());
        }

        public Task<DiagnosticSessionDto?>
            GetSessionAsync(
                Guid sessionId)
        {
            string Answer(string key) => _diagnostic.Session?.Answers.FirstOrDefault(a => a.QuestionKey == key)?.AnswerValue ?? "";
            var rawProof = Answer("proof");
            return Task.FromResult<DiagnosticSessionDto?>(Current ?? new()
            {
                SessionId = sessionId.ToString(), DeviceId = "test-device", CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                DiagnosisMode = string.IsNullOrWhiteSpace(Answer("diagnosis_mode")) ? "full" : Answer("diagnosis_mode"),
                ComponentIds = Answer("component_ids"), ScenarioId = Answer("scenario_id"),
                DiagnosedCategory = _diagnostic.DiagnosedCategory,
                Proof = string.IsNullOrWhiteSpace(rawProof)
                    ? [new { metric_id = "cpu_usage", label = "CPU", value = "95%", status = "high", evidence_kind = "measurement" }]
                    : JsonSerializer.Deserialize<object[]>(rawProof)!
            });
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
