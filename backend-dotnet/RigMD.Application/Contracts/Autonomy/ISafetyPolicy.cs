using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Autonomy;

public interface ISafetyPolicy
{
    SafetyEvaluation Evaluate(RemediationPlan plan, HardwareProfileDto systemState);
}
