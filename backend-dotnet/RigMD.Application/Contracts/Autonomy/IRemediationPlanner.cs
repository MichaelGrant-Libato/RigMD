using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Autonomy;

public interface IRemediationPlanner
{
    Task<RemediationPlan> CreatePlanAsync(DiagnosticOutput diagnosticOutput);
}
