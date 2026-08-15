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
    private readonly IRemediationExecutor _executor;

    public AutonomousOrchestrator(
        IRemediationPlanner planner,
        ISafetyPolicy safetyPolicy,
        IRemediationExecutor executor)
    {
        _planner = planner;
        _safetyPolicy = safetyPolicy;
        _executor = executor;
    }

    public async Task<OrchestrationResult> RunDryRunCycleAsync(DiagnosticOutput diagnostic, HardwareProfileDto hardware)
    {
        var trace = new StringBuilder();
        trace.AppendLine($"[ORCHESTRATOR] Starting Dry Run for Diagnosis: {diagnostic.DiagnosedCategory}");

        // 1. Plan
        trace.AppendLine("[PLANNER] Formulating plan...");
        var plan = _planner.CreatePlan(diagnostic);
        trace.AppendLine($"[PLANNER] Plan formulated: {plan.PlannedActions.Count} actions found.");
        trace.AppendLine($"[PLANNER] Reasoning: {plan.StrategyReasoning}");

        if (plan.PlannedActions.Count == 0)
        {
            trace.AppendLine("[ORCHESTRATOR] Cycle stopped. No actions to execute.");
            return new OrchestrationResult { Plan = plan, Trace = trace.ToString() };
        }

        // 2. Safety Check
        trace.AppendLine("[SAFETY] Evaluating plan...");
        var safety = _safetyPolicy.Evaluate(plan, hardware);
        if (!safety.IsApproved)
        {
            trace.AppendLine($"[SAFETY] Plan REJECTED. Reason: {safety.RejectionReason}");
            return new OrchestrationResult { Plan = plan, Safety = safety, Trace = trace.ToString() };
        }
        trace.AppendLine("[SAFETY] Plan APPROVED.");

        // 3. Execute (Dry Run)
        var actionToExecute = plan.PlannedActions.First();
        trace.AppendLine($"[EXECUTOR] Attempting execution of {actionToExecute.Name}...");
        var executionResult = await _executor.ExecuteAsync(actionToExecute);
        
        trace.AppendLine($"[EXECUTOR] Result: {(executionResult.Success ? "SUCCESS" : "FAILED")}");
        trace.AppendLine($"[EXECUTOR] Output: {executionResult.Summary}");

        trace.AppendLine("[ORCHESTRATOR] Dry run cycle complete.");

        return new OrchestrationResult
        {
            Plan = plan,
            Safety = safety,
            Execution = executionResult,
            Trace = trace.ToString()
        };
    }
}
