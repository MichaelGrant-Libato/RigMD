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

public class ClearTempFilesTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public ClearTempFilesTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "clear_temp_files";

    public string DisplayName => "Clear Temporary Files";

    public string Description =>
        "Purges unlocked temporary files and subdirectories from the user %TEMP% folder (and optionally C:\\Windows\\Temp) to reclaim disk space and remove stale installer/application temp artifacts.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier1_SafeReversible;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["includeWindowsTemp"] = new()
                {
                    Type = "boolean",
                    Description = "Whether to also scan C:\\Windows\\Temp in addition to the user's %TEMP% directory (default: false)."
                },
                ["minAgeMinutes"] = new()
                {
                    Type = "integer",
                    Description = "Minimum age of temporary files in minutes before deletion (default: 0)."
                }
            },
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var includeWindowsTemp = ToolArgumentHelper.GetBool(arguments, "includeWindowsTemp", false);
        var tempPath = Path.GetTempPath();
        var (fileCount, totalBytes) = MeasureDirectory(tempPath);

        var targets = new List<string>
        {
            $"{tempPath} ({fileCount} file(s), ~{ToolArgumentHelper.FormatBytes(totalBytes)})"
        };

        if (includeWindowsTemp)
        {
            var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            var (winFiles, winBytes) = MeasureDirectory(winTemp);
            fileCount += winFiles;
            totalBytes += winBytes;
            targets.Add($"{winTemp} ({winFiles} file(s), ~{ToolArgumentHelper.FormatBytes(winBytes)})");
        }

        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = true,
            AffectedItemsCount = fileCount,
            EstimatedBytesAffected = totalBytes,
            AffectedTargets = targets,
            WhatWillHappen =
                $"Will delete unlocked temporary files in {string.Join(" and ", targets)} (up to ~{ToolArgumentHelper.FormatBytes(totalBytes)} across {fileCount} files). Active files locked by running applications are safely skipped."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        var action = new ClearTempFilesAction(_loggerFactory.CreateLogger<ClearTempFilesAction>());
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

    private static (int FileCount, long Bytes) MeasureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return (0, 0);
        }

        int count = 0;
        long bytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SafeEnumOptions))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    count++;
                }
                catch { }
            }
        }
        catch { }

        return (count, bytes);
    }
}
