using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Application.Services.Autonomy;

public class DryRunRemediationExecutor : IDryRunRemediationExecutor
{
    public Task<ExecutionResult> ExecuteAsync(RemediationActionDef action)
    {
        var (summary, proof) = action.Id switch
        {
            "clear_user_temp_files" => EstimateTempFiles(),
            "flush_dns" => EstimateFlushDns(),
            "clear_browser_cache" => EstimateBrowserCache(),
            "clear_windows_update_cache" => EstimateUpdateCache(),
            "run_disk_cleanup" => EstimateDiskCleanup(),
            "run_sfc_scan" => EstimateSfcScan(),
            _ => (
                $"DRY RUN: Would execute {action.Name}.",
                new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "Dry Run",
                        Status = "Simulated",
                        Meaning = "No action-specific preview is available.",
                        Before = "Current state",
                        After = "Unknown"
                    }
                }
            )
        };

        var result = new ExecutionResult
        {
            Success = true,
            Summary = summary,
            OutputLog = $"DRY RUN simulation for action: {action.Id}",
            Proof = proof
        };

        return Task.FromResult(result);
    }

    private static (string summary, List<ExecutionProof> proof)
        EstimateTempFiles()
    {
        var tempPath = Path.GetTempPath();
        int fileCount = 0;
        long totalSize = 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in Directory.EnumerateFiles(
                         tempPath, "*", options))
            {
                try
                {
                    totalSize += new FileInfo(file).Length;
                    fileCount++;
                }
                catch { /* skip inaccessible */ }
            }
        }
        catch { /* best effort */ }

        return (
            $"DRY RUN: Would attempt to clear ~{fileCount} temp files ({FormatBytes(totalSize)}).",
            new List<ExecutionProof>
            {
                new()
                {
                    Label = "Temp Files Found",
                    Before = $"{fileCount} files ({FormatBytes(totalSize)})",
                    After = "Would be deleted",
                    Status = "Preview",
                    Meaning = $"The TEMP folder currently contains {fileCount} accessible files totaling {FormatBytes(totalSize)}."
                }
            }
        );
    }

    private static (string summary, List<ExecutionProof> proof)
        EstimateFlushDns()
    {
        return (
            "DRY RUN: Would flush the Windows DNS resolver cache (ipconfig /flushdns).",
            new List<ExecutionProof>
            {
                new()
                {
                    Label = "DNS Cache",
                    Before = "Cached entries present",
                    After = "Would be flushed",
                    Status = "Preview",
                    Meaning = "All cached DNS entries would be cleared, forcing fresh lookups."
                }
            }
        );
    }

    private static (string summary, List<ExecutionProof> proof)
        EstimateBrowserCache()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        var paths = new Dictionary<string, string>
        {
            ["Chrome"] = Path.Combine(
                localAppData, "Google", "Chrome",
                "User Data", "Default", "Cache"),
            ["Edge"] = Path.Combine(
                localAppData, "Microsoft", "Edge",
                "User Data", "Default", "Cache")
        };

        long totalSize = 0;
        var found = new List<string>();

        foreach (var (browser, path) in paths)
        {
            if (!Directory.Exists(path)) continue;

            try
            {
                var size = Directory
                    .EnumerateFiles(path, "*",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true
                        })
                    .Sum(f =>
                    {
                        try { return new FileInfo(f).Length; }
                        catch { return 0L; }
                    });
                totalSize += size;
                found.Add($"{browser} ({FormatBytes(size)})");
            }
            catch { /* best effort */ }
        }

        var browserList = found.Count > 0
            ? string.Join(", ", found)
            : "No browser caches found";

        return (
            $"DRY RUN: Would clear ~{FormatBytes(totalSize)} of browser cache data. Detected: {browserList}.",
            new List<ExecutionProof>
            {
                new()
                {
                    Label = "Browser Cache",
                    Before = $"{FormatBytes(totalSize)} across {found.Count} browser(s)",
                    After = "Would be cleared",
                    Status = "Preview",
                    Meaning = $"Detected caches: {browserList}."
                }
            }
        );
    }

    private static (string summary, List<ExecutionProof> proof)
        EstimateUpdateCache()
    {
        var downloadPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows),
            "SoftwareDistribution", "Download");

        long totalSize = 0;
        int fileCount = 0;

        if (Directory.Exists(downloadPath))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             downloadPath, "*",
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 IgnoreInaccessible = true
                             }))
                {
                    try
                    {
                        totalSize += new FileInfo(file).Length;
                        fileCount++;
                    }
                    catch { /* skip */ }
                }
            }
            catch { /* best effort */ }
        }

        return (
            $"DRY RUN: Would clear ~{fileCount} Windows Update cache files ({FormatBytes(totalSize)}). Requires admin privileges.",
            new List<ExecutionProof>
            {
                new()
                {
                    Label = "Update Cache",
                    Before = $"{fileCount} files ({FormatBytes(totalSize)})",
                    After = "Would be cleared",
                    Status = "Preview",
                    Meaning = $"The SoftwareDistribution\\Download folder contains {fileCount} files totaling {FormatBytes(totalSize)}."
                }
            }
        );
    }

    private static (string summary, List<ExecutionProof> proof)
        EstimateDiskCleanup()
    {
        var systemDrive = Path.GetPathRoot(
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows)) ?? "C:\\";

        long freeSpace = 0;
        long totalSpace = 0;

        try
        {
            var driveInfo = new DriveInfo(systemDrive.Substring(0, 1));
            freeSpace = driveInfo.AvailableFreeSpace;
            totalSpace = driveInfo.TotalSize;
        }
        catch { /* best effort */ }

        return (
            $"DRY RUN: Would run Windows Disk Cleanup on drive {systemDrive}. Current free space: {FormatBytes(freeSpace)} / {FormatBytes(totalSpace)}.",
            new List<ExecutionProof>
            {
                new()
                {
                    Label = "Disk Space",
                    Before = $"{FormatBytes(freeSpace)} free / {FormatBytes(totalSpace)} total",
                    After = "Would be improved",
                    Status = "Preview",
                    Meaning = "Disk Cleanup targets temporary files, thumbnails, and delivery optimization caches."
                }
            }
        );
    }

    private static (string summary, List<ExecutionProof> proof)
        EstimateSfcScan()
    {
        return (
            "DRY RUN: Would run System File Checker (sfc /scannow). This scan typically takes 5-15 minutes. Requires admin privileges.",
            new List<ExecutionProof>
            {
                new()
                {
                    Label = "System File Integrity",
                    Before = "Unknown",
                    After = "Would be scanned",
                    Status = "Preview",
                    Meaning = "SFC scans all protected Windows system files and replaces corrupted ones with cached copies."
                }
            }
        );
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
