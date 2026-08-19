using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation;

public class RollbackManager : IRollbackManager
{
    private readonly ILogger<RollbackManager> _logger;

    public RollbackManager(
        ILogger<RollbackManager> logger)
    {
        _logger = logger;
    }

    public bool CanRollback(
        RemediationActionDef action)
    {
        // No real remediation action currently has a verified,
        // state-restoring rollback implementation.
        return false;
    }

    public Task<ExecutionResult> RollbackAsync(
        RemediationActionDef action,
        ExecutionResult originalExecution)
    {
        _logger.LogWarning(
            "Rollback requested for unsupported action '{ActionId}'.",
            action.Id);

        return Task.FromResult(new ExecutionResult
        {
            Success = false,
            Summary =
                $"Rollback is not supported for action '{action.Id}'.",
            OutputLog =
                "No verified rollback handler is registered for this action."
        });
    }
}