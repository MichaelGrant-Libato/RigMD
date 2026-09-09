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

            "flush_dns" =>
                await ExecuteFlushDns(),

            "clear_browser_cache" =>
                await ExecuteClearBrowserCache(),

            "clear_windows_update_cache" =>
                await ExecuteClearWindowsUpdateCache(),

            "run_disk_cleanup" =>
                await ExecuteRunDiskCleanup(),

            "run_sfc_scan" =>
                await ExecuteRunSfcScan(),

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

    private async Task<ExecutionResult>
        ExecuteFlushDns()
    {
        var actionLogger =
            _loggerFactory
                .CreateLogger<FlushDnsAction>();

        var action =
            new FlushDnsAction(actionLogger);

        return await action.ExecuteAsync();
    }

    private async Task<ExecutionResult>
        ExecuteClearBrowserCache()
    {
        var actionLogger =
            _loggerFactory
                .CreateLogger<
                    ClearBrowserCacheAction>();

        var action =
            new ClearBrowserCacheAction(
                actionLogger);

        return await action.ExecuteAsync();
    }

    private async Task<ExecutionResult>
        ExecuteClearWindowsUpdateCache()
    {
        var actionLogger =
            _loggerFactory
                .CreateLogger<
                    ClearWindowsUpdateCacheAction>();

        var action =
            new ClearWindowsUpdateCacheAction(
                actionLogger);

        return await action.ExecuteAsync();
    }

    private async Task<ExecutionResult>
        ExecuteRunDiskCleanup()
    {
        var actionLogger =
            _loggerFactory
                .CreateLogger<
                    RunDiskCleanupAction>();

        var action =
            new RunDiskCleanupAction(
                actionLogger);

        return await action.ExecuteAsync();
    }

    private async Task<ExecutionResult>
        ExecuteRunSfcScan()
    {
        var actionLogger =
            _loggerFactory
                .CreateLogger<RunSfcScanAction>();

        var action =
            new RunSfcScanAction(actionLogger);

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