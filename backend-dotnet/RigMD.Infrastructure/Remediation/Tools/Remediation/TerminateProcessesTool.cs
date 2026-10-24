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

public class TerminateProcessesTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    public TerminateProcessesTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "terminate_processes";

    public string DisplayName => "Try to close reviewed high-use apps";

    public string Description =>
        "Requests normal closure of the reviewed non-system user apps only. Apps may keep running in the background; force termination is not used. This does not guarantee memory relief or a crash repair. Protected Windows, security and RigMD processes are blocked.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["processNames"] = new()
                {
                    Type = "array",
                    ItemsType = "string",
                    Description = "List of process names to terminate (e.g., ['chrome', 'discord', 'steamwebhelper'])."
                },
                ["reason"] = new()
                {
                    Type = "string",
                    Description = "Evidence supporting review of these specific apps. Do not claim that closing an app will repair a crash or free an exact amount of system memory."
                }
            },
            Required = new List<string> { "processNames", "reason" }
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var processNames = ToolArgumentHelper.GetStringArray(arguments, "processNames");
        var reason = ToolArgumentHelper.GetString(arguments, "reason");

        var action = new TerminateProcessesAction(_loggerFactory.CreateLogger<TerminateProcessesAction>());
        var preview = action.Preview(processNames, reason);
        return Task.FromResult(preview);
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        var processNames = ToolArgumentHelper.GetStringArray(arguments, "processNames");
        var reason = ToolArgumentHelper.GetString(arguments, "reason");

        var action = new TerminateProcessesAction(_loggerFactory.CreateLogger<TerminateProcessesAction>());
        var execResult = await action.ExecuteAsync(processNames, reason, progressReporter);

        var payload = new
        {
            success = execResult.Success,
            summary = execResult.Summary,
            targetProcesses = processNames,
            reason,
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
