using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Tests.Application;

public class SafetyPolicyTests
{
    [Fact]
    public void Evaluate_WhenPlanIsEmpty_RejectsPlan()
    {
        var policy = new SafetyPolicy();

        var plan = new RemediationPlan();

        var hardware = new HardwareProfileDto
        {
            OsVersion = "Windows 11"
        };

        var result = policy.Evaluate(plan, hardware);

        Assert.False(result.IsApproved);
        Assert.Contains("no actions", result.RejectionReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenRunningOnWindowsServer_RejectsPlan()
    {
        var policy = new SafetyPolicy();

        var plan = CreateLowRiskPlan();

        var hardware = new HardwareProfileDto
        {
            OsVersion = "Windows Server 2025"
        };

        var result = policy.Evaluate(plan, hardware);

        Assert.False(result.IsApproved);

        Assert.Contains(
            "Windows Server",
            result.RejectionReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenActionIsHighRisk_RejectsPlan()
    {
        var policy = new SafetyPolicy();

        var plan = new RemediationPlan
        {
            PlannedActions = new List<RemediationActionDef>
            {
                new()
                {
                    Id = "high_risk_test",
                    Name = "High Risk Test",
                    SupportedDiagnosisCategories = new List<string>
                    {
                        "Test"
                    },
                    RiskLevel = "High",
                    IsReversible = true
                }
            }
        };

        var hardware = new HardwareProfileDto
        {
            OsVersion = "Windows 11"
        };

        var result = policy.Evaluate(plan, hardware);

        Assert.False(result.IsApproved);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public void Evaluate_WhenLowRiskPlanIsValid_ApprovesPlan()
    {
        var policy = new SafetyPolicy();

        var result = policy.Evaluate(
            CreateLowRiskPlan(),
            new HardwareProfileDto
            {
                OsVersion = "Windows 11"
            });

        Assert.True(result.IsApproved);
    }

    [Fact]
    public void Evaluate_WhenActionIsIrreversible_ApprovesButRequiresConfirmationAndAddsWarning()
    {
        var policy = new SafetyPolicy();

        var plan = new RemediationPlan
        {
            PlannedActions = new List<RemediationActionDef>
            {
                new()
                {
                    Id = "irreversible_action",
                    Name = "Irreversible Action",
                    SupportedDiagnosisCategories = new List<string>
                    {
                        "Test"
                    },
                    RiskLevel = "Low",
                    IsReversible = false
                }
            }
        };

        var hardware = new HardwareProfileDto
        {
            OsVersion = "Windows 11"
        };

        var result = policy.Evaluate(plan, hardware);

        Assert.True(result.IsApproved);
        Assert.True(result.RequiresUserConfirmation);
        Assert.Contains(result.Warnings, w => w.Contains("irreversible"));
    }

    private static RemediationPlan CreateLowRiskPlan()
    {
        return new RemediationPlan
        {
            PlannedActions = new List<RemediationActionDef>
            {
                new()
                {
                    Id = "test_action",
                    Name = "Test Action",
                    SupportedDiagnosisCategories = new List<string>
                    {
                        "Test"
                    },
                    RiskLevel = "Low",
                    IsReversible = true
                }
            }
        };
    }
}
