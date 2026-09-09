using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

/// <summary>
/// Clears browser cache directories for Chrome, Edge, and Firefox.
///
/// Safety behavior:
/// - Only targets well-known cache subdirectories.
/// - Skips locked/in-use files (browsers may be running).
/// - Does not delete profiles, bookmarks, history, or passwords.
/// - Records before/after proof for the remediation result.
/// </summary>
public class ClearBrowserCacheAction
{
    private readonly ILogger<ClearBrowserCacheAction> _logger;

    public ClearBrowserCacheAction(
        ILogger<ClearBrowserCacheAction> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync()
    {
        await Task.Yield();

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var cachePaths = new Dictionary<string, string>
        {
            ["Chrome"] = Path.Combine(
                localAppData,
                "Google", "Chrome", "User Data", "Default", "Cache"),
            ["Edge"] = Path.Combine(
                localAppData,
                "Microsoft", "Edge", "User Data", "Default", "Cache"),
            ["Firefox"] = Path.Combine(
                localAppData,
                "Mozilla", "Firefox", "Profiles")
        };

        long totalBytesFreed = 0;
        int totalFilesDeleted = 0;
        int totalFilesSkipped = 0;
        var browserResults = new List<string>();

        foreach (var (browser, cachePath) in cachePaths)
        {
            if (browser == "Firefox")
            {
                var result = ClearFirefoxCache(cachePath);
                totalBytesFreed += result.bytesFreed;
                totalFilesDeleted += result.filesDeleted;
                totalFilesSkipped += result.filesSkipped;
                browserResults.Add(
                    $"{browser}: {result.filesDeleted} files deleted, {FormatBytes(result.bytesFreed)} freed");
                continue;
            }

            if (!Directory.Exists(cachePath))
            {
                browserResults.Add(
                    $"{browser}: cache directory not found");
                continue;
            }

            var cacheResult = ClearDirectory(cachePath);
            totalBytesFreed += cacheResult.bytesFreed;
            totalFilesDeleted += cacheResult.filesDeleted;
            totalFilesSkipped += cacheResult.filesSkipped;
            browserResults.Add(
                $"{browser}: {cacheResult.filesDeleted} files deleted, {FormatBytes(cacheResult.bytesFreed)} freed");
        }

        _logger.LogInformation(
            "ClearBrowserCache completed: totalDeleted={Deleted}, totalSkipped={Skipped}, totalFreed={Freed}",
            totalFilesDeleted,
            totalFilesSkipped,
            totalBytesFreed);

        var success = totalFilesDeleted > 0;

        return new ExecutionResult
        {
            Success = success,

            Summary = success
                ? $"Cleared browser cache: {totalFilesDeleted} files deleted, {FormatBytes(totalBytesFreed)} freed."
                : "No browser cache files could be deleted. Browsers may be running or caches are already clean.",

            OutputLog = string.Join(
                Environment.NewLine,
                browserResults),

            Proof = new List<ExecutionProof>
            {
                new()
                {
                    Label = "Files Deleted",
                    Before = "Cached",
                    After = totalFilesDeleted.ToString(),
                    Status = success ? "Reduced" : "Unchanged",
                    Meaning = success
                        ? $"{totalFilesDeleted} browser cache files were removed."
                        : "No browser cache files were removed."
                },
                new()
                {
                    Label = "Space Freed",
                    Before = "In use",
                    After = FormatBytes(totalBytesFreed),
                    Status = success ? "Reduced" : "Unchanged",
                    Meaning = success
                        ? $"{FormatBytes(totalBytesFreed)} of browser cache data was removed."
                        : "No measurable browser cache space was recovered."
                }
            }
        };
    }

    private (long bytesFreed, int filesDeleted, int filesSkipped)
        ClearDirectory(string path)
    {
        long bytesFreed = 0;
        int filesDeleted = 0;
        int filesSkipped = 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var filePath in
                     Directory.EnumerateFiles(path, "*", options))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    long size;

                    try { size = info.Length; }
                    catch { size = 0; }

                    info.Delete();
                    filesDeleted++;
                    bytesFreed += size;
                }
                catch (UnauthorizedAccessException) { filesSkipped++; }
                catch (IOException) { filesSkipped++; }
                catch { filesSkipped++; }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error enumerating cache directory: {Path}", path);
        }

        return (bytesFreed, filesDeleted, filesSkipped);
    }

    private (long bytesFreed, int filesDeleted, int filesSkipped)
        ClearFirefoxCache(string profilesPath)
    {
        long totalBytesFreed = 0;
        int totalDeleted = 0;
        int totalSkipped = 0;

        if (!Directory.Exists(profilesPath))
            return (0, 0, 0);

        try
        {
            foreach (var profileDir in
                     Directory.EnumerateDirectories(profilesPath))
            {
                var cache2 = Path.Combine(profileDir, "cache2");

                if (Directory.Exists(cache2))
                {
                    var result = ClearDirectory(cache2);
                    totalBytesFreed += result.bytesFreed;
                    totalDeleted += result.filesDeleted;
                    totalSkipped += result.filesSkipped;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error enumerating Firefox profiles: {Path}",
                profilesPath);
        }

        return (totalBytesFreed, totalDeleted, totalSkipped);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
