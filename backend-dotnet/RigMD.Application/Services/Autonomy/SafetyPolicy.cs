using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using System.Linq;

namespace RigMD.Application.Services.Autonomy;

public class SafetyPolicy : ISafetyPolicy
{
    public SafetyEvaluation Evaluate(RemediationPlan plan, HardwareProfileDto hardwareProfile)
    {
        if (plan.PlannedActions.Count == 0)
        {
            return new SafetyEvaluation
            {
                IsApproved = false,
                RequiresUserConfirmation = false,
                RejectionReason = "Plan contains no actions."
            };
        }

        // For Dry Run mode, we generally approve, but let's test rejection for high risk
        if (plan.PlannedActions.Any(a => a.RiskLevel == "High"))
        {
            return new SafetyEvaluation
            {
                IsApproved = false,
                RequiresUserConfirmation = true, // We could say high risk requires consent
                RejectionReason = "High risk actions require explicit user consent."
            };
        }

        return new SafetyEvaluation
        {
            IsApproved = true,
            RequiresUserConfirmation = false,
            RejectionReason = string.Empty
        };
    }
}
