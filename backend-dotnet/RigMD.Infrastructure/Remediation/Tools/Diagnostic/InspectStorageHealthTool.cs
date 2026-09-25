using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class InspectStorageHealthTool : IRigMdAgentTool
{
    private readonly IStorageProvider _storageProvider;

    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public InspectStorageHealthTool(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    public string Name => "inspect_storage_health";

    public string DisplayName => "Inspect Storage Health & Reclaimable Caches";

    public string Description =>
        "Inspects physical drives (NVMe/SATA SSD/HDD), SMART failure status, logical volume free space/usage percentages, and measures reclaimable bytes in User Temp, Windows Temp, Browser Caches, and Windows Update Download cache.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

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
            RequiresUserConfirmation = false,
            WhatWillHappen = "Inspects disk volumes, SMART health flags, and cache directory sizes without deleting any files."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Checking disk volumes, SMART health, and reclaimable cache sizes...");

        var drives = _storageProvider.GetStorageDrives();
        var volumes = _storageProvider.GetAllDisks();

        var userTempPath = Path.GetTempPath();
        var windowsTempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var wuDownloadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SoftwareDistribution",
            "Download");

        var userTempStats = MeasureDirectory(userTempPath);
        var windowsTempStats = MeasureDirectory(windowsTempPath);
        var wuDownloadStats = MeasureDirectory(wuDownloadPath);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var chromeCacheStats = MeasureDirectory(
            Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"));
        var edgeCacheStats = MeasureDirectory(
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"));

        var totalReclaimableBytes =
            userTempStats.Bytes +
            windowsTempStats.Bytes +
            wuDownloadStats.Bytes +
            chromeCacheStats.Bytes +
            edgeCacheStats.Bytes;

        var payload = new
        {
            primaryStorageType = _storageProvider.GetPrimaryStorageType(),
            drives = drives.Select(d => new
            {
                model = d.Model,
                type = d.Type,
                sizeGb = d.SizeGb,
                isFailingSmart = d.IsFailingSmart,
                busType = d.BusType
            }),
            volumes = volumes.Select(v => new
            {
                drive = v.Drive,
                fsType = v.FsType,
                totalGb = Math.Round(v.TotalGb, 2),
                usedGb = Math.Round(v.UsedGb, 2),
                freeGb = Math.Round(Math.Max(0, v.TotalGb - v.UsedGb), 2),
                usagePercent = Math.Round(v.UsagePercent, 1)
            }),
            reclaimableCaches = new
            {
                userTemp = new { path = userTempPath, fileCount = userTempStats.FileCount, sizeMb = Math.Round(userTempStats.Bytes / (1024.0 * 1024.0), 2) },
                windowsTemp = new { path = windowsTempPath, fileCount = windowsTempStats.FileCount, sizeMb = Math.Round(windowsTempStats.Bytes / (1024.0 * 1024.0), 2) },
                windowsUpdateCache = new { path = wuDownloadPath, fileCount = wuDownloadStats.FileCount, sizeMb = Math.Round(wuDownloadStats.Bytes / (1024.0 * 1024.0), 2) },
                chromeCache = new { fileCount = chromeCacheStats.FileCount, sizeMb = Math.Round(chromeCacheStats.Bytes / (1024.0 * 1024.0), 2) },
                edgeCache = new { fileCount = edgeCacheStats.FileCount, sizeMb = Math.Round(edgeCacheStats.Bytes / (1024.0 * 1024.0), 2) },
                totalReclaimableMb = Math.Round(totalReclaimableBytes / (1024.0 * 1024.0), 2)
            }
        };

        var primaryVol = volumes.FirstOrDefault();
        var volSummary = primaryVol != null
            ? $"{primaryVol.Drive} at {primaryVol.UsagePercent:F1}% used ({Math.Max(0, primaryVol.TotalGb - primaryVol.UsedGb):F1} GB free)"
            : $"{volumes.Count} volume(s)";

        var summary =
            $"Storage: {volSummary}. SMART failures: {drives.Count(d => d.IsFailingSmart)}. Reclaimable cache/temp space: {ToolArgumentHelper.FormatBytes(totalReclaimableBytes)}.";

        return Task.FromResult(new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        });
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
                    var info = new FileInfo(file);
                    bytes += info.Length;
                    count++;
                }
                catch
                {
                    // Skip inaccessible file
                }
            }
        }
        catch
        {
            // Skip inaccessible directory
        }

        return (count, bytes);
    }
}
