using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class ClearWindowsUpdateCacheTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public ClearWindowsUpdateCacheTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "clear_windows_update_cache";

    public string DisplayName => "Clear Windows Update Download Cache";

    public string Description =>
        "Stops the Windows Update service (wuauserv), clears downloaded update packages in C:\\Windows\\SoftwareDistribution\\Download, and restarts wuauserv to reclaim disk space or fix stuck Windows Updates.";

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
        var downloadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SoftwareDistribution",
            "Download");

        int fileCount = 0;
        long totalBytes = 0;
        if (Directory.Exists(downloadPath))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(downloadPath, "*", SafeEnumOptions))
                {
                    try
                    {
                        totalBytes += new FileInfo(file).Length;
                        fileCount++;
                    }
                    catch { }
                }
            }
            catch { }
        }

        var warnings = new List<string>();
        if (!isAdmin)
        {
            warnings.Add("Administrator elevation is typically required to stop/start the wuauserv service and purge C:\\Windows\\SoftwareDistribution\\Download.");
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
            AffectedItemsCount = fileCount,
            EstimatedBytesAffected = totalBytes,
            AffectedTargets = new List<string>
            {
                $"{downloadPath} ({fileCount} file(s), ~{ToolArgumentHelper.FormatBytes(totalBytes)})",
                "Windows Update Service (wuauserv)"
            },
            Warnings = warnings,
            WhatWillHappen =
                $"Will stop wuauserv, delete {fileCount} cached Windows Update package file(s) (~{ToolArgumentHelper.FormatBytes(totalBytes)}) in {downloadPath}, and restart wuauserv. Installed Windows updates are never removed."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentHelper.IsCurrentProcessElevated())
        {
            var msg = "Clearing the Windows Update cache (stopping wuauserv and purging SoftwareDistribution\\Download) requires Administrator privileges. Please run RigMD as Administrator.";
            return new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = false,
                Summary = msg,
                DataJson = JsonSerializer.Serialize(new { success = false, error = msg }, ToolArgumentHelper.JsonOptions),
                OutputLog = msg
            };
        }

        progressReporter?.Invoke("Stopping wuauserv and clearing Windows Update Download cache...");

        var action = new ClearWindowsUpdateCacheAction(_loggerFactory.CreateLogger<ClearWindowsUpdateCacheAction>());
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
