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

public class FlushDnsCacheTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    public FlushDnsCacheTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "flush_dns_cache";

    public string DisplayName => "Flush Windows DNS Resolver Cache";

    public string Description =>
        "Executes 'ipconfig /flushdns' to purge stale or poisoned DNS resolver records in Windows and force fresh hostname lookups.";

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
        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = true,
            AffectedItemsCount = 1,
            EstimatedBytesAffected = 0,
            AffectedTargets = new List<string> { "Windows DNS Client Resolver Cache (ipconfig /flushdns)" },
            WhatWillHappen =
                "Flushes cached DNS records in Windows. Active network connections are not dropped; subsequent domain requests will resolve fresh IP addresses from your configured DNS servers."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Running ipconfig /flushdns...");

        var action = new FlushDnsAction(_loggerFactory.CreateLogger<FlushDnsAction>());
        var execResult = await action.ExecuteAsync();

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
