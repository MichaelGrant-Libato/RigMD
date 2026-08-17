using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

/// <summary>
/// Clears non-locked files from the current user's %TEMP% directory.
/// Skips files that are in use. Never deletes subdirectories that are still in use.
/// Records file count and bytes freed as execution proof.
/// </summary>
public class ClearTempFilesAction
{
    private readonly ILogger<ClearTempFilesAction> _logger;

    public ClearTempFilesAction(ILogger<ClearTempFilesAction> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync()
    {
        var tempPath = Path.GetTempPath();

        if (!Directory.Exists(tempPath))
        {
            return new ExecutionResult
            {
                Success = false,
                Summary = $"Temp directory does not exist: {tempPath}",
                OutputLog = "No action taken."
            };
        }

        // Measure before state
        long bytesBefore = MeasureDirectorySize(tempPath);
        int fileCountBefore = CountFiles(tempPath);

        int deletedFiles = 0;
        int skippedFiles = 0;
        long bytesFreed = 0;
        var errors = new List<string>();

        // Delete files (not directories first — safer)
        foreach (var filePath in Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;

                fileInfo.Delete();
                deletedFiles++;
                bytesFreed += fileSize;
            }
            catch (UnauthorizedAccessException)
            {
                skippedFiles++;
            }
            catch (IOException)
            {
                // File is locked / in use — expected behavior, skip silently
                skippedFiles++;
            }
            catch (Exception ex)
            {
                skippedFiles++;
                errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        // Attempt to remove empty subdirectories (bottom-up)
        int removedDirs = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(tempPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length)) // deepest first
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        removedDirs++;
                    }
                }
                catch
                {
                    // Directory in use or permission denied — skip
                }
            }
        }
        catch
        {
            // Enumeration failed — non-critical
        }

        // Measure after state
        long bytesAfter = MeasureDirectorySize(tempPath);
        int fileCountAfter = CountFiles(tempPath);

        var outputLines = new List<string>
        {
            $"Temp directory: {tempPath}",
            $"Files before: {fileCountBefore}",
            $"Files deleted: {deletedFiles}",
            $"Files skipped (in use): {skippedFiles}",
            $"Empty directories removed: {removedDirs}",
            $"Files after: {fileCountAfter}",
            $"Bytes freed: {bytesFreed:N0}"
        };

        if (errors.Count > 0)
        {
            outputLines.Add($"Errors ({errors.Count}):");
            outputLines.AddRange(errors.Take(10)); // Cap logged errors
        }

        _logger.LogInformation(
            "ClearTempFiles completed: deleted={Deleted}, skipped={Skipped}, bytesFreed={BytesFreed}",
            deletedFiles, skippedFiles, bytesFreed);

        return new ExecutionResult
        {
            Success = deletedFiles > 0 || fileCountBefore == 0,
            Summary = deletedFiles > 0
                ? $"Deleted {deletedFiles} temp files, freed {FormatBytes(bytesFreed)}."
                : "No deletable temp files found (all files were in use or directory was already clean).",
            OutputLog = string.Join(Environment.NewLine, outputLines),
            Proof = new List<ExecutionProof>
            {
                new ExecutionProof
                {
                    Label = "File Count",
                    Before = fileCountBefore.ToString(),
                    After = fileCountAfter.ToString(),
                    Status = deletedFiles > 0 ? "Reduced" : "Unchanged",
                    Meaning = deletedFiles > 0
                        ? $"{deletedFiles} temporary files were safely removed."
                        : "No files could be removed (all were in use)."
                },
                new ExecutionProof
                {
                    Label = "Directory Size",
                    Before = FormatBytes(bytesBefore),
                    After = FormatBytes(bytesAfter),
                    Status = bytesFreed > 0 ? "Reduced" : "Unchanged",
                    Meaning = bytesFreed > 0
                        ? $"{FormatBytes(bytesFreed)} of disk space was recovered."
                        : "No measurable disk space was recovered."
                }
            }
        };
    }

    private static long MeasureDirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return 0L;
        }
    }

    private static int CountFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
