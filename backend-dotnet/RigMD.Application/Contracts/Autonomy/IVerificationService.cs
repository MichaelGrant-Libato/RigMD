using System.Threading.Tasks;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

public interface IVerificationService
{
    Task<VerificationStatus> VerifyAsync(RemediationActionDef action, ExecutionResult executionResult, HardwareProfileDto currentSystemState);
}
