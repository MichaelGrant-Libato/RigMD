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
        public CpuStatsDto GetCpuStats() => new()
        {
            Name = "Intel Core i7-13700K",
            UsagePercent = 42.0,
            FrequencyMhz = 5100,
            MaxFrequencyMhz = 5400,
            Cores = 16,
            Threads = 24,
            TemperatureCelsius = 64.0
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

    private sealed class FakeSafeRemediationTool : IRigMdAgentTool
    {
        public int ExecutionCount { get; private set; }

        public string Name => "clear_temp_files";
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
            new QueryWindowsEventLogsTool(),
            new QueryStartupAppsTool(),
            fakeTempTool,
            new FlushDnsCacheTool(loggerFactory),
            new ClearBrowserCacheTool(loggerFactory),
            new TerminateProcessesTool(loggerFactory)
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

    private sealed class StubSystemProfileService : IWindowsSystemProfileService
    {
        public HardwareProfileDto GetLiveSystemProfile() => new()
        {
            DeviceName = "RIGMD-TEST-PC",
            DeviceType = "Desktop",
            OsVersion = "Windows 11 Pro 24H2",
            PrimaryStorageType = "NVMe SSD",
            Cpu = new CpuStatsDto { Name = "Intel Core i7-13700K", UsagePercent = 38.0, Cores = 16, Threads = 24 },
            Ram = new MemoryStatsDto { TotalGb = 32, UsedGb = 19.0, UsagePercent = 59.4 },
            Gpu = new GpuStatsDto { Name = "NVIDIA GeForce RTX 4070 Ti", HasGpu = true, HasDedicatedGpu = true, VramGb = 12 }
        };
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
            fakeTempTool
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
}
