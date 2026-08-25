using System;
using System.Collections.Generic;
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
    private readonly IPivotEngine _pivotEngine;

    public AutonomousOrchestrator(
        IRemediationPlanner planner,
        ISafetyPolicy safetyPolicy,
        IDryRunRemediationExecutor dryRunExecutor,
        IRemediationExecutor realExecutor,
        IVerificationService verificationService,
        IRollbackManager rollbackManager,
        IPivotEngine pivotEngine)
    {
        _planner = planner;
        _safetyPolicy = safetyPolicy;
        _dryRunExecutor = dryRunExecutor;
        _realExecutor = realExecutor;
        _verificationService = verificationService;
        _rollbackManager = rollbackManager;
        _pivotEngine = pivotEngine;
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

        trace.AppendLine(
            "[PLANNER] Formulating plan...");

        var plan = await _planner.CreatePlanAsync(diagnostic);

        trace.AppendLine(
            $"[PLANNER] Plan formulated: {plan.PlannedActions.Count} actions found.");

        trace.AppendLine(
            $"[PLANNER] Reasoning: {plan.StrategyReasoning}");

        var attempts = new List<RemediationAttempt>();

        var result = new OrchestrationResult
        {
            Plan = plan,
            Attempts = attempts,
            Trace = ""
        };

        if (plan.PlannedActions.Count == 0)
        {
            trace.AppendLine(
                "[ORCHESTRATOR] Cycle stopped. No actions to execute.");

            result.Trace = trace.ToString();

            return result;
        }

        while (plan.PlannedActions.Count > 0)
        {
            var actionToExecute =
                plan.PlannedActions.First();

            var attempt = new RemediationAttempt
            {
                Action = actionToExecute,
                State = RemediationAttemptState.Planned,
                Notes = "Action selected by remediation planner."
            };

            attempts.Add(attempt);

            trace.AppendLine();

            trace.AppendLine(
                $"[SAFETY] Evaluating plan for action {actionToExecute.Name}...");

            var safety =
                _safetyPolicy.Evaluate(
                    plan,
                    hardware);

            result.Safety = safety;

            if (!isDryRun &&
                safety.RequiresUserConfirmation &&
                !userConsentProvided)
            {
                safety.IsApproved = false;

                safety.RejectionReason =
                    "This action requires explicit user consent.";

                attempt.State =
                    RemediationAttemptState.AwaitingConsent;

                attempt.Notes =
                    safety.RejectionReason;

                trace.AppendLine(
                    "[SAFETY] Execution paused. Explicit user consent is required.");

                break;
            }

            if (isDryRun &&
                safety.IsApproved &&
                safety.RequiresUserConfirmation)
            {
                trace.AppendLine(
                    "[SAFETY] Dry-run preview allowed. Real execution will require explicit user consent.");
            }

            if (!safety.IsApproved)
            {
                attempt.State =
                    RemediationAttemptState.SafetyRejected;

                attempt.Notes =
                    safety.RejectionReason;

                trace.AppendLine(
                    $"[SAFETY] Plan REJECTED. Reason: {safety.RejectionReason}");

                break;
            }

            trace.AppendLine(
                "[SAFETY] Plan APPROVED.");

            attempt.State =
                RemediationAttemptState.Executing;

            trace.AppendLine(
                $"[EXECUTOR] Attempting {(isDryRun ? "simulation" : "execution")} of {actionToExecute.Name}...");

            var executionResult =
                await executeAction(actionToExecute);

            attempt.Execution =
                executionResult;

            result.Execution =
                executionResult;

            trace.AppendLine(
                $"[EXECUTOR] Result: {(executionResult.Success ? "SUCCESS" : "FAILED")}");

            trace.AppendLine(
                $"[EXECUTOR] Output: {executionResult.Summary}");

            if (!executionResult.Success)
            {
                attempt.State =
                    RemediationAttemptState.ExecutionFailed;

                attempt.Notes =
                    executionResult.Summary;

                trace.AppendLine(
                    "[ORCHESTRATOR] Execution failed. Verification skipped.");

                plan =
                    _pivotEngine.Pivot(
                        diagnostic,
                        plan,
                        actionToExecute,
                        hardware);

                result.Plan = plan;

                attempt.Notes +=
                    " Pivoted to next action.";

                trace.AppendLine(
                    $"[PIVOT] Pivoted to next action. {plan.PlannedActions.Count} actions remaining.");

                continue;
            }

            if (isDryRun)
            {
                attempt.State =
                    RemediationAttemptState.Completed;

                attempt.Notes =
                    "Dry-run simulation completed successfully. No real system changes were made.";

                trace.AppendLine(
                    "[ORCHESTRATOR] Dry-run completed. Real-state verification skipped.");

                break;
            }

            attempt.State =
                RemediationAttemptState.VerificationPending;

            trace.AppendLine(
                "[VERIFICATION] Verifying remediation outcome...");

            var verificationStatus =
                await _verificationService.VerifyAsync(
                    actionToExecute,
                    executionResult,
                    hardware);

            attempt.Verification =
                verificationStatus;

            result.Verification =
                verificationStatus;

            trace.AppendLine(
                $"[VERIFICATION] Result: {verificationStatus}");

            switch (verificationStatus)
            {
                case VerificationStatus.Resolved:
                    attempt.State =
                        RemediationAttemptState.Resolved;

                    attempt.Notes =
                        "Verification confirmed that the remediation resolved the targeted condition.";

                    break;

                case VerificationStatus.Unresolved:
                    attempt.State =
                        RemediationAttemptState.Unresolved;

                    attempt.Notes =
                        "Execution succeeded, but verification did not confirm resolution.";

                    break;

                case VerificationStatus.Worse:
                    attempt.State =
                        RemediationAttemptState.Worse;

                    attempt.Notes =
                        "Verification indicates that the targeted condition became worse.";

                    break;

                case VerificationStatus.Unknown:
                default:
                    attempt.State =
                        RemediationAttemptState.VerificationUnknown;

                    attempt.Notes =
                        "The remediation executed successfully, but the result could not be verified.";

                    break;
            }

            if (verificationStatus ==
                VerificationStatus.Resolved)
            {
                trace.AppendLine(
                    "[ORCHESTRATOR] Issue resolved. Cycle complete.");

                break;
            }

            if (verificationStatus ==
                VerificationStatus.Unknown)
            {
                trace.AppendLine(
                    "[ORCHESTRATOR] Verification result is unknown. Further autonomous remediation is unsafe.");

                trace.AppendLine(
                    "[ORCHESTRATOR] Escalating for human intervention.");

                attempt.State =
                    RemediationAttemptState.VerificationUnknown;

                attempt.Notes +=
                    " Further autonomous remediation was stopped because the result could not be verified.";

                result.Escalated = true;

                break;
            }

            var shouldAttemptRollback =
                verificationStatus ==
                    VerificationStatus.Unresolved ||
                verificationStatus ==
                    VerificationStatus.Worse;

            if (shouldAttemptRollback)
            {
                if (!actionToExecute.IsReversible)
                {
                    trace.AppendLine(
                        "[ROLLBACK] Action is irreversible. Rollback is unavailable.");

                    trace.AppendLine(
                        "[ORCHESTRATOR] Further autonomous remediation stopped because the previous action cannot be safely reversed.");

                    attempt.Notes +=
                        " Rollback was not attempted because the action is irreversible. Further autonomous remediation was stopped.";

                    result.Escalated = true;

                    break;
                }

                if (!_rollbackManager.CanRollback(
                        actionToExecute))
                {
                    trace.AppendLine(
                        "[ROLLBACK] No verified rollback handler is available.");

                    trace.AppendLine(
                        "[ORCHESTRATOR] Further autonomous remediation stopped because a safe rollback path is unavailable.");

                    attempt.Notes +=
                        " Rollback was not attempted because no verified rollback handler is available. Further autonomous remediation was stopped.";

                    result.Escalated = true;

                    break;
                }

                attempt.State =
                    RemediationAttemptState.RollbackPending;

                trace.AppendLine(
                    $"[ROLLBACK] Attempting rollback for {actionToExecute.Name}...");

                var rollbackResult =
                    await _rollbackManager.RollbackAsync(
                        actionToExecute,
                        executionResult);

                attempt.RollbackResult =
                    rollbackResult;

                if (rollbackResult.Success)
                {
                    attempt.State =
                        RemediationAttemptState.RolledBack;

                    attempt.Notes =
                        "Verification did not confirm a safe outcome. The remediation was rolled back successfully.";

                    trace.AppendLine(
                        "[ROLLBACK] Rollback completed successfully.");
                }
                else
                {
                    attempt.State =
                        RemediationAttemptState.RollbackFailed;

                    attempt.Notes =
                        $"Rollback failed: {rollbackResult.Summary}";

                    trace.AppendLine(
                        $"[ROLLBACK] Rollback failed: {rollbackResult.Summary}");

                    trace.AppendLine(
                        "[ORCHESTRATOR] Further autonomous remediation stopped because rollback failed.");

                    trace.AppendLine(
                        "[ORCHESTRATOR] Escalating for human intervention.");

                    result.Escalated = true;

                    break;
                }
            }

            plan =
                _pivotEngine.Pivot(
                    diagnostic,
                    plan,
                    actionToExecute,
                    hardware);

            result.Plan = plan;

            attempt.Notes +=
                " Pivoted to next action.";

            trace.AppendLine(
                $"[PIVOT] Pivoted to next action. {plan.PlannedActions.Count} actions remaining.");
        }

        if (plan.PlannedActions.Count == 0 &&
            result.Verification !=
                VerificationStatus.Resolved &&
            !isDryRun)
        {
            trace.AppendLine(
                "[ORCHESTRATOR] All planned actions exhausted. Escalating for human intervention.");

            result.Escalated = true;
        }

        result.Trace =
            trace.ToString();

        return result;
    }
}