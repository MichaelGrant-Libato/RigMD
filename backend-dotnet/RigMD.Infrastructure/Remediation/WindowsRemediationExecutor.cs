using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation;

/// <summary>
/// Real remediation executor that dispatches to concrete Windows action implementations.
/// Only explicitly registered actions are supported — no arbitrary command execution.
/// </summary>
public class WindowsRemediationExecutor : IRemediationExecutor
{
    private readonly ILogger<WindowsRemediationExecutor> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public WindowsRemediationExecutor(
        ILogger<WindowsRemediationExecutor> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<ExecutionResult> ExecuteAsync(RemediationActionDef action)
    {
        _logger.LogInformation("WindowsRemediationExecutor: dispatching action '{ActionId}' ({ActionName})",
            action.Id, action.Name);

        return action.Id switch
        {
            "clear_user_temp_files" => await ExecuteClearTempFiles(),
            _ => new ExecutionResult
            {
                Success = false,
                Summary = $"Action '{action.Id}' is not yet implemented for real execution.",
                OutputLog = $"No handler registered for action ID: {action.Id}"
            }
        };
    }

    private async Task<ExecutionResult> ExecuteClearTempFiles()
    {
        var actionLogger = _loggerFactory.CreateLogger<ClearTempFilesAction>();
        var action = new ClearTempFilesAction(actionLogger);
        return await action.ExecuteAsync();
    }
}
