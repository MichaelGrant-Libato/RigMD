using System.Threading.Tasks;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

public interface IRollbackManager
{
    bool CanRollback(RemediationActionDef action);

    Task<ExecutionResult> RollbackAsync(
        RemediationActionDef action,
        ExecutionResult originalExecution);
}