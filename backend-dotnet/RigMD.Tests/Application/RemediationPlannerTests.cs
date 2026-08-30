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

    [Fact]
    public async Task CreatePlanAsync_WhenLowAvailableStorageSpace_SelectsClearTempFiles()
    {
        var registry = new RemediationRegistry();
        var repo = new FakeRemediationRepo(Array.Empty<string>());
        var planner = new RemediationPlanner(registry, repo);

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "Low Available Storage Space",
            ActionCategory = "Maintain",
            ConfidenceLabel = "High"
        };

        var plan = await planner.CreatePlanAsync(diagnostic);

        var action = Assert.Single(plan.PlannedActions);

        Assert.Equal(
            "clear_user_temp_files",
            action.Id);
    }

    [Fact]
    public async Task CreatePlanAsync_WhenElevatedStorageUtilization_SelectsClearTempFiles()
    {
        var registry = new RemediationRegistry();
        var repo = new FakeRemediationRepo(Array.Empty<string>());
        var planner = new RemediationPlanner(registry, repo);

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "Elevated Storage Utilization",
            ActionCategory = "Maintain",
            ConfidenceLabel = "Medium"
        };

        var plan = await planner.CreatePlanAsync(diagnostic);

        var action = Assert.Single(plan.PlannedActions);

        Assert.Equal(
            "clear_user_temp_files",
            action.Id);
    }

    [Fact]
    public async Task CreatePlanAsync_WhenHighMemoryPressure_DoesNotSelectStorageCleanup()
    {
        var registry = new RemediationRegistry();
        var repo = new FakeRemediationRepo(Array.Empty<string>());
        var planner = new RemediationPlanner(registry, repo);

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "High Memory Pressure",
            ActionCategory = "Troubleshoot",
            ConfidenceLabel = "High"
        };

        var plan = await planner.CreatePlanAsync(diagnostic);

        Assert.Empty(plan.PlannedActions);
    }

    [Fact]
    public async Task CreatePlanAsync_WhenElevatedCpuUtilization_DoesNotSelectStorageCleanup()
    {
        var registry = new RemediationRegistry();
        var repo = new FakeRemediationRepo(Array.Empty<string>());
        var planner = new RemediationPlanner(registry, repo);

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = "Elevated CPU Utilization",
            ActionCategory = "Troubleshoot",
            ConfidenceLabel = "Medium"
        };

        var plan = await planner.CreatePlanAsync(diagnostic);

        Assert.Empty(plan.PlannedActions);
    }

    private sealed class FakeRegistry : IRemediationRegistry
    {
        private readonly List<RemediationActionDef> _actions = new()
        {
            new RemediationActionDef
            {
                Id = "action_a",
                Name = "Action A",
                SupportedDiagnosisCategories = new List<string>
                {
                    "TestCategory"
                },
                RiskLevel = "Low"
            },
            new RemediationActionDef
            {
                Id = "action_b",
                Name = "Action B",
                SupportedDiagnosisCategories = new List<string>
                {
                    "TestCategory"
                },
                RiskLevel = "Low"
            }
        };

        public IEnumerable<RemediationActionDef> GetAllActions() => _actions;
        public RemediationActionDef? GetAction(string id) =>
            _actions.FirstOrDefault(a => a.Id == id);
        public IEnumerable<RemediationActionDef> GetActionsByCategory(string category) =>
            _actions.Where(
                a => a.SupportedDiagnosisCategories.Any(
                    supportedCategory =>
                        supportedCategory.Equals(
                            category,
                            StringComparison.OrdinalIgnoreCase)));
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

