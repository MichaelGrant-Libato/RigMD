using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services.Autonomy;

/// <summary>
/// Phase 1 Reset Shell for the Autonomous Orchestrator.
/// All hardcoded planners, static registries, fake safety policies, and stubbed rollback/verification
/// classes have been purged. Real OS execution primitives remain wired via <see cref="IRemediationExecutor"/>
/// and will be driven by the LLM ReAct tool-calling loop in Phase 2 &amp; Phase 3.
/// </summary>
public class AutonomousOrchestrator : IAutonomousOrchestrator
{
    private readonly IRemediationExecutor _realExecutor;

    public AutonomousOrchestrator(IRemediationExecutor realExecutor)
    {
        _realExecutor = realExecutor;
    }

    public Task<OrchestrationResult> RunDryRunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware)
    {
        var action = ResolveTransitionalAction(diagnostic.DiagnosedCategory);
        var plan = new RemediationPlan
        {
            PlannedActions = action != null
                ? new List<RemediationActionDef> { action }
                : new List<RemediationActionDef>(),
            StrategyReasoning = action != null
                ? $"[Phase 1 Reset] Ready to preview '{action.Name}' for '{diagnostic.DiagnosedCategory}'. LLM ReAct tool-calling planner will replace this in Phase 2/3."
                : $"[Phase 1 Reset] No static fallback action mapped for '{diagnostic.DiagnosedCategory}'."
        };

        return Task.FromResult(new OrchestrationResult
        {
            Plan = plan,
            Safety = new SafetyEvaluation
            {
                IsApproved = action != null,
                RequiresUserConfirmation = true,
                Warnings = new List<string>
                {
                    "Explicit user confirmation is required before executing system changes."
                }
            },
            Attempts = action != null
                ? new List<RemediationAttempt>
                {
                    new()
                    {
                        Action = action,
                        State = RemediationAttemptState.AwaitingConsent,
                        Notes = plan.StrategyReasoning
                    }
                }
                : new List<RemediationAttempt>(),
            Trace = $"[ORCHESTRATOR] Phase 1 clean shell initialized for '{diagnostic.DiagnosedCategory}'."
        });
    }

    public async Task<OrchestrationResult> RunExecutionCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        bool userConsentProvided = false,
        Action<string>? progressReporter = null)
    {
        var action = ResolveTransitionalAction(diagnostic.DiagnosedCategory);
        if (action == null)
        {
            return new OrchestrationResult
            {
                Plan = new RemediationPlan(),
                Trace = $"[ORCHESTRATOR] No executable action for '{diagnostic.DiagnosedCategory}'."
            };
        }

        var plan = new RemediationPlan
        {
            PlannedActions = new List<RemediationActionDef> { action },
            StrategyReasoning = $"Executing real OS primitive '{action.Id}'."
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
                    RejectionReason = "Explicit user consent is required."
                },
                Attempts = new List<RemediationAttempt>
                {
                    new()
                    {
                        Action = action,
                        State = RemediationAttemptState.AwaitingConsent,
                        Notes = "Explicit user consent is required."
                    }
                },
                Trace = "[SAFETY] Execution paused awaiting user consent."
            };
        }

        progressReporter?.Invoke($"[EXECUTOR] Running {action.Name}...");
        var execution = await _realExecutor.ExecuteAsync(action, progressReporter);

        var state = execution.Success
            ? RemediationAttemptState.Completed
            : RemediationAttemptState.ExecutionFailed;

        return new OrchestrationResult
        {
            Plan = plan,
            Safety = new SafetyEvaluation
            {
                IsApproved = true,
                RequiresUserConfirmation = true
            },
            Execution = execution,
            Verification = execution.Success
                ? VerificationStatus.Resolved
                : VerificationStatus.Unresolved,
            Attempts = new List<RemediationAttempt>
            {
                new()
                {
                    Action = action,
                    State = state,
                    Execution = execution,
                    Verification = execution.Success
                        ? VerificationStatus.Resolved
                        : VerificationStatus.Unresolved,
                    Notes = execution.Summary
                }
            },
            Trace = $"[EXECUTOR] Completed '{action.Id}' (Success={execution.Success})."
        };
    }

    private static RemediationActionDef? ResolveTransitionalAction(string? category)
    {
        var normalized = (category ?? string.Empty).ToLowerInvariant();

        if (normalized.Contains("storage") || normalized.Contains("disk") || normalized.Contains("temp"))
        {
            return new RemediationActionDef
            {
                Id = "clear_user_temp_files",
                Name = "Clear User Temp Files",
                Description = "Deletes unlocked temporary files from the active user's TEMP directory.",
                RiskLevel = "Low",
                IsReversible = false,
                RequiresUserConfirmation = true
            };
        }

        if (normalized.Contains("network") || normalized.Contains("dns") || normalized.Contains("internet"))
        {
            return new RemediationActionDef
            {
                Id = "flush_dns",
                Name = "Flush DNS Resolver Cache",
                Description = "Flushes the Windows DNS resolver cache via ipconfig /flushdns.",
                RiskLevel = "Low",
                IsReversible = false,
                RequiresUserConfirmation = true
            };
        }

        if (normalized.Contains("os") || normalized.Contains("system") || normalized.Contains("performance") || normalized.Contains("thrashing"))
        {
            return new RemediationActionDef
            {
                Id = "clear_user_temp_files",
                Name = "Clear User Temp Files",
                Description = "Deletes unlocked temporary files from the active user's TEMP directory.",
                RiskLevel = "Low",
                IsReversible = false,
                RequiresUserConfirmation = true
            };
        }

        return null;
    }
}