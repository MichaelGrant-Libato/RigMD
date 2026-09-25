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

public class RunSystemFileCheckerTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    public RunSystemFileCheckerTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "run_system_file_checker";

    public string DisplayName => "Run Windows System File Checker (SFC)";

    public string Description =>
        "Runs 'sfc.exe /scannow' to verify the integrity of protected Windows system files and repair corrupted binaries from the Windows component store. Requires Administrator privileges.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;

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
        var isAdmin = ToolArgumentHelper.IsCurrentProcessElevated();
        var warnings = new List<string>
        {
            "SFC scan takes several minutes to verify all protected Windows system binaries."
        };

        if (!isAdmin)
        {
            warnings.Add("RigMD is not currently running as Administrator; 'sfc /scannow' requires an elevated Administrator session to execute.");
        }

        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = isAdmin,
            RequiresAdmin = true,
            IsRunningAsAdmin = isAdmin,
            RequiresUserConfirmation = true,
            AffectedItemsCount = 1,
            EstimatedBytesAffected = 0,
            AffectedTargets = new List<string> { "Windows System32 & Component Store (sfc.exe /scannow)" },
            Warnings = warnings,
            WhatWillHappen = isAdmin
                ? "Will run 'sfc.exe /scannow' to scan and repair corrupted Windows system files using cached copies from the Windows component store."
                : "Cannot run 'sfc.exe /scannow' until the RigMD backend is started with Administrator privileges."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentHelper.IsCurrentProcessElevated())
        {
            var msg = "System File Checker (sfc /scannow) requires Administrator privileges. Please run RigMD as Administrator.";
            return new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = false,
                Summary = msg,
                DataJson = JsonSerializer.Serialize(new { success = false, error = msg }, ToolArgumentHelper.JsonOptions),
                OutputLog = msg
            };
        }

        var action = new RunSfcScanAction(_loggerFactory.CreateLogger<RunSfcScanAction>());
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
