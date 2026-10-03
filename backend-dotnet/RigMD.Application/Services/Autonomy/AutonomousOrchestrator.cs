using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services.Autonomy;

/// <summary>
/// Phase 3 ReAct Autonomous Orchestrator:
/// Drives a multi-turn Thought -> Tool Call -> Observation loop using <see cref="IReActLlmClient"/>
/// and <see cref="IRigMdAgentToolRegistry"/>, enforces non-destructive dry-run previews and user consent
/// for Tier 1 / Tier 2 tools, and performs real before/after telemetry verification upon execution.
/// </summary>
public class AutonomousOrchestrator : IAutonomousOrchestrator
{
    private const double ElevatedCpuThreshold = 90;
    private const double ElevatedMemoryThreshold = 80;
    private const double BrowserMemoryPressureMb = 3000;
    private const int BrowserProcessPressureCount = 25;
    private const double ElevatedStorageThreshold = 80;
    private const double ElevatedTempThresholdCelsius = 85;
    private const double NetworkPacketLossThreshold = 5;
    private const double NetworkLatencyThresholdMs = 250;

    private readonly IRemediationExecutor _realExecutor;
    private readonly IRigMdAgentToolRegistry? _toolRegistry;
    private readonly IReActLlmClient? _llmClient;

    public AutonomousOrchestrator(
        IRemediationExecutor realExecutor,
        IRigMdAgentToolRegistry? toolRegistry = null,
        IReActLlmClient? llmClient = null)
    {
        _realExecutor = realExecutor;
        _toolRegistry = toolRegistry;
        _llmClient = llmClient;
    }

    public async Task<OrchestrationResult> RunDryRunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        Action<string>? progressReporter = null,
        Action<ReActTraceStep>? stepReporter = null,
        CancellationToken cancellationToken = default)
    {
        if (_toolRegistry == null || _llmClient == null)
        {
            return BuildFallbackDryRunResult(diagnostic);
        }

        var steps = new List<ReActTraceStep>();
        var traceBuilder = new StringBuilder();
        int stepCounter = 1;

        void EmitStep(ReActTraceStep step)
        {
            step.StepIndex = stepCounter++;
            steps.Add(step);
            var traceLine = $"[{step.StepType.ToUpperInvariant()}] {step.Title}: {step.Content}";
            traceBuilder.AppendLine(traceLine);
            progressReporter?.Invoke(traceLine);
            stepReporter?.Invoke(step);
        }

        var (diagnosisMode, componentIds, scenarioId) = ExtractSessionScope(diagnostic);
        var (allowedTier0Tools, allowedRemediationTools) = diagnosisMode == "component" && componentIds.Count > 0
            ? DiagnosticScopeMapper.GetAllowedToolsForComponents(componentIds)
            : (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var scopedRemediationTools =
            DiagnosticScopeMapper.GetAllowedRemediationTools(
                diagnosisMode,
                componentIds,
                scenarioId,
                diagnostic.DiagnosedCategory);
        var scopedRemediationSet =
            new HashSet<string>(scopedRemediationTools, StringComparer.OrdinalIgnoreCase);

        var requiredScenarioCalls = diagnosisMode == "scenario" && !string.IsNullOrWhiteSpace(scenarioId)
            ? DiagnosticScopeMapper.GetRequiredToolsForScenario(scenarioId).ToList()
            : new List<ReActToolCallRequest>();

        var targetScopeLabels = diagnosisMode == "component" && componentIds.Count > 0
            ? componentIds.Select(DiagnosticScopeMapper.GetComponentDisplayName).ToList()
            : new List<string>();

        var context = new ReActConversationContext
        {
            SessionId = diagnostic.DiagnosticSessionId.ToString(),
            DiagnosisMode = diagnosisMode,
            TargetScope = targetScopeLabels,
            ScenarioId = scenarioId,
            AllowedDiagnosticTools = allowedTier0Tools.ToList(),
            RequiredScenarioToolCalls = requiredScenarioCalls,
            UserSymptom = !string.IsNullOrWhiteSpace(diagnostic.AiExplanation)
                ? diagnostic.AiExplanation!
                : $"{diagnostic.DiagnosedCategory} {diagnostic.ActionCategory}".Trim(),
            DiagnosedCategory = diagnostic.DiagnosedCategory,
            InitialSummary =
                $"CPU: {hardware.Cpu.Name} ({hardware.Cpu.UsagePercent:F1}%), RAM: {hardware.Ram.UsedGb:F1}/{hardware.Ram.TotalGb:F1} GB ({hardware.Ram.UsagePercent:F1}%), Primary Storage: {hardware.PrimaryStorageType}",
            CurrentTurn = 0,
            MaxTurns = 4
        };

        var allDeclarations = _toolRegistry.GetFunctionDeclarations(includeWriteTools: true);
        var requiredScenarioToolNames = requiredScenarioCalls
            .Select(c => c.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declarations = allDeclarations
            .Where(d =>
            {
                var tool = _toolRegistry.GetTool(d.Name);
                if (tool == null)
                {
                    return false;
                }

                if (diagnosisMode == "component" && allowedTier0Tools.Count > 0)
                {
                    return tool.SafetyTier == ToolSafetyTier.Tier0_ReadOnly
                        ? allowedTier0Tools.Contains(d.Name)
                        : allowedRemediationTools.Contains(d.Name);
                }

                if (diagnosisMode == "scenario" && requiredScenarioToolNames.Count > 0)
                {
                    return tool.SafetyTier == ToolSafetyTier.Tier0_ReadOnly
                        ? requiredScenarioToolNames.Contains(d.Name)
                        : scopedRemediationSet.Count == 0 || scopedRemediationSet.Contains(d.Name);
                }

                return tool.SafetyTier == ToolSafetyTier.Tier0_ReadOnly ||
                       scopedRemediationSet.Count == 0 ||
                       scopedRemediationSet.Contains(d.Name);
            })
            .ToList();

        ReActFinalProposal? finalProposal = null;
        string engineMode = "ReAct-Agent";

        for (int turn = 0; turn < context.MaxTurns; turn++)
        {
            context.CurrentTurn = turn;
            var swTurn = Stopwatch.StartNew();
            var decision = await _llmClient.DecideNextTurnAsync(context, declarations, cancellationToken);
            swTurn.Stop();

            engineMode = decision.EngineName;

            // Scenario Gate: On Turn 0, ensure all scenario-mapped Tier-0 tools are executed before any FinalProposal
            if (turn == 0 && requiredScenarioCalls.Count > 0)
            {
                decision.FinalProposal = null;
                var plannedCalls = new List<ReActToolCallRequest>();
                foreach (var reqCall in requiredScenarioCalls)
                {
                    var matching = decision.ToolCalls.FirstOrDefault(c =>
                        string.Equals(c.ToolName, reqCall.ToolName, StringComparison.OrdinalIgnoreCase));
                    plannedCalls.Add(matching ?? reqCall);
                }
                decision.ToolCalls = plannedCalls;
            }

            if (!string.IsNullOrWhiteSpace(decision.Thought))
            {
                EmitStep(new ReActTraceStep
                {
                    StepType = nameof(ReActStepType.Thought),
                    Title = $"Reasoning Turn {turn + 1} ({decision.EngineName})",
                    Content = decision.Thought,
                    DurationMs = swTurn.ElapsedMilliseconds
                });
            }

            if (decision.FinalProposal != null)
            {
                finalProposal = decision.FinalProposal;
                break;
            }

            if (decision.ToolCalls.Count == 0)
            {
                break;
            }

            foreach (var call in decision.ToolCalls)
            {
                var tool = _toolRegistry.GetTool(call.ToolName);
                if (tool == null)
                {
                    continue;
                }

                // Scoped Component Gate: Never run unselected Tier-0 diagnostic tools in 'component' mode
                if (diagnosisMode == "component" &&
                    allowedTier0Tools.Count > 0 &&
                    tool.SafetyTier == ToolSafetyTier.Tier0_ReadOnly &&
                    !allowedTier0Tools.Contains(tool.Name) &&
                    !allowedTier0Tools.Contains(call.ToolName))
                {
                    continue;
                }

                // Safety Gate: Never auto-execute Tier 1 or Tier 2 remediation tools during reasoning turns!
                if (tool.SafetyTier != ToolSafetyTier.Tier0_ReadOnly)
                {
                    finalProposal = new ReActFinalProposal
                    {
                        RootCauseAnalysis = decision.Thought,
                        ConfidenceLevel = "High",
                        RecommendedToolName = tool.Name,
                        RecommendedToolArgumentsJson = call.ArgumentsJson,
                        RemediationRationale = $"Agent selected '{tool.DisplayName}' based on live diagnostic telemetry.",
                        EvidenceCitations = context.History.Select(h => $"{h.ToolName}: {h.ObservationSummary}").ToList()
                    };
                    break;
                }

                EmitStep(new ReActTraceStep
                {
                    StepType = nameof(ReActStepType.ToolCall),
                    Title = $"Calling Tool: {tool.DisplayName}",
                    Content = $"Executing read-only diagnostic tool '{call.ToolName}' with args {call.ArgumentsJson}",
                    ToolName = tool.Name,
                    ToolArgumentsJson = call.ArgumentsJson
                });

                var swTool = Stopwatch.StartNew();
                using var argsDoc = ParseJsonSafe(call.ArgumentsJson);
                var obs = await tool.ExecuteAsync(argsDoc.RootElement, progressReporter, cancellationToken);
                swTool.Stop();

                EmitStep(new ReActTraceStep
                {
                    StepType = nameof(ReActStepType.Observation),
                    Title = $"Observation from {tool.DisplayName}",
                    Content = obs.Summary,
                    ToolName = tool.Name,
                    ToolArgumentsJson = call.ArgumentsJson,
                    ObservationJson = obs.DataJson,
                    DurationMs = swTool.ElapsedMilliseconds
                });

                context.History.Add(new ReActTurnRecord
                {
                    TurnNumber = turn + 1,
                    Thought = call.Thought,
                    ToolName = tool.Name,
                    ArgumentsJson = call.ArgumentsJson,
                    ObservationSummary = obs.Summary,
                    ObservationJson = obs.DataJson
                });
            }

            if (finalProposal != null)
            {
                break;
            }
        }

        finalProposal ??= new ReActFinalProposal
        {
            RootCauseAnalysis = context.History.Count > 0
                ? string.Join(" ", context.History.Select(h => h.ObservationSummary))
                : "Completed baseline hardware inspection.",
            ConfidenceLevel = "Medium",
            RecommendedToolName = ResolveDefaultToolName(diagnostic.DiagnosedCategory),
            RecommendedToolArgumentsJson = "{}",
            RemediationRationale = "Selected safe OS maintenance tool based on diagnostic session context.",
            EvidenceCitations = context.History.Select(h => $"{h.ToolName}: {h.ObservationSummary}").ToList()
        };

        if (scopedRemediationSet.Count > 0 &&
            !string.IsNullOrWhiteSpace(finalProposal.RecommendedToolName) &&
            !scopedRemediationSet.Contains(finalProposal.RecommendedToolName))
        {
            var scopedToolName = scopedRemediationTools.FirstOrDefault(name => _toolRegistry.GetTool(name) != null);
            if (!string.IsNullOrWhiteSpace(scopedToolName))
            {
                var previousToolName = finalProposal.RecommendedToolName;
                finalProposal.RecommendedToolName = scopedToolName;
                finalProposal.RecommendedToolArgumentsJson = "{}";
                finalProposal.RemediationRationale =
                    $"RigMD changed the proposed action from '{previousToolName}' to '{scopedToolName}' because it better matches this diagnosis scope.";
            }
        }

        var recommendedTool = _toolRegistry.GetTool(finalProposal.RecommendedToolName ?? "clear_temp_files")
            ?? _toolRegistry.GetTool("clear_temp_files");

        ToolDryRunPreview? dryRunPreview = null;
        ProposedToolInvocation? proposedInvocation = null;
        RemediationActionDef? actionDef = null;

        if (recommendedTool != null)
        {
            var swPreview = Stopwatch.StartNew();
            using var previewArgs = ParseJsonSafe(finalProposal.RecommendedToolArgumentsJson);
            dryRunPreview = await recommendedTool.PreviewImpactAsync(previewArgs.RootElement, cancellationToken);
            swPreview.Stop();

            EmitStep(new ReActTraceStep
            {
                StepType = nameof(ReActStepType.DryRunPreview),
                Title = $"Dry-Run Impact Preview: {recommendedTool.DisplayName}",
                Content = dryRunPreview.WhatWillHappen,
                ToolName = recommendedTool.Name,
                ToolArgumentsJson = finalProposal.RecommendedToolArgumentsJson,
                ObservationJson = JsonSerializer.Serialize(dryRunPreview),
                DurationMs = swPreview.ElapsedMilliseconds
            });

            EmitStep(new ReActTraceStep
            {
                StepType = nameof(ReActStepType.AwaitingApproval),
                Title = "Awaiting User Approval",
                Content = $"{finalProposal.RemediationRationale} ({dryRunPreview.AffectedItemsCount} item(s) affected).",
                ToolName = recommendedTool.Name,
                ToolArgumentsJson = finalProposal.RecommendedToolArgumentsJson
            });

            proposedInvocation = new ProposedToolInvocation
            {
                ToolName = recommendedTool.Name,
                DisplayName = recommendedTool.DisplayName,
                SafetyTier = recommendedTool.SafetyTier.ToString(),
                ArgumentsJson = finalProposal.RecommendedToolArgumentsJson,
                RootCauseAnalysis = finalProposal.RootCauseAnalysis,
                RemediationRationale = finalProposal.RemediationRationale,
                EvidenceCitations = finalProposal.EvidenceCitations,
                DryRunPreview = dryRunPreview
            };

            actionDef = new RemediationActionDef
            {
                Id = recommendedTool.Name,
                Name = recommendedTool.DisplayName,
                Description = dryRunPreview.WhatWillHappen,
                SupportedDiagnosisCategories = new List<string> { diagnostic.DiagnosedCategory },
                RiskLevel = recommendedTool.SafetyTier == ToolSafetyTier.Tier2_DestructiveOrAdmin ? "Medium (Tier 2)" : "Low (Tier 1)",
                SafetyTier = recommendedTool.SafetyTier.ToString(),
                ToolArgumentsJson = finalProposal.RecommendedToolArgumentsJson,
                IsReversible = recommendedTool.SafetyTier == ToolSafetyTier.Tier1_SafeReversible,
                RequiresUserConfirmation = true
            };
        }

        var plan = new RemediationPlan
        {
            SessionId = diagnostic.DiagnosticSessionId.ToString(),
            PlannedActions = actionDef != null ? new List<RemediationActionDef> { actionDef } : new List<RemediationActionDef>(),
            StrategyReasoning = $"{finalProposal.RootCauseAnalysis} {finalProposal.RemediationRationale}".Trim()
        };

        var warnings = dryRunPreview?.Warnings.ToList() ?? new List<string>();
        if (warnings.Count == 0)
        {
            warnings.Add("Explicit user confirmation is required before executing system changes.");
        }

        return new OrchestrationResult
        {
            EngineMode = engineMode,
            RootCauseAnalysis = finalProposal.RootCauseAnalysis,
            Plan = plan,
            Safety = new SafetyEvaluation
            {
                IsApproved = dryRunPreview?.CanExecute ?? (actionDef != null),
                RequiresUserConfirmation = true,
                RejectionReason = (dryRunPreview != null && !dryRunPreview.CanExecute)
                    ? dryRunPreview.WhatWillHappen
                    : string.Empty,
                Warnings = warnings
            },
            Attempts = actionDef != null
                ? new List<RemediationAttempt>
                {
                    new()
                    {
                        Action = actionDef,
                        State = (dryRunPreview != null && !dryRunPreview.CanExecute)
                            ? RemediationAttemptState.SafetyRejected
                            : RemediationAttemptState.AwaitingConsent,
                        Notes = dryRunPreview?.WhatWillHappen ?? plan.StrategyReasoning
                    }
                }
                : new List<RemediationAttempt>(),
            ReasoningSteps = steps,
            ProposedTool = proposedInvocation,
            DryRunPreview = dryRunPreview,
            Trace = traceBuilder.ToString().Trim()
        };
    }

    public async Task<OrchestrationResult> RunExecutionCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        bool userConsentProvided = false,
        Action<string>? progressReporter = null,
        string? requestedToolName = null,
        string? requestedArgumentsJson = null,
        Action<ReActTraceStep>? stepReporter = null,
        CancellationToken cancellationToken = default)
    {
        var toolName = !string.IsNullOrWhiteSpace(requestedToolName)
            ? requestedToolName!
            : ResolveDefaultToolName(diagnostic.DiagnosedCategory);

        var argsJson = !string.IsNullOrWhiteSpace(requestedArgumentsJson)
            ? requestedArgumentsJson!
            : "{}";

        var (diagnosisMode, componentIds, scenarioId) = ExtractSessionScope(diagnostic);
        var scopedRemediationTools =
            DiagnosticScopeMapper.GetAllowedRemediationTools(
                diagnosisMode,
                componentIds,
                scenarioId,
                diagnostic.DiagnosedCategory);
        if (scopedRemediationTools.Count > 0 &&
            !scopedRemediationTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            var blockedActionDef = new RemediationActionDef
            {
                Id = toolName,
                Name = toolName,
                Description = $"'{toolName}' does not match this diagnosis scope.",
                RiskLevel = "Blocked",
                SafetyTier = nameof(ToolSafetyTier.Tier2_DestructiveOrAdmin),
                ToolArgumentsJson = argsJson,
                IsReversible = false,
                RequiresUserConfirmation = true
            };

            return new OrchestrationResult
            {
                EngineMode = "ReAct-Scope-Safety-Gate",
                Plan = new RemediationPlan
                {
                    SessionId = diagnostic.DiagnosticSessionId.ToString(),
                    PlannedActions = new List<RemediationActionDef> { blockedActionDef },
                    StrategyReasoning = "The requested remediation was blocked because it does not match this diagnosis."
                },
                Safety = new SafetyEvaluation
                {
                    IsApproved = false,
                    RequiresUserConfirmation = true,
                    RejectionReason = $"'{toolName}' is not a recommended action for '{diagnostic.DiagnosedCategory}'. Recommended action(s): {string.Join(", ", scopedRemediationTools)}."
                },
                Attempts = new List<RemediationAttempt>
                {
                    new()
                    {
                        Action = blockedActionDef,
                        State = RemediationAttemptState.SafetyRejected,
                        Notes = "RigMD blocked this action because it did not match the selected scenario or component."
                    }
                },
                Trace = $"[SAFETY] Blocked out-of-scope remediation tool '{toolName}'."
            };
        }

        var tool = _toolRegistry?.GetTool(toolName);
        var actionDef = new RemediationActionDef
        {
            Id = tool?.Name ?? toolName,
            Name = tool?.DisplayName ?? toolName,
            Description = tool?.Description ?? $"Execute '{toolName}'.",
            RiskLevel = tool?.SafetyTier == ToolSafetyTier.Tier2_DestructiveOrAdmin ? "Medium (Tier 2)" : "Low (Tier 1)",
            SafetyTier = tool?.SafetyTier.ToString() ?? nameof(ToolSafetyTier.Tier1_SafeReversible),
            ToolArgumentsJson = argsJson,
            IsReversible = tool?.SafetyTier == ToolSafetyTier.Tier1_SafeReversible,
            RequiresUserConfirmation = true
        };

        var plan = new RemediationPlan
        {
            SessionId = diagnostic.DiagnosticSessionId.ToString(),
            PlannedActions = new List<RemediationActionDef> { actionDef },
            StrategyReasoning = $"Executing user-approved remediation tool '{actionDef.Name}'."
        };

        if (!userConsentProvided)
        {
            return new OrchestrationResult
            {
                Plan = plan,
                Safety = new SafetyEvaluation
                {
                    IsApproved = false,
                    RequiresUserConfirmation = true,
                    RejectionReason = "Explicit user consent is required before executing OS changes."
                },
                Attempts = new List<RemediationAttempt>
                {
                    new()
                    {
                        Action = actionDef,
                        State = RemediationAttemptState.AwaitingConsent,
                        Notes = "Execution blocked until user consent is provided."
                    }
                },
                Trace = "[SAFETY] Execution paused awaiting explicit user consent."
            };
        }

        var steps = new List<ReActTraceStep>();
        var traceBuilder = new StringBuilder();
        int stepCounter = 1;

        void EmitStep(ReActTraceStep step)
        {
            step.StepIndex = stepCounter++;
            steps.Add(step);
            var line = $"[{step.StepType.ToUpperInvariant()}] {step.Title}: {step.Content}";
            traceBuilder.AppendLine(line);
            progressReporter?.Invoke(line);
            stepReporter?.Invoke(step);
        }

        // 1. Pre-execution verification snapshot via paired Tier 0 diagnostic tool
        var verificationToolName = GetPairedVerificationToolName(actionDef.Id, diagnostic);
        var verificationTool = _toolRegistry?.GetTool(verificationToolName);
        AgentToolExecutionResult? beforeSnapshot = null;

        if (verificationTool != null)
        {
            using var emptyDoc = ParseJsonSafe("{}");
            var swBefore = Stopwatch.StartNew();
            beforeSnapshot = await verificationTool.ExecuteAsync(emptyDoc.RootElement, null, cancellationToken);
            swBefore.Stop();

            EmitStep(new ReActTraceStep
            {
                StepType = nameof(ReActStepType.Observation),
                Title = $"Pre-Execution Baseline ({verificationTool.DisplayName})",
                Content = beforeSnapshot.Summary,
                ToolName = verificationTool.Name,
                ObservationJson = beforeSnapshot.DataJson,
                DurationMs = swBefore.ElapsedMilliseconds
            });
        }

        // 2. Execute the approved remediation tool
        EmitStep(new ReActTraceStep
        {
            StepType = nameof(ReActStepType.Execution),
            Title = $"Executing {actionDef.Name}",
            Content = $"Running '{actionDef.Id}' with arguments {argsJson}...",
            ToolName = actionDef.Id,
            ToolArgumentsJson = argsJson
        });

        ExecutionResult execution;
        var swExec = Stopwatch.StartNew();
        if (tool != null)
        {
            using var argsDoc = ParseJsonSafe(argsJson);
            var toolResult = await tool.ExecuteAsync(argsDoc.RootElement, progressReporter, cancellationToken);
            execution = new ExecutionResult
            {
                Success = toolResult.Success,
                Summary = toolResult.Summary,
                OutputLog = toolResult.OutputLog,
                Proof = toolResult.Proof
            };
        }
        else
        {
            execution = await _realExecutor.ExecuteAsync(actionDef, progressReporter);
        }
        swExec.Stop();

        // 3. Post-execution telemetry verification via paired Tier 0 diagnostic tool
        AgentToolExecutionResult? afterSnapshot = null;
        if (verificationTool != null)
        {
            using var emptyDoc = ParseJsonSafe("{}");
            var swAfter = Stopwatch.StartNew();
            afterSnapshot = await verificationTool.ExecuteAsync(emptyDoc.RootElement, null, cancellationToken);
            swAfter.Stop();

            EmitStep(new ReActTraceStep
            {
                StepType = nameof(ReActStepType.Verification),
                Title = $"Post-Execution Verification ({verificationTool.DisplayName})",
                Content = afterSnapshot.Summary,
                ToolName = verificationTool.Name,
                ObservationJson = afterSnapshot.DataJson,
                DurationMs = swAfter.ElapsedMilliseconds
            });
        }

        var verificationStatus = DetermineVerificationStatus(
            diagnostic,
            execution,
            afterSnapshot,
            verificationTool?.Name);

        var verificationReport = new PostExecutionVerificationReport
        {
            VerificationToolName = verificationTool?.Name ?? "execution_proof",
            Status = verificationStatus,
            Summary = afterSnapshot != null
                ? $"Before: {beforeSnapshot?.Summary} -> After: {afterSnapshot.Summary}"
                : execution.Summary,
            BeforeSnapshotJson = beforeSnapshot?.DataJson ?? "{}",
            AfterSnapshotJson = afterSnapshot?.DataJson ?? "{}",
            MetricDeltas = execution.Proof
        };

        var attemptState = execution.Success
            ? RemediationAttemptState.Completed
            : RemediationAttemptState.ExecutionFailed;

        return new OrchestrationResult
        {
            EngineMode = "ReAct-Verified-Executor",
            RootCauseAnalysis = plan.StrategyReasoning,
            Plan = plan,
            Safety = new SafetyEvaluation
            {
                IsApproved = true,
                RequiresUserConfirmation = true
            },
            Execution = execution,
            Verification = verificationStatus,
            VerificationReport = verificationReport,
            ReasoningSteps = steps,
            Attempts = new List<RemediationAttempt>
            {
                new()
                {
                    Action = actionDef,
                    State = attemptState,
                    Execution = execution,
                    Verification = verificationStatus,
                    Notes = execution.Summary
                }
            },
            Trace = traceBuilder.ToString().Trim()
        };
    }

    private static (string DiagnosisMode, List<string> ComponentIds, string? ScenarioId) ExtractSessionScope(
        DiagnosticOutput diagnostic)
    {
        var answers = diagnostic.Session?.Answers;
        if (answers == null || answers.Count == 0)
        {
            return ("full", new List<string>(), null);
        }

        string GetAnswer(string key) =>
            answers.FirstOrDefault(a => string.Equals(a.QuestionKey, key, StringComparison.OrdinalIgnoreCase))?.AnswerValue ?? string.Empty;

        var rawMode = GetAnswer("diagnosis_mode").Trim().ToLowerInvariant();
        var mode = rawMode is "component" or "scenario" or "full" ? rawMode : "full";

        var rawScenario = GetAnswer("scenario_id").Trim();
        var scenarioId = string.IsNullOrWhiteSpace(rawScenario)
            ? null
            : DiagnosticScopeMapper.NormalizeScenarioId(rawScenario);

        var rawComponents = GetAnswer("component_ids").Trim();
        var componentIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(rawComponents))
        {
            if (rawComponents.StartsWith("["))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(rawComponents);
                    if (parsed != null)
                    {
                        componentIds.AddRange(parsed
                            .Select(DiagnosticScopeMapper.NormalizeComponentId)
                            .Where(c => !string.IsNullOrWhiteSpace(c)));
                    }
                }
                catch
                {
                    // Ignore malformed JSON array
                }
            }
            else
            {
                componentIds.AddRange(rawComponents
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(DiagnosticScopeMapper.NormalizeComponentId)
                    .Where(c => !string.IsNullOrWhiteSpace(c)));
            }
        }

        return (mode, componentIds, scenarioId);
    }

    private static string GetPairedVerificationToolName(string remediationToolName, DiagnosticOutput diagnostic)
    {
        var diagnosisVerificationTool = GetDiagnosisVerificationToolName(diagnostic);
        if (!string.IsNullOrWhiteSpace(diagnosisVerificationTool))
        {
            return diagnosisVerificationTool;
        }

        return remediationToolName.ToLowerInvariant() switch
        {
            "rescan_plug_and_play_devices" => "inspect_full_device_profile",
            "terminate_processes" or "restart_windows_explorer" => "inspect_memory_and_processes",
            "flush_dns" or "flush_dns_cache" => "inspect_network_connectivity",
            "clear_temp_files" or "clear_user_temp_files" or "clear_browser_cache" or "clear_windows_update_cache" or "run_disk_cleanup" => "inspect_storage_health",
            _ => "inspect_cpu_and_thermals"
        };
    }

    private static string? GetDiagnosisVerificationToolName(DiagnosticOutput diagnostic)
    {
        var (diagnosisMode, componentIds, scenarioId) = ExtractSessionScope(diagnostic);
        var category = (diagnostic.DiagnosedCategory ?? string.Empty).ToLowerInvariant();
        var scenario = DiagnosticScopeMapper.NormalizeScenarioId(scenarioId);
        var components = componentIds
            .Select(DiagnosticScopeMapper.NormalizeComponentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (category.Contains("driver") ||
            category.Contains("pnp") ||
            category.Contains("usb") ||
            category.Contains("device manager") ||
            scenario is "driver-error" or "no-display" ||
            components.Overlaps(new[] { "drivers", "peripherals", "audio", "display", "gpu" }))
        {
            return "inspect_full_device_profile";
        }

        if (category.Contains("network") || category.Contains("dns") || category.Contains("internet") || scenario == "network-problem" || components.Contains("network"))
        {
            return "inspect_network_connectivity";
        }

        if (category.Contains("storage") || category.Contains("disk") || category.Contains("cache") || scenario == "storage-problem" || components.Contains("storage"))
        {
            return "inspect_storage_health";
        }

        if (category.Contains("memory") || category.Contains("ram") || category.Contains("performance") || category.Contains("slow") || category.Contains("stuttering") || category.Contains("freezing") || scenario is "slow-system" or "stuttering-freezing" || components.Contains("memory"))
        {
            return "inspect_memory_and_processes";
        }

        if (category.Contains("thermal") || category.Contains("overheat") || category.Contains("fan") || category.Contains("cpu") || scenario == "overheating-loud-fan" || components.Contains("cpu") || components.Contains("thermal"))
        {
            return "inspect_cpu_and_thermals";
        }

        if (category.Contains("battery") || category.Contains("power") || scenario == "rapid-battery-drain" || components.Contains("battery"))
        {
            return "inspect_battery_and_power";
        }

        if (category.Contains("application crash") || category.Contains("app crash") || category.Contains("blue screen") || category.Contains("stop error") || scenario is "app-crashes" or "blue-screen-crash")
        {
            return "query_windows_event_logs";
        }

        return diagnosisMode == "full" ? null : "inspect_full_device_profile";
    }

    private static VerificationStatus DetermineVerificationStatus(
        DiagnosticOutput diagnostic,
        ExecutionResult execution,
        AgentToolExecutionResult? afterSnapshot,
        string? verificationToolName)
    {
        if (!execution.Success)
        {
            return VerificationStatus.Unresolved;
        }

        var category = (diagnostic.DiagnosedCategory ?? string.Empty).ToLowerInvariant();
        var toolName = verificationToolName ?? string.Empty;

        if (IsDriverIssue(category) && ToolIs(toolName, "inspect_full_device_profile"))
        {
            var errorCount = TryReadInt(afterSnapshot?.DataJson, "deviceErrorsCount");
            return errorCount.HasValue && errorCount.Value == 0
                ? VerificationStatus.Resolved
                : VerificationStatus.Unresolved;
        }

        if (IsMemoryIssue(category) && ToolIs(toolName, "inspect_memory_and_processes"))
        {
            return EvaluateMemoryEvidence(afterSnapshot?.DataJson);
        }

        if (IsStorageIssue(category) && ToolIs(toolName, "inspect_storage_health"))
        {
            return EvaluateStorageEvidence(afterSnapshot?.DataJson);
        }

        if (IsNetworkIssue(category) && ToolIs(toolName, "inspect_network_connectivity"))
        {
            return EvaluateNetworkEvidence(afterSnapshot?.DataJson);
        }

        if (IsThermalIssue(category) && ToolIs(toolName, "inspect_cpu_and_thermals"))
        {
            return EvaluateThermalEvidence(afterSnapshot?.DataJson);
        }

        if (IsBatteryIssue(category) && ToolIs(toolName, "inspect_battery_and_power"))
        {
            return EvaluateBatteryEvidence(afterSnapshot?.DataJson);
        }

        if (IsCrashIssue(category) && ToolIs(toolName, "query_windows_event_logs"))
        {
            return EvaluateEventLogEvidence(afterSnapshot?.DataJson);
        }

        return afterSnapshot?.Success == true ? VerificationStatus.Unknown : VerificationStatus.Unresolved;
    }

    private static bool ToolIs(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsDriverIssue(string category) =>
        category.Contains("driver") ||
        category.Contains("pnp") ||
        category.Contains("usb") ||
        category.Contains("device manager") ||
        category.Contains("display");

    private static bool IsMemoryIssue(string category) =>
        category.Contains("memory") ||
        category.Contains("ram") ||
        category.Contains("performance") ||
        category.Contains("slow") ||
        category.Contains("stuttering") ||
        category.Contains("freezing");

    private static bool IsStorageIssue(string category) =>
        category.Contains("storage") ||
        category.Contains("disk") ||
        category.Contains("cache") ||
        category.Contains("temporary");

    private static bool IsNetworkIssue(string category) =>
        category.Contains("network") ||
        category.Contains("dns") ||
        category.Contains("internet") ||
        category.Contains("connection");

    private static bool IsThermalIssue(string category) =>
        category.Contains("thermal") ||
        category.Contains("overheat") ||
        category.Contains("fan") ||
        category.Contains("cpu");

    private static bool IsBatteryIssue(string category) =>
        category.Contains("battery") ||
        category.Contains("power") ||
        category.Contains("charging");

    private static bool IsCrashIssue(string category) =>
        category.Contains("application crash") ||
        category.Contains("app crash") ||
        category.Contains("crash") ||
        category.Contains("blue screen") ||
        category.Contains("stop error");

    private static VerificationStatus EvaluateMemoryEvidence(string? json)
    {
        var ramUsage = TryReadDouble(json, "memory", "usagePercent");
        var browserMemoryMb = TryReadDouble(json, "browserSummary", "browserMemoryMb");
        var browserProcessCount = TryReadInt(json, "browserSummary", "browserProcessCount");
        var memoryLeakWarning = TryReadBool(json, "memoryLeakWarning") ?? false;

        if (!ramUsage.HasValue && !browserMemoryMb.HasValue && !browserProcessCount.HasValue)
        {
            return VerificationStatus.Unknown;
        }

        var ramOk = !ramUsage.HasValue || ramUsage.Value < ElevatedMemoryThreshold;
        var browserMemoryOk = !browserMemoryMb.HasValue || browserMemoryMb.Value < BrowserMemoryPressureMb;
        var browserCountOk = !browserProcessCount.HasValue || browserProcessCount.Value < BrowserProcessPressureCount;

        return ramOk && browserMemoryOk && browserCountOk && !memoryLeakWarning
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    private static VerificationStatus EvaluateStorageEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return VerificationStatus.Unknown;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var hasEvidence = false;
            var storageHealthy = true;

            if (doc.RootElement.TryGetProperty("drives", out var drives) && drives.ValueKind == JsonValueKind.Array)
            {
                foreach (var drive in drives.EnumerateArray())
                {
                    if (TryReadBool(drive, "isFailingSmart") == true)
                    {
                        storageHealthy = false;
                    }

                    hasEvidence = true;
                }
            }

            if (doc.RootElement.TryGetProperty("volumes", out var volumes) && volumes.ValueKind == JsonValueKind.Array)
            {
                foreach (var volume in volumes.EnumerateArray())
                {
                    var usagePercent = TryReadDouble(volume, "usagePercent");
                    if (usagePercent.HasValue)
                    {
                        storageHealthy = storageHealthy && usagePercent.Value < ElevatedStorageThreshold;
                        hasEvidence = true;
                    }
                }
            }

            return !hasEvidence
                ? VerificationStatus.Unknown
                : storageHealthy
                    ? VerificationStatus.Resolved
                    : VerificationStatus.Unresolved;
        }
        catch
        {
            return VerificationStatus.Unknown;
        }
    }

    private static VerificationStatus EvaluateNetworkEvidence(string? json)
    {
        var hasAdapter = TryReadBool(json, "hasActiveAdapter");
        var hasIpv4 = TryReadBool(json, "hasIpv4Address");
        var hasGateway = TryReadBool(json, "hasDefaultGateway");
        var hasDns = TryReadBool(json, "hasDnsServers");
        var dnsSucceeded = TryReadBool(json, "liveProbe", "dnsResolutionSucceeded");
        var packetLoss = TryReadDouble(json, "packetLossPercent");
        var pingLatency = TryReadDouble(json, "liveProbe", "pingLatencyMs");
        var pingStatus = TryReadString(json, "liveProbe", "pingStatus");

        if (!hasAdapter.HasValue && !hasIpv4.HasValue && !hasGateway.HasValue && !hasDns.HasValue)
        {
            return VerificationStatus.Unknown;
        }

        var pingOk = string.IsNullOrWhiteSpace(pingStatus) ||
            pingStatus.Equals("Success", StringComparison.OrdinalIgnoreCase);
        var latencyOk = !pingLatency.HasValue || pingLatency.Value <= NetworkLatencyThresholdMs;
        var lossOk = !packetLoss.HasValue || packetLoss.Value <= NetworkPacketLossThreshold;

        return hasAdapter == true &&
            hasIpv4 == true &&
            hasGateway == true &&
            hasDns == true &&
            dnsSucceeded != false &&
            pingOk &&
            latencyOk &&
            lossOk
                ? VerificationStatus.Resolved
                : VerificationStatus.Unresolved;
    }

    private static VerificationStatus EvaluateThermalEvidence(string? json)
    {
        var cpuUsage = TryReadDouble(json, "usagePercent");
        var tempCelsius = TryReadDouble(json, "temperatureCelsius");
        var throttling = TryReadBool(json, "isThermallyThrottling");

        if (!cpuUsage.HasValue && !tempCelsius.HasValue && !throttling.HasValue)
        {
            return VerificationStatus.Unknown;
        }

        var cpuOk = !cpuUsage.HasValue || cpuUsage.Value < ElevatedCpuThreshold;
        var tempOk = !tempCelsius.HasValue || tempCelsius.Value < ElevatedTempThresholdCelsius;

        return cpuOk && tempOk && throttling != true
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    private static VerificationStatus EvaluateBatteryEvidence(string? json)
    {
        var hasBattery = TryReadBool(json, "hasBattery");
        if (hasBattery == false)
        {
            return VerificationStatus.Resolved;
        }

        var chargePercent = TryReadDouble(json, "battery", "chargePercent");
        var healthStatus = TryReadString(json, "battery", "healthStatus") ?? string.Empty;
        var activePowerPlan = TryReadString(json, "activePowerPlan") ?? string.Empty;

        if (!hasBattery.HasValue && !chargePercent.HasValue && string.IsNullOrWhiteSpace(healthStatus) && string.IsNullOrWhiteSpace(activePowerPlan))
        {
            return VerificationStatus.Unknown;
        }

        var healthOk = !healthStatus.Contains("poor", StringComparison.OrdinalIgnoreCase) &&
            !healthStatus.Contains("bad", StringComparison.OrdinalIgnoreCase) &&
            !healthStatus.Contains("critical", StringComparison.OrdinalIgnoreCase) &&
            !healthStatus.Contains("replace", StringComparison.OrdinalIgnoreCase);
        var chargeOk = !chargePercent.HasValue || chargePercent.Value >= 20;

        return healthOk && chargeOk
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    private static VerificationStatus EvaluateEventLogEvidence(string? json)
    {
        var eventCount = TryReadInt(json, "eventCount");
        if (!eventCount.HasValue)
        {
            return VerificationStatus.Unknown;
        }

        return eventCount.Value == 0
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    private static int? TryReadInt(string? json, params string[] path)
    {
        var prop = TryReadElement(json, path);
        return prop.HasValue ? TryReadInt(prop.Value) : null;
    }

    private static double? TryReadDouble(string? json, params string[] path)
    {
        var prop = TryReadElement(json, path);
        return prop.HasValue ? TryReadDouble(prop.Value) : null;
    }

    private static bool? TryReadBool(string? json, params string[] path)
    {
        var prop = TryReadElement(json, path);
        return prop.HasValue ? TryReadBool(prop.Value) : null;
    }

    private static string? TryReadString(string? json, params string[] path)
    {
        var prop = TryReadElement(json, path);
        return prop.HasValue ? TryReadString(prop.Value) : null;
    }

    private static JsonElement? TryReadElement(string? json, params string[] path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }

            return current.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadInt(JsonElement prop) =>
        prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt32(out var value) => value,
            JsonValueKind.Number when prop.TryGetDouble(out var value) => (int)Math.Round(value),
            JsonValueKind.String when int.TryParse(prop.GetString(), out var value) => value,
            _ => null
        };

    private static double? TryReadDouble(JsonElement prop) =>
        prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(prop.GetString(), out var value) => value,
            _ => null
        };

    private static bool? TryReadBool(JsonElement prop) =>
        prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var value) => value,
            _ => null
        };

    private static string? TryReadString(JsonElement prop) =>
        prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();

    private static double? TryReadDouble(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out var prop)
            ? TryReadDouble(prop)
            : null;

    private static bool? TryReadBool(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out var prop)
            ? TryReadBool(prop)
            : null;

    private static string ResolveDefaultToolName(string? category)
    {
        var normalized = (category ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("network") || normalized.Contains("dns") || normalized.Contains("internet"))
        {
            return "flush_dns_cache";
        }

        if (normalized.Contains("browser"))
        {
            return "clear_browser_cache";
        }

        if (normalized.Contains("application crash") || normalized.Contains("app crash") || normalized.Contains("crash"))
        {
            return "run_system_file_checker";
        }

        return "clear_temp_files";
    }

    private static JsonDocument ParseJsonSafe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static OrchestrationResult BuildFallbackDryRunResult(DiagnosticOutput diagnostic)
    {
        var toolId = ResolveDefaultToolName(diagnostic.DiagnosedCategory);
        var action = new RemediationActionDef
        {
            Id = toolId,
            Name = toolId == "flush_dns_cache" ? "Flush Windows DNS Resolver Cache" : "Clear Temporary Files",
            Description = $"Preview '{toolId}' for '{diagnostic.DiagnosedCategory}'.",
            RiskLevel = "Low",
            IsReversible = true,
            RequiresUserConfirmation = true
        };

        var plan = new RemediationPlan
        {
            SessionId = diagnostic.DiagnosticSessionId.ToString(),
            PlannedActions = new List<RemediationActionDef> { action },
            StrategyReasoning = action.Description
        };

        return new OrchestrationResult
        {
            Plan = plan,
            Safety = new SafetyEvaluation
            {
                IsApproved = true,
                RequiresUserConfirmation = true
            },
            Attempts = new List<RemediationAttempt>
            {
                new()
                {
                    Action = action,
                    State = RemediationAttemptState.AwaitingConsent,
                    Notes = action.Description
                }
            },
            Trace = $"[ORCHESTRATOR] Prepared '{action.Id}'."
        };
    }
}
