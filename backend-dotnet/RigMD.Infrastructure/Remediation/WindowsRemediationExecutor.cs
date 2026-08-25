using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation;

/// <summary>
/// Real Windows remediation executor.
///
/// Only explicitly registered remediation action IDs may be executed.
/// Arbitrary commands or user-provided executable instructions are
/// never dispatched by this executor.
/// </summary>
public class WindowsRemediationExecutor :
    IRemediationExecutor
{
    private readonly
        ILogger<WindowsRemediationExecutor>
        _logger;

    private readonly ILoggerFactory
        _loggerFactory;

    public WindowsRemediationExecutor(
        ILogger<WindowsRemediationExecutor>
            logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<ExecutionResult>
        ExecuteAsync(
            RemediationActionDef action)
    {
        _logger.LogInformation(
            "WindowsRemediationExecutor: dispatching action '{ActionId}' ({ActionName})",
            action.Id,
            action.Name);

        return action.Id switch
        {
            "clear_user_temp_files" =>
                await ExecuteClearTempFiles(),

            _ =>
                CreateUnsupportedActionResult(
                    action)
        };
    }

    private async Task<ExecutionResult>
        ExecuteClearTempFiles()
    {
        var actionLogger =
            _loggerFactory
                .CreateLogger<
                    ClearTempFilesAction>();

        var action =
            new ClearTempFilesAction(
                actionLogger);

        return await action.ExecuteAsync();
    }

    private static ExecutionResult
        CreateUnsupportedActionResult(
            RemediationActionDef action)
    {
        return new ExecutionResult
        {
            Success = false,

            Summary =
                $"Action '{action.Name}' is not yet implemented for real execution.",

            OutputLog =
                $"No real remediation handler is registered for action ID '{action.Id}'. No system changes were made."
        };
    }
}