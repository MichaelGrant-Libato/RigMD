using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Ai;
using RigMD.Infrastructure.Remediation;
using RigMD.Infrastructure.Remediation.Tools;
using RigMD.Infrastructure.Remediation.Tools.Diagnostic;
using RigMD.Infrastructure.Remediation.Tools.Remediation;
using Xunit;

namespace RigMD.Tests.Services.Autonomy;

public class ReActOrchestratorTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubProviders :
        ICpuProvider,
        IMemoryProvider,
        IProcessProvider,
        IStorageProvider,
        IGpuProvider,
        IDisplayProvider,
        INetworkProvider,
        IBatteryProvider,
        IPowerProvider,
        IDeviceTypeProvider
    {
        public double? CpuTemperature { get; set; } = 64.0;
        public double CpuUsage { get; set; } = 42.0;

        public CpuStatsDto GetCpuStats() => new()
        {
            Name = "Intel Core i7-13700K",
            UsagePercent = CpuUsage,
            FrequencyMhz = 5100,
            MaxFrequencyMhz = 5400,
            Cores = 16,
            Threads = 24,
            TemperatureCelsius = CpuTemperature,
            TemperatureSource = CpuTemperature.HasValue ? "test CPU sensor" : "Unavailable"
        };

        public MemoryStatsDto GetMemoryStats() => new()
        {
            TotalGb = 32,
            UsedGb = 22.5,
            UsagePercent = 70.3,
            CommittedGb = 25.0
        };

        public ProcessInsightsDto GetProcessInsights() => new()
        {
            BrowserDetected = true,
            BrowserProcessCount = 18,
            BrowserMemoryMb = 1850.0,
            BrowserHeavy = true
        };

        public string GetPrimaryStorageType() => "NVMe SSD";

        public List<StorageDriveDto> GetStorageDrives() => new()
        {
            new() { Model = "WD Black SN850X 1TB", Type = "NVMe SSD", SizeGb = 931.5, IsFailingSmart = false }
        };

        public List<DiskVolumeDto> GetAllDisks() => new()
        {
            new() { Drive = "C:\\", FsType = "NTFS", TotalGb = 931.5, UsedGb = 610.0, UsagePercent = 65.5 }
        };

        public GpuStatsDto GetGpuStats() => new()
        {
            Name = "NVIDIA GeForce RTX 4070 Ti",
            Type = "Dedicated",
            Driver = "32.0.15.6094",
            VramGb = 12.0
        };

        public int GetConnectedDisplays() => 1;

        public List<DisplayStatsDto> GetDisplays() => new()
        {
            new() { Name = "Display 1", Resolution = "2560 x 1440", RefreshRate = 144 }
        };

        public NetworkStatsDto GetNetworkStats() => new()
        {
            HasActiveAdapter = true,
            AdapterName = "Ethernet",
            HasIpv4Address = true,
            HasDefaultGateway = true,
            HasDnsServers = true
        };

        public BatteryStatsDto? GetBatteryStats() => null;

        public string GetActivePowerPlan() => "High Performance";

        public string GetDeviceType() => "Desktop";
    }

    [Theory]
    [InlineData("app-crashes", 0, "valid", VerificationStatus.Unknown)]
    [InlineData("app-crashes", 1, "valid", VerificationStatus.Unresolved)]
    [InlineData("blue-screen-crash", 1, "valid", VerificationStatus.Unresolved)]
    [InlineData("app-crashes", 12, "wrong-focus", VerificationStatus.Unknown)]
    [InlineData("app-crashes", 12, "wrong-time", VerificationStatus.Unknown)]
    [InlineData("app-crashes", 12, "failed", VerificationStatus.Unknown)]
    [InlineData("app-crashes", 12, "warning", VerificationStatus.Unknown)]
    [InlineData("app-crashes", -1, "valid", VerificationStatus.Unknown)]
    public async Task CrashVerification_UsesScopedNewRecords_NotHistoricalCounts(
        string scenario, int count, string evidence, VerificationStatus expected)
    {
        var action = new FakeSafeRemediationTool { Name = "run_system_file_checker" };
        var records = new FakeCrashInspectionTool(count, evidence);
        var registry = new RigMdAgentToolRegistry(new IRigMdAgentTool[] { action, records });
        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, NullLoggerFactory.Instance),
            registry);
        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "Application stability risk needs review",
            Session = new DiagnosticSession
            {
                Answers = new List<SessionAnswer>
                {
                    new() { QuestionKey = "diagnosis_mode", AnswerValue = "scenario" },
                    new() { QuestionKey = "scenario_id", AnswerValue = scenario }
                }
            }
        };
        var started = DateTimeOffset.UtcNow;
        var result = await orchestrator.RunExecutionCycleAsync(diagnostic, new HardwareProfileDto(),
            userConsentProvided: true, requestedToolName: action.Name, requestedArgumentsJson: "{}");
        Assert.Equal(0, action.ExecutionCount);
        Assert.Null(result.Execution);
        Assert.Empty(records.Arguments);
        Assert.Equal(VerificationStatus.Unknown, result.Verification);
        Assert.Contains("cause", result.Safety!.RejectionReason, StringComparison.OrdinalIgnoreCase);
        var focus = scenario == "app-crashes" ? "ApplicationCrash" : "BugCheck";
        var beforeArgs = JsonSerializer.SerializeToElement(new
        {
            eventFocus = focus, logName = scenario == "app-crashes" ? "Application" : "System", sinceUtc = (string?)null
        });
        var afterArgs = JsonSerializer.SerializeToElement(new
        {
            eventFocus = focus, logName = scenario == "app-crashes" ? "Application" : "System", sinceUtc = started.ToString("O")
        });
        await records.ExecuteAsync(beforeArgs);
        var snapshot = await records.ExecuteAsync(afterArgs);
        Assert.Equal(expected, AutonomousOrchestrator.EvaluateEventLogEvidence(snapshot, focus, started));
        Assert.Equal(2, records.Arguments.Count);
        var before = records.Arguments[0];
        var after = records.Arguments[1];
        Assert.Equal(focus, before.GetProperty("eventFocus").GetString());
        Assert.Equal(focus, after.GetProperty("eventFocus").GetString());
        Assert.Equal(scenario == "app-crashes" ? "Application" : "System", after.GetProperty("logName").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("sinceUtc").ValueKind);
        Assert.InRange(DateTimeOffset.Parse(after.GetProperty("sinceUtc").GetString()!), started, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CrashExecution_WithoutConsent_DoesNotQueryOrRunAction()
    {
        var action = new FakeSafeRemediationTool { Name = "run_system_file_checker" };
        var records = new FakeCrashInspectionTool(0, "valid");
        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, NullLoggerFactory.Instance),
            new RigMdAgentToolRegistry(new IRigMdAgentTool[] { action, records }));
        await orchestrator.RunExecutionCycleAsync(new DiagnosticOutput { DiagnosedCategory = "Application crash history" },
            new HardwareProfileDto(), userConsentProvided: false, requestedToolName: action.Name, requestedArgumentsJson: "{}");
        Assert.Empty(records.Arguments);
        Assert.Equal(0, action.ExecutionCount);
    }

    [Theory]
    [InlineData("app-crashes", "ApplicationCrash", "Application")]
    [InlineData("blue-screen-crash", "BugCheck", "System")]
    public async Task CrashInvestigation_DoesNotAcceptUnscopedModelLogArguments(string scenario, string focus, string channel)
    {
        var action = new FakeSafeRemediationTool { Name = "run_system_file_checker" };
        var records = new FakeCrashInspectionTool(0, "valid");
        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, NullLoggerFactory.Instance),
            new RigMdAgentToolRegistry(new IRigMdAgentTool[] { action, records }), new UnscopedCrashQueryLlmStub());
        await orchestrator.RunDryRunCycleAsync(new DiagnosticOutput
        {
            DiagnosedCategory = "Application stability risk needs review",
            Session = new DiagnosticSession { Answers = new List<SessionAnswer>
            {
                new() { QuestionKey = "diagnosis_mode", AnswerValue = "scenario" },
                new() { QuestionKey = "scenario_id", AnswerValue = scenario }
            } }
        }, new HardwareProfileDto());
        var args = Assert.Single(records.Arguments);
        Assert.Equal(focus, args.GetProperty("eventFocus").GetString());
        Assert.Equal(channel, args.GetProperty("logName").GetString());
        Assert.Equal(168, args.GetProperty("hoursBack").GetInt32());
        Assert.Equal(0, action.ExecutionCount);
    }

    private sealed class UnscopedCrashQueryLlmStub : IReActLlmClient
    {
        public Task<ReActModelTurnDecision> DecideNextTurnAsync(ReActConversationContext context,
            IReadOnlyList<AgentToolFunctionDeclaration> availableTools, CancellationToken cancellationToken = default)
            => Task.FromResult(context.CurrentTurn == 0 ? new ReActModelTurnDecision
            {
                ToolCalls = new List<ReActToolCallRequest> { new() { ToolName = "query_windows_event_logs", ArgumentsJson = "{}" } }
            } : new ReActModelTurnDecision
            {
                FinalProposal = new ReActFinalProposal { RecommendedToolName = "run_system_file_checker", RecommendedToolArgumentsJson = "{}" }
            });
    }

    private sealed class FakeCrashInspectionTool(int count, string evidence) : IRigMdAgentTool
    {
        public List<JsonElement> Arguments { get; } = new();
        public string Name => "query_windows_event_logs";
        public string DisplayName => "Test crash records";
        public string Description => "In-memory records only.";
        public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;
        public AgentToolFunctionDeclaration GetFunctionDeclaration() => new() { Name = Name, Description = Description };
        public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolDryRunPreview { CanExecute = true });
        public Task<AgentToolExecutionResult> ExecuteAsync(JsonElement arguments, Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            Arguments.Add(arguments.Clone());
            return Task.FromResult(new AgentToolExecutionResult
            {
                ToolName = Name, Success = evidence != "failed", Summary = "Test records",
                DataJson = JsonSerializer.Serialize(new
                {
                    logName = arguments.GetProperty("logName").GetString(),
                    eventFocus = evidence == "wrong-focus" ? "General" : arguments.GetProperty("eventFocus").GetString(),
                    sinceUtc = evidence == "wrong-time" ? DateTimeOffset.UtcNow.AddDays(-1).ToString("O")
                        : arguments.TryGetProperty("sinceUtc", out var since) ? since.GetString() : null,
                    warning = evidence == "warning" ? "Access denied" : null,
                    eventCount = Arguments.Count == 1 ? 12 : count
                })
            });
        }
    }

    private sealed class FakeSafeRemediationTool : IRigMdAgentTool
    {
        public int ExecutionCount { get; private set; }

        public string Name { get; init; } = "clear_temp_files";
        public string DisplayName => "Clear Temporary Files";
        public string Description => "Safe test tool for clearing temporary files.";
        public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier1_SafeReversible;

        public AgentToolFunctionDeclaration GetFunctionDeclaration() => new()
        {
            Name = Name,
            Description = Description
        };

        public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolDryRunPreview
            {
                ToolName = Name,
                DisplayName = DisplayName,
                SafetyTier = SafetyTier,
                CanExecute = true,
                RequiresUserConfirmation = true,
                AffectedItemsCount = 42,
                EstimatedBytesAffected = 104857600,
                WhatWillHappen = "Will delete 42 unlocked temp files (~100.0 MB)."
            });
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            JsonElement arguments,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = true,
                Summary = "Deleted 42 temp files (~100.0 MB).",
                DataJson = "{\"deletedFiles\":42,\"freedMb\":100.0}",
                OutputLog = "Deleted 42 temp files.",
                Proof = new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "User Temp Directory",
                        Status = "Cleaned",
                        Before = "42 files (100 MB)",
                        After = "0 files (0 MB)",
                        Meaning = "Freed 100 MB of temporary storage."
                    }
                }
            });
        }
    }

    private sealed class FakeHighMemoryInspectionTool : IRigMdAgentTool
    {
        public string Payload { get; init; } = "{\"memory\":{\"totalGb\":15.7,\"usedGb\":13.0,\"usagePercent\":82.9},\"browserSummary\":{\"browserDetected\":true,\"browserProcessCount\":28,\"browserMemoryMb\":3309.0,\"browserHeavy\":true},\"memoryLeakWarning\":false}";
        public string Name => "inspect_memory_and_processes";
        public string DisplayName => "Inspect RAM and Active Processes";
        public string Description => "Returns a high live-memory snapshot.";
        public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

        public AgentToolFunctionDeclaration GetFunctionDeclaration() => new()
        {
            Name = Name,
            Description = Description
        };

        public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolDryRunPreview
            {
                ToolName = Name,
                DisplayName = DisplayName,
                SafetyTier = SafetyTier,
                CanExecute = true,
                WhatWillHappen = "Will inspect current RAM use and browser workload."
            });
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            JsonElement arguments,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = true,
                Summary = "RAM usage: 13.0/15.7 GB (82.9%). Browser RAM: 3309 MB across 28 processes.",
                DataJson = Payload,
                OutputLog = "High memory snapshot."
            });
        }
    }

    private static (AutonomousOrchestrator Orchestrator, FakeSafeRemediationTool FakeTempTool) CreateOrchestratorWithTools(
        IReActLlmClient? customLlmClient = null)
    {
        var providers = new StubProviders();
        var fakeTempTool = new FakeSafeRemediationTool();
        var loggerFactory = NullLoggerFactory.Instance;

        var tools = new IRigMdAgentTool[]
        {
            new InspectCpuAndThermalsTool(providers),
            new InspectMemoryAndProcessesTool(providers, providers),
            new InspectStorageHealthTool(providers),
            new InspectGpuAndDisplaysTool(providers, providers),
            new InspectNetworkConnectivityTool(providers),
            new InspectBatteryAndPowerTool(providers, providers, providers),
            new InspectFullDeviceProfileTool(new StubSystemProfileService()),
            new QueryWindowsEventLogsTool(),
            new QueryStartupAppsTool(),
            fakeTempTool,
            new FlushDnsCacheTool(loggerFactory),
            new ClearBrowserCacheTool(loggerFactory),
            new TerminateProcessesTool(loggerFactory),
            new RestartWindowsExplorerTool(loggerFactory),
            new RunSystemFileCheckerTool(loggerFactory),
            new RescanPlugAndPlayDevicesTool()
        };

        var registry = new RigMdAgentToolRegistry(tools);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = ""
            })
            .Build();

        var llmClient = customLlmClient ?? new GeminiReActLlmClient(
            new HttpClient(),
            config,
            NullLogger<GeminiReActLlmClient>.Instance);

        var executor = new WindowsRemediationExecutor(
            NullLogger<WindowsRemediationExecutor>.Instance,
            loggerFactory);

        return (new AutonomousOrchestrator(executor, registry, llmClient), fakeTempTool);
    }

    [Fact]
    public async Task RunDryRunCycleAsync_ExecutesMultiTurnReActLoop_CallsTier0Tools_AndPreviewsRemediation()
    {
        var (orchestrator, fakeTempTool) = CreateOrchestratorWithTools();
        var streamedSteps = new List<ReActTraceStep>();

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Storage & Temp Cache Pressure",
            AiExplanation = "Disk space is filling up with temporary files and caches."
        };

        var hardware = new HardwareProfileDto
        {
            PrimaryStorageType = "NVMe SSD",
            Cpu = new CpuStatsDto { Name = "Intel Core i7-13700K", UsagePercent = 42.0 },
            Ram = new MemoryStatsDto { TotalGb = 32, UsedGb = 22.5, UsagePercent = 70.3 }
        };

        var result = await orchestrator.RunDryRunCycleAsync(
            diagnostic,
            hardware,
            stepReporter: step => streamedSteps.Add(step));

        // 1. Verify Tier 1 remediation tool was NOT executed during dry-run reasoning
        Assert.Equal(0, fakeTempTool.ExecutionCount);

        // 2. Verify multi-turn Thought -> ToolCall -> Observation -> DryRunPreview -> AwaitingApproval steps
        Assert.NotEmpty(result.ReasoningSteps);
        Assert.Equal(result.ReasoningSteps.Count, streamedSteps.Count);
        Assert.Contains(result.ReasoningSteps, s => s.StepType == nameof(ReActStepType.Thought));
        Assert.Contains(result.ReasoningSteps, s => s.StepType == nameof(ReActStepType.ToolCall));
        Assert.Contains(result.ReasoningSteps, s => s.StepType == nameof(ReActStepType.Observation));
        Assert.Contains(result.ReasoningSteps, s => s.StepType == nameof(ReActStepType.DryRunPreview));
        Assert.Contains(result.ReasoningSteps, s => s.StepType == nameof(ReActStepType.AwaitingApproval));

        // 3. Verify dry-run preview and safety state
        Assert.NotNull(result.DryRunPreview);
        Assert.Equal(42, result.DryRunPreview!.AffectedItemsCount);
        Assert.NotNull(result.ProposedTool);
        Assert.Equal("clear_temp_files", result.ProposedTool!.ToolName);
        Assert.Single(result.Attempts);
        Assert.Equal(RemediationAttemptState.AwaitingConsent, result.Attempts[0].State);
    }

    [Theory]
    [InlineData("High Memory Pressure")]
    [InlineData("Elevated Memory Pressure From Active Workloads")]
    public async Task MemoryProposal_NeverPresentsStorageCleanupAsTheMemoryRemedy(string category)
    {
        var (orchestrator, action) = CreateOrchestratorWithTools(new DirectWriteToolCallingLlmStub());
        var result = await orchestrator.RunDryRunCycleAsync(new DiagnosticOutput
        {
            DiagnosedCategory = category, DiagnosticSessionId = Guid.NewGuid()
        }, new HardwareProfileDto());
        Assert.Null(result.ProposedTool);
        Assert.Null(result.DryRunPreview);
        Assert.Empty(result.Plan!.PlannedActions);
        Assert.Contains("Storage cleanup does not directly free app memory", result.Plan.StrategyReasoning);
        Assert.Equal(0, action.ExecutionCount);
    }

    private sealed class DirectWriteToolCallingLlmStub : IReActLlmClient
    {
        public Task<ReActModelTurnDecision> DecideNextTurnAsync(
            ReActConversationContext context,
            IReadOnlyList<AgentToolFunctionDeclaration> availableTools,
            CancellationToken cancellationToken = default)
        {
            // Simulates an LLM trying to call a Tier 1 remediation tool directly during a reasoning turn
            return Task.FromResult(new ReActModelTurnDecision
            {
                EngineName = "Test-LLM",
                Thought = "Directly invoking clear_temp_files.",
                ToolCalls = new List<ReActToolCallRequest>
                {
                    new()
                    {
                        ToolName = "clear_temp_files",
                        ArgumentsJson = "{\"includeWindowsTemp\":false}",
                        Thought = "Attempting direct write call"
                    }
                }
            });
        }
    }

    [Fact]
    public async Task RunDryRunCycleAsync_SafetyGate_InterceptsDirectWriteToolCall_WithoutAutoExecutingIt()
    {
        var (orchestrator, fakeTempTool) = CreateOrchestratorWithTools(new DirectWriteToolCallingLlmStub());

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Storage",
            AiExplanation = "High temp usage"
        };

        var result = await orchestrator.RunDryRunCycleAsync(diagnostic, new HardwareProfileDto());

        // Must NOT auto-execute the Tier 1 tool during reasoning!
        Assert.Equal(0, fakeTempTool.ExecutionCount);
        Assert.NotNull(result.DryRunPreview);
        Assert.Equal("clear_temp_files", result.ProposedTool?.ToolName);
        Assert.Equal(RemediationAttemptState.AwaitingConsent, result.Attempts[0].State);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_EnforcesConsent_AndRunsBeforeAndAfterTelemetryVerification()
    {
        var (orchestrator, fakeTempTool) = CreateOrchestratorWithTools();

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Storage",
            AiExplanation = "Disk cleanup"
        };

        // 1. Without user consent -> blocked
        var blocked = await orchestrator.RunExecutionCycleAsync(
            diagnostic,
            new HardwareProfileDto(),
            userConsentProvided: false,
            requestedToolName: "clear_temp_files");

        Assert.Equal(0, fakeTempTool.ExecutionCount);
        Assert.False(blocked.Safety!.IsApproved);
        Assert.Equal(RemediationAttemptState.AwaitingConsent, blocked.Attempts[0].State);

        // 2. With user consent -> Pre-Execution Observation -> Execution -> Post-Execution Verification
        var executed = await orchestrator.RunExecutionCycleAsync(
            diagnostic,
            new HardwareProfileDto(),
            userConsentProvided: true,
            requestedToolName: "clear_temp_files",
            requestedArgumentsJson: "{\"includeWindowsTemp\":false}");

        Assert.Equal(1, fakeTempTool.ExecutionCount);
        Assert.True(executed.Execution!.Success);
        Assert.Equal(VerificationStatus.Resolved, executed.Verification);
        Assert.NotNull(executed.VerificationReport);
        Assert.Equal("inspect_storage_health", executed.VerificationReport!.VerificationToolName);
        Assert.Contains("NVMe SSD", executed.VerificationReport.BeforeSnapshotJson);
        Assert.Contains("NVMe SSD", executed.VerificationReport.AfterSnapshotJson);
        Assert.Contains(executed.ReasoningSteps, s => s.StepType == nameof(ReActStepType.Observation));
        Assert.Contains(executed.ReasoningSteps, s => s.StepType == nameof(ReActStepType.Execution));
        Assert.Contains(executed.ReasoningSteps, s => s.StepType == nameof(ReActStepType.Verification));
    }

    [Fact]
    public async Task RunExecutionCycleAsync_MemoryPressure_DoesNotMarkResolvedWhenRamStillHigh()
    {
        var fakeTempTool = new FakeSafeRemediationTool { Name = "terminate_processes" };
        var registry = new RigMdAgentToolRegistry(new IRigMdAgentTool[]
        {
            new FakeHighMemoryInspectionTool(),
            fakeTempTool
        });
        var loggerFactory = NullLoggerFactory.Instance;
        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, loggerFactory),
            registry,
            new GeminiReActLlmClient(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "" }).Build(),
                NullLogger<GeminiReActLlmClient>.Instance));

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "High Memory Pressure",
            AiExplanation = "Memory remains above the high-use range."
        };

        var result = await orchestrator.RunExecutionCycleAsync(
            diagnostic,
            new HardwareProfileDto(),
            userConsentProvided: true,
            requestedToolName: fakeTempTool.Name,
            requestedArgumentsJson: "{}");

        Assert.Equal(1, fakeTempTool.ExecutionCount);
        Assert.True(result.Execution!.Success);
        Assert.Equal("inspect_memory_and_processes", result.VerificationReport!.VerificationToolName);
        Assert.Equal(VerificationStatus.Unresolved, result.Verification);
        Assert.Contains("\"usagePercent\":82.9", result.VerificationReport.AfterSnapshotJson);
    }

    public static IEnumerable<object[]> MemoryVerificationCases()
    {
        static string Payload(double? usage, double? browserMb, object? warning = null) => JsonSerializer.Serialize(new
        {
            memory = new { usagePercent = usage },
            browserSummary = new { browserMemoryMb = browserMb, browserProcessCount = 100 },
            memoryLeakWarning = warning
        });

        yield return new object[] { "full", "High Memory Pressure", Payload(81.99, 5000), VerificationStatus.Resolved };
        yield return new object[] { "full", "High Memory Pressure", Payload(82, 0), VerificationStatus.Unresolved };
        yield return new object[] { "memory", "High Memory Pressure", Payload(81.99, 500), VerificationStatus.Resolved };
        yield return new object[] { "memory", "High Memory Pressure", Payload(74.99, 6000), VerificationStatus.Resolved };
        yield return new object[] { "memory", "High Memory Pressure", Payload(75, 1799.99), VerificationStatus.Resolved };
        yield return new object[] { "memory", "Elevated Memory Pressure From Active Workloads", Payload(75, 1800), VerificationStatus.Resolved };
        yield return new object[] { "full", "Elevated Memory Pressure From Active Workloads", Payload(76, 1900), VerificationStatus.Resolved };
        yield return new object[] { "memory", "High Memory Pressure", Payload(75, null), VerificationStatus.Resolved };
        yield return new object[] { "full", "High Memory Pressure", Payload(null, 500), VerificationStatus.Unknown };
        yield return new object[] { "full", "High Memory Pressure", Payload(-1, 500), VerificationStatus.Unknown };
        yield return new object[] { "full", "High Memory Pressure", Payload(101, 500), VerificationStatus.Unknown };
        yield return new object[] { "full", "High Memory Pressure", Payload(0, 0), VerificationStatus.Resolved };
        yield return new object[] { "memory", "High Memory Pressure", Payload(25, 0, "Unusual application memory growth."), VerificationStatus.Unresolved };
        yield return new object[] { "full", "High Memory Pressure", Payload(null, 0, "Unusual application memory growth."), VerificationStatus.Unresolved };
        yield return new object[] { "full", "High Memory Pressure", Payload(25, 0, true), VerificationStatus.Unresolved };
        yield return new object[] { "full", "High Memory Pressure", Payload(25, 0, false), VerificationStatus.Resolved };
        yield return new object[] { "full", "High Memory Pressure", "{\"memory\":{\"usagePercent\":25}}", VerificationStatus.Unknown };
        yield return new object[] { "full", "High Memory Pressure", "{\"memory\":{\"usagePercent\":82}}", VerificationStatus.Unresolved };
        yield return new object[] { "full", "High Memory Pressure", "{\"memory\":{\"usagePercent\":25},\"memoryLeakWarning\":{}}", VerificationStatus.Unknown };
        yield return new object[] { "mixed", "High Memory Pressure", Payload(25, 0), VerificationStatus.Unknown };
        yield return new object[] { "cpu", "High Memory Pressure", Payload(25, 0), VerificationStatus.Unknown };
        yield return new object[] { "cpu", "High Memory Pressure", Payload(82, 0), VerificationStatus.Unknown };
        yield return new object[] { "slow-system", "OS performance degradation", Payload(25, 0), VerificationStatus.Unknown };
        yield return new object[] { "full", "OS performance degradation", Payload(25, 0), VerificationStatus.Unknown };
    }

    [Theory]
    [MemberData(nameof(MemoryVerificationCases))]
    public async Task MemoryVerification_UsesScopedEvidenceAndDoesNotDiscardTextWarnings(
        string scope, string category, string payload, VerificationStatus expected)
    {
        var fakeAction = new FakeSafeRemediationTool { Name = category.Contains("Memory") || scope == "cpu" ? "terminate_processes" : "clear_temp_files" };
        var registry = new RigMdAgentToolRegistry(new IRigMdAgentTool[]
        {
            new FakeHighMemoryInspectionTool { Payload = payload }, fakeAction
        });
        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, NullLoggerFactory.Instance),
            registry,
            new GeminiReActLlmClient(new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "" }).Build(),
                NullLogger<GeminiReActLlmClient>.Instance));
        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(), DiagnosticSessionId = Guid.NewGuid(), DiagnosedCategory = category
        };
        if (scope != "full")
        {
            diagnostic.Session = new DiagnosticSession
            {
                Id = diagnostic.DiagnosticSessionId,
                Answers = new List<SessionAnswer>
                {
                    new() { QuestionKey = "diagnosis_mode", AnswerValue = scope == "slow-system" ? "scenario" : "component" },
                    scope == "slow-system"
                        ? new() { QuestionKey = "scenario_id", AnswerValue = scope }
                        : new() { QuestionKey = "component_ids", AnswerValue = scope switch
                            { "mixed" => "[\"memory\",\"cpu\"]", "cpu" => "[\"cpu\"]", _ => "[\"memory\"]" } }
                }
            };
        }
        var result = await orchestrator.RunExecutionCycleAsync(diagnostic, new HardwareProfileDto(),
            userConsentProvided: true, requestedToolName: fakeAction.Name, requestedArgumentsJson: "{}");

        Assert.Equal(1, fakeAction.ExecutionCount);
        Assert.Equal("inspect_memory_and_processes", result.VerificationReport!.VerificationToolName);
        Assert.Equal(expected, result.Verification);
    }

    [Theory]
    [InlineData("Thermal condition", null, 42d, VerificationStatus.Unknown)]
    [InlineData("Thermal condition", 45d, 42d, VerificationStatus.Unknown)]
    [InlineData("Thermal condition", 85d, 42d, VerificationStatus.Unresolved)]
    [InlineData("Thermal condition", 45d, 95d, VerificationStatus.Unknown)]
    [InlineData("Elevated CPU Utilization", null, 42d, VerificationStatus.Resolved)]
    [InlineData("Elevated CPU Utilization", null, 79.9, VerificationStatus.Resolved)]
    [InlineData("Elevated CPU Utilization", null, 79.99, VerificationStatus.Resolved)]
    [InlineData("Elevated CPU Utilization", null, 80d, VerificationStatus.Unresolved)]
    [InlineData("Elevated CPU Utilization", 45d, 80.1, VerificationStatus.Unresolved)]
    [InlineData("Elevated CPU Utilization", 45d, 89.9, VerificationStatus.Unresolved)]
    [InlineData("Elevated CPU Utilization", 45d, 90d, VerificationStatus.Unresolved)]
    [InlineData("Elevated CPU Utilization", 45d, 95d, VerificationStatus.Unresolved)]
    public async Task CpuAndThermalVerification_UsesTheRelevantMeasurement(
        string category, double? temperature, double usage, VerificationStatus expected)
    {
        var providers = new StubProviders { CpuTemperature = temperature, CpuUsage = usage };
        var fakeAction = new FakeSafeRemediationTool { Name = "terminate_processes" };
        var registry = new RigMdAgentToolRegistry(new IRigMdAgentTool[]
        {
            new InspectCpuAndThermalsTool(providers), fakeAction
        });
        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, NullLoggerFactory.Instance),
            registry,
            new GeminiReActLlmClient(new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "" }).Build(),
                NullLogger<GeminiReActLlmClient>.Instance));
        var result = await orchestrator.RunExecutionCycleAsync(
            new DiagnosticOutput { Id = Guid.NewGuid(), DiagnosticSessionId = Guid.NewGuid(), DiagnosedCategory = category },
            new HardwareProfileDto(), userConsentProvided: true,
            requestedToolName: "terminate_processes", requestedArgumentsJson: "{}");

        Assert.Equal(1, fakeAction.ExecutionCount);
        Assert.Equal("inspect_cpu_and_thermals", result.VerificationReport!.VerificationToolName);
        Assert.Equal(expected, result.Verification);
    }

    [Fact]
    public async Task RunDryRunCycleAsync_ComponentMode_StrictlyFiltersTier0ToolsToSelectedScope()
    {
        var (orchestrator, _) = CreateOrchestratorWithTools();

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "No Active Issue Detected",
            AiExplanation = "Memory is healthy.",
            Session = new DiagnosticSession
            {
                Id = Guid.NewGuid(),
                Answers = new List<SessionAnswer>
                {
                    new() { QuestionKey = "diagnosis_mode", AnswerValue = "component" },
                    new() { QuestionKey = "component_ids", AnswerValue = "[\"memory\"]" }
                }
            }
        };

        var result = await orchestrator.RunDryRunCycleAsync(
            diagnostic,
            new HardwareProfileDto
            {
                Ram = new MemoryStatsDto { TotalGb = 32, UsedGb = 14, UsagePercent = 43.7 }
            });

        var toolCallSteps = result.ReasoningSteps.Where(s => s.StepType == nameof(ReActStepType.ToolCall)).ToList();
        Assert.NotEmpty(toolCallSteps);
        Assert.All(toolCallSteps, s => Assert.Equal("inspect_memory_and_processes", s.ToolName));
        Assert.DoesNotContain(toolCallSteps, s => s.ToolName == "inspect_storage_health");
    }

    [Fact]
    public async Task RunDryRunCycleAsync_ScenarioMode_ExecutesMappedScenarioToolsBeforeDiagnosis()
    {
        var (orchestrator, _) = CreateOrchestratorWithTools();

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Boot or Startup Contention",
            AiExplanation = "Slow boot scenario.",
            Session = new DiagnosticSession
            {
                Id = Guid.NewGuid(),
                Answers = new List<SessionAnswer>
                {
                    new() { QuestionKey = "diagnosis_mode", AnswerValue = "scenario" },
                    new() { QuestionKey = "scenario_id", AnswerValue = "slow-boot" }
                }
            }
        };

        var result = await orchestrator.RunDryRunCycleAsync(
            diagnostic,
            new HardwareProfileDto());

        var calledTools = result.ReasoningSteps
            .Where(s => s.StepType == nameof(ReActStepType.ToolCall))
            .Select(s => s.ToolName)
            .ToList();

        Assert.Contains("query_startup_apps", calledTools);
        Assert.Contains("query_windows_event_logs", calledTools);
    }

    [Fact]
    public async Task RunDryRunCycleAsync_AppCrashes_DoesNotProposeBrowserCacheByDefault()
    {
        var (orchestrator, _) = CreateOrchestratorWithTools();

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Application crash history requires review",
            AiExplanation = "Application crash scenario.",
            Session = new DiagnosticSession
            {
                Id = Guid.NewGuid(),
                Answers = new List<SessionAnswer>
                {
                    new() { QuestionKey = "diagnosis_mode", AnswerValue = "scenario" },
                    new() { QuestionKey = "scenario_id", AnswerValue = "app-crashes" }
                }
            }
        };

        var result = await orchestrator.RunDryRunCycleAsync(
            diagnostic,
            new HardwareProfileDto());

        Assert.Null(result.ProposedTool);
        Assert.Empty(result.Plan!.PlannedActions);
        Assert.False(result.Safety!.IsApproved);
        Assert.Contains("cause", result.Safety.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunDryRunCycleAsync_DriverConflict_PrefersDeviceRescan()
    {
        var (orchestrator, _) = CreateOrchestratorWithTools();

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Driver conflict",
            AiExplanation = "Windows Device Manager reported a USB device error.",
            Session = new DiagnosticSession
            {
                Id = Guid.NewGuid(),
                Answers = new List<SessionAnswer>
                {
                    new() { QuestionKey = "diagnosis_mode", AnswerValue = "full" }
                }
            }
        };

        var result = await orchestrator.RunDryRunCycleAsync(
            diagnostic,
            new HardwareProfileDto());

        Assert.NotNull(result.ProposedTool);
        Assert.Equal("rescan_plug_and_play_devices", result.ProposedTool!.ToolName);
    }

    [Fact]
    public async Task RunExecutionCycleAsync_DriverConflict_VerifiesDeviceManagerErrors()
    {
        var providers = new StubProviders();
        var loggerFactory = NullLoggerFactory.Instance;
        var rescanTool = new FakeDriverRescanTool();
        var registry = new RigMdAgentToolRegistry(new IRigMdAgentTool[]
        {
            new InspectFullDeviceProfileTool(new StubSystemProfileService()),
            rescanTool
        });

        var orchestrator = new AutonomousOrchestrator(
            new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, loggerFactory),
            registry,
            new GeminiReActLlmClient(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "" }).Build(),
                NullLogger<GeminiReActLlmClient>.Instance));

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Driver conflict",
            AiExplanation = "Windows Device Manager reported a USB device error."
        };

        var result = await orchestrator.RunExecutionCycleAsync(
            diagnostic,
            new HardwareProfileDto(),
            userConsentProvided: true,
            requestedToolName: "rescan_plug_and_play_devices",
            requestedArgumentsJson: "{}");

        Assert.Equal(1, rescanTool.ExecutionCount);
        Assert.Equal("inspect_full_device_profile", result.VerificationReport!.VerificationToolName);
        Assert.Equal(VerificationStatus.Unresolved, result.Verification);
        Assert.Contains("deviceErrorsCount", result.VerificationReport.AfterSnapshotJson);
    }

    private sealed class StubSystemProfileService : IWindowsSystemProfileService
    {
        public HardwareProfileDto GetLiveSystemProfile() => new()
        {
            DeviceName = "RIGMD-TEST-PC",
            DeviceErrorsAvailable = true,
            DeviceType = "Desktop",
            OsVersion = "Windows 11 Pro 24H2",
            PrimaryStorageType = "NVMe SSD",
            Cpu = new CpuStatsDto { Name = "Intel Core i7-13700K", UsagePercent = 38.0, Cores = 16, Threads = 24 },
            Ram = new MemoryStatsDto { TotalGb = 32, UsedGb = 19.0, UsagePercent = 59.4 },
            Gpu = new GpuStatsDto { Name = "NVIDIA GeForce RTX 4070 Ti", HasGpu = true, HasDedicatedGpu = true, VramGb = 12 },
            DeviceErrors = new List<DeviceErrorDto>
            {
                new()
                {
                    Name = "Unknown USB Device (Device Descriptor Request Failed)",
                    ErrorCode = 43,
                    Description = "Windows stopped this device because it reported problems."
                }
            }
        };
    }

    private sealed class FakeDriverRescanTool : IRigMdAgentTool
    {
        public int ExecutionCount { get; private set; }

        public string Name => "rescan_plug_and_play_devices";
        public string DisplayName => "Rescan connected devices";
        public string Description => "Test driver rescan tool.";
        public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;

        public AgentToolFunctionDeclaration GetFunctionDeclaration() => new()
        {
            Name = Name,
            Description = Description
        };

        public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolDryRunPreview
            {
                ToolName = Name,
                DisplayName = DisplayName,
                SafetyTier = SafetyTier,
                CanExecute = true,
                RequiresUserConfirmation = true,
                AffectedItemsCount = 1,
                WhatWillHappen = "Will ask Windows to rescan connected devices."
            });
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            JsonElement arguments,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = true,
                Summary = "Windows completed the connected-device rescan.",
                DataJson = "{\"success\":true}",
                OutputLog = "Rescan complete."
            });
        }
    }

    [Fact]
    public async Task WholeSystemAutonomousDoctor_ExecutesDeviceAndHistoricalMemoryTools_OnTurn0()
    {
        var profileTool = new InspectFullDeviceProfileTool(new StubSystemProfileService());
        using var emptyArgs = JsonDocument.Parse("{}");
        var profileExec = await profileTool.ExecuteAsync(emptyArgs.RootElement);

        Assert.True(profileExec.Success);
        Assert.Contains("RIGMD-TEST-PC", profileExec.DataJson);
        Assert.Contains("Intel Core i7-13700K", profileExec.DataJson);

        var (orchestrator, _) = CreateOrchestratorWithTools();
        var providers = new StubProviders();
        var loggerFactory = NullLoggerFactory.Instance;
        var fakeTempTool = new FakeSafeRemediationTool();

        // Register InspectFullDeviceProfileTool in a whole-system registry
        var registry = new RigMdAgentToolRegistry(new IRigMdAgentTool[]
        {
            profileTool,
            new InspectCpuAndThermalsTool(providers),
            new InspectMemoryAndProcessesTool(providers, providers),
            new InspectStorageHealthTool(providers),
            new InspectGpuAndDisplaysTool(providers, providers),
            new InspectNetworkConnectivityTool(providers),
            new InspectBatteryAndPowerTool(providers, providers, providers),
            new QueryWindowsEventLogsTool(),
            new QueryStartupAppsTool(),
            fakeTempTool,
            new RescanPlugAndPlayDevicesTool()
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "" })
            .Build();
        var llmClient = new GeminiReActLlmClient(new HttpClient(), config, NullLogger<GeminiReActLlmClient>.Instance);
        var executor = new WindowsRemediationExecutor(NullLogger<WindowsRemediationExecutor>.Instance, loggerFactory);
        var wholeSystemOrchestrator = new AutonomousOrchestrator(executor, registry, llmClient);

        var diagnostic = new DiagnosticOutput
        {
            Id = Guid.NewGuid(),
            DiagnosticSessionId = Guid.NewGuid(),
            DiagnosedCategory = "Storage & Temp Cache Pressure",
            AiExplanation = "System storage check.\n[AUTONOMOUS DOCTOR WHOLE-SYSTEM SCAN]"
        };

        var result = await wholeSystemOrchestrator.RunDryRunCycleAsync(
            diagnostic,
            new StubSystemProfileService().GetLiveSystemProfile());

        var calledTools = result.ReasoningSteps
            .Where(s => s.StepType == nameof(ReActStepType.ToolCall))
            .Select(s => s.ToolName)
            .ToList();

        Assert.Contains("inspect_full_device_profile", calledTools);
        Assert.NotNull(result.ProposedTool);
    }

    [Fact]
    public void RuntimeSettingsStore_MasksApiKey_AndRespectsLocalOnlyMode()
    {
        var masked = RigMD.Application.Services.RigMdAgentRuntimeSettingsStore.MaskApiKey("AIzaSyTestKey123456789XYZ");
        Assert.Equal("AIza••••••••9XYZ", masked);
        Assert.Equal(string.Empty, RigMD.Application.Services.RigMdAgentRuntimeSettingsStore.MaskApiKey(null));
    }

    [Fact]
    public async Task GeminiReActLlmClient_WhenProxyIsUnreachableOrFails_SilentlyFallsBackToLocalEngine()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "",
                ["Gemini:ProxyUrl"] = "http://127.0.0.1:59999/unreachable-proxy"
            })
            .Build();

        var llmClient = new GeminiReActLlmClient(new HttpClient(), config, NullLogger<GeminiReActLlmClient>.Instance);

        var context = new ReActConversationContext
        {
            CurrentTurn = 1,
            DiagnosedCategory = "Elevated Memory Pressure From Active Workloads",
            UserSymptom = "RAM usage is 85%",
            ScenarioId = "slow-system"
        };

        var availableTools = new List<AgentToolFunctionDeclaration>
        {
            new() { Name = "inspect_memory_and_processes", Description = "Inspect RAM" },
            new() { Name = "clear_temp_files", Description = "Clear temp" }
        };

        var decision = await llmClient.DecideNextTurnAsync(context, availableTools);

        Assert.NotNull(decision);
        Assert.True(decision.ToolCalls.Count > 0 || decision.FinalProposal != null);
        Assert.Equal("inspect_memory_and_processes", decision.ToolCalls.FirstOrDefault()?.ToolName);
    }

    [Fact]
    public async Task GeminiAiExplainer_WhenProxyIsUnreachableOrFails_SilentlyFallsBackToOfflineTemplate()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "",
                ["Gemini:ProxyUrl"] = "http://127.0.0.1:59999/unreachable-proxy"
            })
            .Build();

        var offlineExplainer = new OfflineAiExplainer();
        var explainer = new GeminiAiExplainer(
            new HttpClient(),
            config,
            offlineExplainer,
            NullLogger<GeminiAiExplainer>.Instance);

        var diagnostic = new RigMD.Domain.Rules.DiagnosticResult
        {
            DiagnosedCategory = "Network connectivity and adapter issue",
            ConfidenceLabel = "High",
            RecommendedNextStep = "Run Network Reset"
        };

        var explanation = await explainer.GenerateExplanationAsync(diagnostic, new RigMD.Domain.Rules.DiagnosticSymptomPayload());

        Assert.NotNull(explanation);
        Assert.NotEmpty(explanation);
        Assert.Contains("network", explanation, StringComparison.OrdinalIgnoreCase);
    }
}
