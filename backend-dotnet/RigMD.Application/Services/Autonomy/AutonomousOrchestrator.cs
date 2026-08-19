using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services.Autonomy;

public class AutonomousOrchestrator : IAutonomousOrchestrator
{
    private readonly IRemediationPlanner _planner;
    private readonly ISafetyPolicy _safetyPolicy;
    private readonly IDryRunRemediationExecutor _dryRunExecutor;
    private readonly IRemediationExecutor _realExecutor;
    private readonly IVerificationService _verificationService;
    private readonly IRollbackManager _rollbackManager;

    public AutonomousOrchestrator(
        IRemediationPlanner planner,
        ISafetyPolicy safetyPolicy,
        IDryRunRemediationExecutor dryRunExecutor,
        IRemediationExecutor realExecutor,
        IVerificationService verificationService,
        IRollbackManager rollbackManager)
    {
        _planner = planner;
        _safetyPolicy = safetyPolicy;
        _dryRunExecutor = dryRunExecutor;
        _realExecutor = realExecutor;
        _verificationService = verificationService;
        _rollbackManager = rollbackManager;
    }

    public Task<OrchestrationResult> RunDryRunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware)
    {
        return RunCycleAsync(
            diagnostic,
            hardware,
            _dryRunExecutor.ExecuteAsync,
            isDryRun: true,
            userConsentProvided: false);
    }

    public Task<OrchestrationResult> RunExecutionCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        bool userConsentProvided = false)
    {
        return RunCycleAsync(
            diagnostic,
            hardware,
            _realExecutor.ExecuteAsync,
            isDryRun: false,
            userConsentProvided: userConsentProvided);
    }

    private async Task<OrchestrationResult> RunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        Func<RemediationActionDef, Task<ExecutionResult>> executeAction,
        bool isDryRun,
        bool userConsentProvided)
    {
        var trace = new StringBuilder();
        var mode = isDryRun ? "Dry Run" : "Real Execution";

        trace.AppendLine(
            $"[ORCHESTRATOR] Starting {mode} for Diagnosis: {diagnostic.DiagnosedCategory}");

        trace.AppendLine("[PLANNER] Formulating plan...");

        var plan = _planner.CreatePlan(diagnostic);

        trace.AppendLine(
            $"[PLANNER] Plan formulated: {plan.PlannedActions.Count} actions found.");

        trace.AppendLine(
            $"[PLANNER] Reasoning: {plan.StrategyReasoning}");

        if (plan.PlannedActions.Count == 0)
        {
            trace.AppendLine(
                "[ORCHESTRATOR] Cycle stopped. No actions to execute.");

            return new OrchestrationResult
            {
                Plan = plan,
                Trace = trace.ToString()
            };
        }

        var actionToExecute = plan.PlannedActions.First();

        var attempt = new RemediationAttempt
        {
            Action = actionToExecute,
            State = RemediationAttemptState.Planned,
            Notes = "Action selected by remediation planner."
        };

        trace.AppendLine("[SAFETY] Evaluating plan...");

        var safety = _safetyPolicy.Evaluate(plan, hardware);

        if (safety.RequiresUserConfirmation && !userConsentProvided)
        {
            safety.IsApproved = false;
            safety.RejectionReason =
                "This action requires explicit user consent.";

            attempt.State = RemediationAttemptState.AwaitingConsent;
            attempt.Notes = safety.RejectionReason;

            trace.AppendLine(
                "[SAFETY] Execution paused. Explicit user consent is required.");

            return new OrchestrationResult
            {
                Plan = plan,
                Safety = safety,
                Attempt = attempt,
                Trace = trace.ToString()
            };
        }

        if (!safety.IsApproved)
        {
            attempt.State = RemediationAttemptState.SafetyRejected;
            attempt.Notes = safety.RejectionReason;

            trace.AppendLine(
                $"[SAFETY] Plan REJECTED. Reason: {safety.RejectionReason}");

            return new OrchestrationResult
            {
                Plan = plan,
                Safety = safety,
                Attempt = attempt,
                Trace = trace.ToString()
            };
        }

        trace.AppendLine("[SAFETY] Plan APPROVED.");

        attempt.State = RemediationAttemptState.Executing;

        trace.AppendLine(
            $"[EXECUTOR] Attempting {(isDryRun ? "simulation" : "execution")} of {actionToExecute.Name}...");

        var executionResult = await executeAction(actionToExecute);

        attempt.Execution = executionResult;

        trace.AppendLine(
            $"[EXECUTOR] Result: {(executionResult.Success ? "SUCCESS" : "FAILED")}");

        trace.AppendLine(
            $"[EXECUTOR] Output: {executionResult.Summary}");

        if (!executionResult.Success)
        {
            attempt.State = RemediationAttemptState.ExecutionFailed;
            attempt.Notes = executionResult.Summary;

            trace.AppendLine(
                "[ORCHESTRATOR] Execution failed. Verification skipped.");

            return new OrchestrationResult
            {
                Plan = plan,
                Safety = safety,
                Execution = executionResult,
                Attempt = attempt,
                Trace = trace.ToString()
            };
        }

        // Dry-run simulates execution only.
        // It must never verify real Windows state.
        if (isDryRun)
        {
            attempt.State = RemediationAttemptState.Completed;
            attempt.Notes =
                "Dry-run simulation completed successfully. No real system changes were made.";

            trace.AppendLine(
                "[ORCHESTRATOR] Dry-run completed. Real-state verification skipped.");

            return new OrchestrationResult
            {
                Plan = plan,
                Safety = safety,
                Execution = executionResult,
                Attempt = attempt,
                Trace = trace.ToString()
            };
        }

        attempt.State = RemediationAttemptState.VerificationPending;

        trace.AppendLine(
            "[VERIFICATION] Verifying remediation outcome...");

        var verificationStatus =
            await _verificationService.VerifyAsync(
                actionToExecute,
                executionResult,
                hardware);

        attempt.Verification = verificationStatus;

        trace.AppendLine(
            $"[VERIFICATION] Result: {verificationStatus}");

        switch (verificationStatus)
        {
            case VerificationStatus.Resolved:
                attempt.State = RemediationAttemptState.Resolved;
                attempt.Notes =
                    "Verification confirmed that the remediation resolved the targeted condition.";
                break;

            case VerificationStatus.Unresolved:
                attempt.State = RemediationAttemptState.Unresolved;
                attempt.Notes =
                    "Execution succeeded, but verification did not confirm resolution.";
                break;

            case VerificationStatus.Worse:
                attempt.State = RemediationAttemptState.Worse;
                attempt.Notes =
                    "Verification indicates that the targeted condition became worse.";
                break;

            case VerificationStatus.Unknown:
            default:
                attempt.State = RemediationAttemptState.VerificationUnknown;
                attempt.Notes =
                    "The remediation executed successfully, but the result could not be verified.";
                break;
        }

        var shouldAttemptRollback =
            verificationStatus == VerificationStatus.Unresolved ||
            verificationStatus == VerificationStatus.Worse;

        if (shouldAttemptRollback)
        {
            if (!actionToExecute.IsReversible)
            {
                trace.AppendLine(
                    "[ROLLBACK] Action is irreversible. Rollback skipped.");

                attempt.Notes +=
                    " Rollback was not attempted because the action is irreversible.";
            }
            else if (!_rollbackManager.CanRollback(actionToExecute))
            {
                trace.AppendLine(
                    "[ROLLBACK] No verified rollback handler is available.");

                attempt.Notes +=
                    " Rollback was not attempted because no verified rollback handler is available.";
            }
            else
            {
                attempt.State = RemediationAttemptState.RollbackPending;

                trace.AppendLine(
                    $"[ROLLBACK] Attempting rollback for {actionToExecute.Name}...");

                var rollbackResult =
                    await _rollbackManager.RollbackAsync(
                        actionToExecute,
                        executionResult);

                attempt.RollbackResult = rollbackResult;

                if (rollbackResult.Success)
                {
                    attempt.State = RemediationAttemptState.RolledBack;
                    attempt.Notes =
                        "Verification did not confirm a safe outcome. The remediation was rolled back successfully.";

                    trace.AppendLine(
                        "[ROLLBACK] Rollback completed successfully.");
                }
                else
                {
                    attempt.State = RemediationAttemptState.RollbackFailed;
                    attempt.Notes =
                        $"Rollback failed: {rollbackResult.Summary}";

                    trace.AppendLine(
                        $"[ROLLBACK] Rollback failed: {rollbackResult.Summary}");
                }
            }
        }

        trace.AppendLine(
            $"[ORCHESTRATOR] {mode} cycle complete.");

        return new OrchestrationResult
        {
            Plan = plan,
            Safety = safety,
            Execution = executionResult,
            Verification = verificationStatus,
            Attempt = attempt,
            Trace = trace.ToString()
        };
    }
}