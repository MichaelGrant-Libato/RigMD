using System.Threading.Tasks;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Autonomy;

public interface IAutonomousOrchestrator
{
    Task<OrchestrationResult> RunDryRunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware);

    Task<OrchestrationResult> RunExecutionCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware);
}