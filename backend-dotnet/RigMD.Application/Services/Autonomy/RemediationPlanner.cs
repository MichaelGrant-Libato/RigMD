using System;
using System.Linq;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services.Autonomy;

public class RemediationPlanner : IRemediationPlanner
{
    private readonly IRemediationRegistry _registry;

    public RemediationPlanner(IRemediationRegistry registry)
    {
        _registry = registry;
    }

    public RemediationPlan CreatePlan(DiagnosticOutput diagnostic)
    {
        var category = diagnostic.DiagnosedCategory;
        
        // Find all actions that match the diagnosed category
        var potentialActions = _registry.GetAllActions()
            .Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new RemediationPlan
        {
            StrategyReasoning = $"Based on the diagnosis '{category}', found {potentialActions.Count} candidate actions.",
            PlannedActions = potentialActions
        };
    }
}
