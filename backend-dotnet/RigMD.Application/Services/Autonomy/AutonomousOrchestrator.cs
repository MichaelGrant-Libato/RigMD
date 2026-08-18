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

    public AutonomousOrchestrator(
        IRemediationPlanner planner,
        ISafetyPolicy safetyPolicy,
        IDryRunRemediationExecutor dryRunExecutor,
        IRemediationExecutor realExecutor)
    {
        _planner = planner;
        _safetyPolicy = safetyPolicy;
        _dryRunExecutor = dryRunExecutor;
        _realExecutor = realExecutor;
    }

    public Task<OrchestrationResult> RunDryRunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware)
    {
        return RunCycleAsync(
            diagnostic,
            hardware,
            _dryRunExecutor.ExecuteAsync,
            isDryRun: true);
    }

    public Task<OrchestrationResult> RunExecutionCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware)
    {
        return RunCycleAsync(
            diagnostic,
            hardware,
            _realExecutor.ExecuteAsync,
            isDryRun: false);
    }

    private async Task<OrchestrationResult> RunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        Func<RemediationActionDef, Task<ExecutionResult>> executeAction,
        bool isDryRun)
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

        trace.AppendLine("[SAFETY] Evaluating plan...");

        var safety = _safetyPolicy.Evaluate(plan, hardware);

        if (!safety.IsApproved)
        {
            trace.AppendLine(
                $"[SAFETY] Plan REJECTED. Reason: {safety.RejectionReason}");

            return new OrchestrationResult
            {
                Plan = plan,
                Safety = safety,
                Trace = trace.ToString()
            };
        }

        trace.AppendLine("[SAFETY] Plan APPROVED.");

        var actionToExecute = plan.PlannedActions.First();

        trace.AppendLine(
            $"[EXECUTOR] Attempting {(isDryRun ? "simulation" : "execution")} of {actionToExecute.Name}...");

        var executionResult =
            await executeAction(actionToExecute);

        trace.AppendLine(
            $"[EXECUTOR] Result: {(executionResult.Success ? "SUCCESS" : "FAILED")}");

        trace.AppendLine(
            $"[EXECUTOR] Output: {executionResult.Summary}");

        trace.AppendLine(
            $"[ORCHESTRATOR] {mode} cycle complete.");

        return new OrchestrationResult
        {
            Plan = plan,
            Safety = safety,
            Execution = executionResult,
            Trace = trace.ToString()
        };
    }
}