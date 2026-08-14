using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Autonomy;

public interface IRemediationPlanner
{
    RemediationPlan CreatePlan(DiagnosticOutput diagnosticOutput);
}
