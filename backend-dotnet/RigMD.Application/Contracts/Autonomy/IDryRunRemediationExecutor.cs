using RigMD.Application.Models;
using System.Threading.Tasks;

namespace RigMD.Application.Contracts.Autonomy;

public interface IDryRunRemediationExecutor
{
    Task<ExecutionResult> ExecuteAsync(RemediationActionDef action);
}