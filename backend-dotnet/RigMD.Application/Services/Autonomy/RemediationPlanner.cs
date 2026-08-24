using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services.Autonomy;

public class RemediationPlanner : IRemediationPlanner
{
    private readonly IRemediationRegistry _registry;
    private readonly IRemediationRepository _remediationRepo;

    public RemediationPlanner(IRemediationRegistry registry, IRemediationRepository remediationRepo)
    {
        _registry = registry;
        _remediationRepo = remediationRepo;
    }

    public async Task<RemediationPlan> CreatePlanAsync(DiagnosticOutput diagnostic)
    {
        var category = diagnostic.DiagnosedCategory;

        // Find all actions that match the diagnosed category
        var potentialActions = _registry.GetAllActions()
            .Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Deprioritize actions that have historically failed verification
        var failedCodes = await _remediationRepo.GetFailedActionCodesAsync();
        var failedSet = new HashSet<string>(failedCodes, StringComparer.OrdinalIgnoreCase);

        var deprioritizedCount = potentialActions.Count(a => failedSet.Contains(a.Id));

        // Sort: non-failed actions first, failed actions last
        var sortedActions = potentialActions
            .OrderBy(a => failedSet.Contains(a.Id) ? 1 : 0)
            .ToList();

        var reasoning = $"Based on the diagnosis '{category}', found {potentialActions.Count} candidate actions.";
        if (deprioritizedCount > 0)
        {
            reasoning += $" {deprioritizedCount} action(s) were deprioritized due to past failed verification.";
        }

        return new RemediationPlan
        {
            StrategyReasoning = reasoning,
            PlannedActions = sortedActions
        };
    }
}
