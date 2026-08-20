using System.Linq;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services.Autonomy;

public class PivotEngine : IPivotEngine
{
    public RemediationPlan Pivot(
        DiagnosticOutput diagnosticOutput,
        RemediationPlan failedPlan,
        RemediationActionDef failedAction,
        HardwareProfileDto currentSystemState)
    {
        var newActions = failedPlan.PlannedActions
            .Where(a => a.Id != failedAction.Id)
            .ToList();

        return new RemediationPlan
        {
            SessionId = failedPlan.SessionId,
            PlannedActions = newActions,
            StrategyReasoning = failedPlan.StrategyReasoning + $"\n[PIVOT] Action {failedAction.Name} failed or rolled back. Pivoting to next available action."
        };
    }
}
