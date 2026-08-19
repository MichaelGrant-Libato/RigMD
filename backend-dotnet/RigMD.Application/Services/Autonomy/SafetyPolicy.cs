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
        var requiresConsent = false;

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

        // Require explicit user consent for irreversible actions
        if (plan.PlannedActions.Any(a => !a.IsReversible))
        {
            requiresConsent = true;
            warnings.Add("Plan contains irreversible action(s). Deleted files cannot be recovered. Explicit user consent is required.");
        }

        return new SafetyEvaluation
        {
            IsApproved = true,
            RequiresUserConfirmation = requiresConsent,
            RejectionReason = string.Empty,
            Warnings = warnings
        };
    }
}

