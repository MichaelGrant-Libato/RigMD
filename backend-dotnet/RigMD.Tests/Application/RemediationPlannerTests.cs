using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Application;

public class RemediationPlannerTests
{
    [Fact]
    public async Task CreatePlanAsync_WhenHistoryHasFailures_DeprioritizesFailedActions()
    {
        var registry = new FakeRegistry();
        var repo = new FakeRemediationRepo(new[] { "action_b" });
        var planner = new RemediationPlanner(registry, repo);

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "TestCategory",
            ActionCategory = "Test",
            ConfidenceLabel = "High"
        };

        var plan = await planner.CreatePlanAsync(diagnostic);

        // action_a should come first since action_b has a failed history
        Assert.Equal(2, plan.PlannedActions.Count);
        Assert.Equal("action_a", plan.PlannedActions[0].Id);
        Assert.Equal("action_b", plan.PlannedActions[1].Id);
        Assert.Contains("deprioritized", plan.StrategyReasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_WhenNoFailures_KeepsOriginalOrder()
    {
        var registry = new FakeRegistry();
        var repo = new FakeRemediationRepo(Array.Empty<string>());
        var planner = new RemediationPlanner(registry, repo);

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "TestCategory",
            ActionCategory = "Test",
            ConfidenceLabel = "High"
        };

        var plan = await planner.CreatePlanAsync(diagnostic);

        Assert.Equal(2, plan.PlannedActions.Count);
        Assert.DoesNotContain("deprioritized", plan.StrategyReasoning, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeRegistry : IRemediationRegistry
    {
        private readonly List<RemediationActionDef> _actions = new()
        {
            new RemediationActionDef
            {
                Id = "action_a",
                Name = "Action A",
                Category = "TestCategory",
                RiskLevel = "Low"
            },
            new RemediationActionDef
            {
                Id = "action_b",
                Name = "Action B",
                Category = "TestCategory",
                RiskLevel = "Low"
            }
        };

        public IEnumerable<RemediationActionDef> GetAllActions() => _actions;
        public RemediationActionDef? GetAction(string id) =>
            _actions.FirstOrDefault(a => a.Id == id);
        public IEnumerable<RemediationActionDef> GetActionsByCategory(string category) =>
            _actions.Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeRemediationRepo : IRemediationRepository
    {
        private readonly IReadOnlyList<string> _failedCodes;

        public FakeRemediationRepo(IEnumerable<string> failedCodes)
        {
            _failedCodes = failedCodes.ToList().AsReadOnly();
        }

        public Task<Guid> SaveRunAsync(RemediationRun run) =>
            Task.FromResult(run.Id);

        public Task<IReadOnlyList<RemediationRun>> GetRunsBySessionAsync(Guid diagnosticOutputId) =>
            Task.FromResult<IReadOnlyList<RemediationRun>>(Array.Empty<RemediationRun>());

        public Task<IReadOnlyList<string>> GetFailedActionCodesAsync() =>
            Task.FromResult(_failedCodes);
    }
}
