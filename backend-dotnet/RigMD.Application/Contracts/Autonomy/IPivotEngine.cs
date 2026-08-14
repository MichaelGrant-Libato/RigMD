using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Autonomy;

public interface IPivotEngine
{
    RemediationPlan Pivot(DiagnosticOutput diagnosticOutput, RemediationPlan failedPlan, RemediationActionDef failedAction, HardwareProfileDto currentSystemState);
}
