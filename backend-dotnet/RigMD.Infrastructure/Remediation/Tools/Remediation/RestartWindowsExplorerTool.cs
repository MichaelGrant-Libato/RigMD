using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class RestartWindowsExplorerTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    public RestartWindowsExplorerTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "restart_windows_explorer";

    public string DisplayName => "Restart Windows Explorer Shell";

    public string Description =>
        "Restarts the Windows Explorer desktop shell (explorer.exe) to recover frozen taskbars, unresponsive File Explorer windows, or high Explorer memory usage.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier1_SafeReversible;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>(),
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var action = new RestartExplorerAction(_loggerFactory.CreateLogger<RestartExplorerAction>());
        return Task.FromResult(action.Preview());
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        var action = new RestartExplorerAction(_loggerFactory.CreateLogger<RestartExplorerAction>());
        var execResult = await action.ExecuteAsync(progressReporter);

        var payload = new
        {
            success = execResult.Success,
            summary = execResult.Summary,
            proof = execResult.Proof
        };

        return new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = execResult.Success,
            Summary = execResult.Summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = execResult.OutputLog,
            Proof = execResult.Proof
        };
    }
}
