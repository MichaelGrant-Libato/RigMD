using System.Threading.Tasks;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

public interface IRollbackManager
{
    Task<ExecutionResult> RollbackAsync(RemediationActionDef action);
}
