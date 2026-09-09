using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

/// <summary>
/// Clears the Windows Update download cache.
///
/// Safety behavior:
/// - Targets only the SoftwareDistribution\Download folder.
/// - Stops the Windows Update service before cleanup and
///   restarts it afterward.
/// - Skips locked/in-use files.
/// - Records before/after proof.
///
/// Note: This action requires administrator privileges.
/// The process will be launched with "runas" verb, which
/// prompts the standard Windows UAC dialog.
/// </summary>
public class ClearWindowsUpdateCacheAction
{
    private readonly ILogger<ClearWindowsUpdateCacheAction> _logger;

    public ClearWindowsUpdateCacheAction(
        ILogger<ClearWindowsUpdateCacheAction> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync()
    {
        var downloadPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows),
            "SoftwareDistribution", "Download");

        if (!Directory.Exists(downloadPath))
        {
            return new ExecutionResult
            {
                Success = false,
                Summary =
                    "Windows Update download cache directory was not found.",
                OutputLog =
                    $"Expected path does not exist: {downloadPath}"
            };
        }

        await Task.Yield();

        // Measure before
        var bytesBefore = MeasureDirectorySize(downloadPath);
        var fileCountBefore = CountFiles(downloadPath);

        // Stop Windows Update service to release file locks
        var stopResult = RunCommand("net", "stop wuauserv");

        int filesDeleted = 0;
        int filesSkipped = 0;
        long bytesFreed = 0;
        var errors = new List<string>();

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
                     Directory.EnumerateFiles(
                         downloadPath, "*", options))
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
                catch (Exception ex)
                {
                    filesSkipped++;
                    if (errors.Count < 10)
                        errors.Add(
                            $"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            // Remove empty directories
            try
            {
                foreach (var dir in
                         Directory.EnumerateDirectories(
                             downloadPath, "*",
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 IgnoreInaccessible = true
                             })
                         .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (!Directory
                                .EnumerateFileSystemEntries(dir)
                                .Any())
                        {
                            Directory.Delete(dir);
                        }
                    }
                    catch { /* Non-critical */ }
                }
            }
            catch { /* Non-critical */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error cleaning Windows Update cache");
            errors.Add($"Enumeration error: {ex.Message}");
        }

        // Restart Windows Update service
        RunCommand("net", "start wuauserv");

        var bytesAfter = MeasureDirectorySize(downloadPath);
        var fileCountAfter = CountFiles(downloadPath);

        _logger.LogInformation(
            "ClearWindowsUpdateCache completed: deleted={Deleted}, skipped={Skipped}, freed={Freed}",
            filesDeleted, filesSkipped, bytesFreed);

        var success = filesDeleted > 0;

        var outputLines = new List<string>
        {
            $"Target: {downloadPath}",
            $"Service stop: {(stopResult ? "OK" : "Failed (may need admin)")}",
            $"Files before: {fileCountBefore}",
            $"Files deleted: {filesDeleted}",
            $"Files skipped: {filesSkipped}",
            $"Files after: {fileCountAfter}",
            $"Bytes freed: {bytesFreed:N0}"
        };

        if (errors.Count > 0)
        {
            outputLines.Add($"Errors ({errors.Count}):");
            outputLines.AddRange(errors.Take(10));
        }

        return new ExecutionResult
        {
            Success = success,

            Summary = success
                ? $"Cleared Windows Update cache: {filesDeleted} files deleted, {FormatBytes(bytesFreed)} freed."
                : "No Windows Update cache files could be deleted. Administrator privileges may be required.",

            OutputLog = string.Join(
                Environment.NewLine, outputLines),

            Proof = new List<ExecutionProof>
            {
                new()
                {
                    Label = "File Count",
                    Before = fileCountBefore.ToString(),
                    After = fileCountAfter.ToString(),
                    Status = filesDeleted > 0
                        ? "Reduced" : "Unchanged",
                    Meaning = filesDeleted > 0
                        ? $"{filesDeleted} Windows Update cache files were removed."
                        : "No cache files were removed."
                },
                new()
                {
                    Label = "Directory Size",
                    Before = FormatBytes(bytesBefore),
                    After = FormatBytes(bytesAfter),
                    Status = bytesFreed > 0
                        ? "Reduced" : "Unchanged",
                    Meaning = bytesFreed > 0
                        ? $"{FormatBytes(bytesFreed)} of update cache data was removed."
                        : "No measurable space was recovered."
                }
            }
        };
    }

    private bool RunCommand(string fileName, string arguments)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            process.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to run {FileName} {Arguments}",
                fileName, arguments);
            return false;
        }
    }

    private static long MeasureDirectorySize(string path)
    {
        try
        {
            return Directory
                .EnumerateFiles(path, "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }
        catch { return 0L; }
    }

    private static int CountFiles(string path)
    {
        try
        {
            return Directory
                .EnumerateFiles(path, "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                .Count();
        }
        catch { return 0; }
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
