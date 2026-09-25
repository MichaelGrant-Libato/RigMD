using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class ClearBrowserCacheTool : IRigMdAgentTool
{
    private readonly ILoggerFactory _loggerFactory;

    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public ClearBrowserCacheTool(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Name => "clear_browser_cache";

    public string DisplayName => "Clear Web Browser Disk Caches";

    public string Description =>
        "Clears disk cache directories for Chrome, Edge, and Firefox (leaves passwords, bookmarks, cookies, and history completely untouched) to reclaim disk space and fix corrupted web assets.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["browsers"] = new()
                {
                    Type = "array",
                    ItemsType = "string",
                    Description = "Optional list of browsers to target: ['chrome', 'edge', 'firefox'] (defaults to all three)."
                }
            },
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var requested = ToolArgumentHelper.GetStringArray(
            arguments,
            "browsers",
            new[] { "chrome", "edge", "firefox" });

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        int totalFiles = 0;
        long totalBytes = 0;
        var targets = new List<string>();
        var warnings = new List<string>();

        if (requested.Any(b => b.Contains("chrome", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache");
            var (files, bytes) = MeasureDirectory(path);
            totalFiles += files;
            totalBytes += bytes;
            targets.Add($"Google Chrome Cache ({files} files, ~{ToolArgumentHelper.FormatBytes(bytes)})");

            var procs = Process.GetProcessesByName("chrome");
            if (procs.Length > 0)
            {
                warnings.Add($"Google Chrome is currently running ({procs.Length} processes); some active cache files may be locked until Chrome is closed.");
                foreach (var p in procs) p.Dispose();
            }
        }

        if (requested.Any(b => b.Contains("edge", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache");
            var (files, bytes) = MeasureDirectory(path);
            totalFiles += files;
            totalBytes += bytes;
            targets.Add($"Microsoft Edge Cache ({files} files, ~{ToolArgumentHelper.FormatBytes(bytes)})");

            var procs = Process.GetProcessesByName("msedge");
            if (procs.Length > 0)
            {
                warnings.Add($"Microsoft Edge is currently running ({procs.Length} processes); some active cache files may be locked until Edge is closed.");
                foreach (var p in procs) p.Dispose();
            }
        }

        if (requested.Any(b => b.Contains("firefox", StringComparison.OrdinalIgnoreCase)))
        {
            var profilesRoot = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
            int ffFiles = 0;
            long ffBytes = 0;
            if (Directory.Exists(profilesRoot))
            {
                try
                {
                    foreach (var profileDir in Directory.EnumerateDirectories(profilesRoot))
                    {
                        var (f, b) = MeasureDirectory(Path.Combine(profileDir, "cache2"));
                        ffFiles += f;
                        ffBytes += b;
                    }
                }
                catch { }
            }

            totalFiles += ffFiles;
            totalBytes += ffBytes;
            targets.Add($"Mozilla Firefox Cache ({ffFiles} files, ~{ToolArgumentHelper.FormatBytes(ffBytes)})");

            var procs = Process.GetProcessesByName("firefox");
            if (procs.Length > 0)
            {
                warnings.Add($"Mozilla Firefox is currently running ({procs.Length} processes); some active cache files may be locked.");
                foreach (var p in procs) p.Dispose();
            }
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
            AffectedItemsCount = totalFiles,
            EstimatedBytesAffected = totalBytes,
            AffectedTargets = targets,
            Warnings = warnings,
            WhatWillHappen =
                $"Will delete up to {totalFiles} cached web asset file(s) (~{ToolArgumentHelper.FormatBytes(totalBytes)}) across {string.Join(", ", targets)}. Bookmarks, saved passwords, cookies, and history are never touched."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Clearing web browser disk cache directories...");

        var action = new ClearBrowserCacheAction(_loggerFactory.CreateLogger<ClearBrowserCacheAction>());
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
