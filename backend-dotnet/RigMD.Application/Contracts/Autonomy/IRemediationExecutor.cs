using System.Threading.Tasks;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

public interface IRemediationExecutor
{
    Task<ExecutionResult> ExecuteAsync(RemediationActionDef action);
}
