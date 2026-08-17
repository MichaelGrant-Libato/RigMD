using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using System.Collections.Generic;
using System.Linq;

namespace RigMD.Application.Services.Autonomy;

public class SafetyPolicy : ISafetyPolicy
{
    public SafetyEvaluation Evaluate(RemediationPlan plan, HardwareProfileDto hardwareProfile)
    {
        var warnings = new List<string>();

        if (plan.PlannedActions.Count == 0)
        {
            return new SafetyEvaluation
            {
                IsApproved = false,
                RequiresUserConfirmation = false,
                RejectionReason = "Plan contains no actions."
            };
        }

        // Reject all remediation on server operating systems
        var osVersion = (hardwareProfile.OsVersion ?? "").ToLowerInvariant();
        if (osVersion.Contains("server"))
        {
            return new SafetyEvaluation
            {
                IsApproved = false,
                RequiresUserConfirmation = false,
                RejectionReason = "Autonomous remediation is not permitted on Windows Server operating systems."
            };
        }

        // Reject high-risk actions outright
        if (plan.PlannedActions.Any(a => a.RiskLevel == "High"))
        {
            return new SafetyEvaluation
            {
                IsApproved = false,
                RequiresUserConfirmation = true,
                RejectionReason = "High risk actions require explicit user consent."
            };
        }

        // Warn about irreversible actions but still approve
        if (plan.PlannedActions.Any(a => !a.IsReversible))
        {
            warnings.Add("Plan contains irreversible action(s). Deleted files cannot be recovered.");
        }

        return new SafetyEvaluation
        {
            IsApproved = true,
            RequiresUserConfirmation = false,
            RejectionReason = string.Empty,
            Warnings = warnings
        };
    }
}

