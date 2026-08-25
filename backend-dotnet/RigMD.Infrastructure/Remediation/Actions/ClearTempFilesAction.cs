using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

/// <summary>
/// Clears deletable files from the current user's %TEMP% directory.
///
/// Safety behavior:
/// - Operates only inside the current user's TEMP directory.
/// - Skips inaccessible directories.
/// - Skips locked/in-use files.
/// - Does not follow directory junctions/reparse points.
/// - Removes only empty directories after file cleanup.
/// - Records before/after proof for the remediation result.
/// </summary>
public class ClearTempFilesAction
{
    private readonly ILogger<ClearTempFilesAction> _logger;

    public ClearTempFilesAction(
        ILogger<ClearTempFilesAction> logger)
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
                Summary =
                    $"Temp directory does not exist: {tempPath}",
                OutputLog =
                    "No action was taken because the current user's TEMP directory could not be found."
            };
        }

        // Keep the async contract without moving filesystem work
        // to another thread unnecessarily.
        await Task.Yield();

        // Measure the accessible portion of TEMP before cleanup.
        var bytesBefore =
            MeasureDirectorySize(tempPath);

        var fileCountBefore =
            CountFiles(tempPath);

        var deletedFiles = 0;
        var skippedFiles = 0;
        var removedDirs = 0;
        long bytesFreed = 0;

        var errors =
            new List<string>();

        /*
         * Delete files first.
         *
         * EnumerationOptions.IgnoreInaccessible prevents protected
         * directories such as TEMP\WinSAT from terminating the entire
         * remediation operation while the enumerator traverses TEMP.
         */
        try
        {
            foreach (var filePath in
                     Directory.EnumerateFiles(
                         tempPath,
                         "*",
                         CreateSafeEnumerationOptions()))
            {
                try
                {
                    var fileInfo =
                        new FileInfo(filePath);

                    long fileSize;

                    try
                    {
                        fileSize =
                            fileInfo.Length;
                    }
                    catch
                    {
                        fileSize = 0;
                    }

                    fileInfo.Delete();

                    deletedFiles++;
                    bytesFreed += fileSize;
                }
                catch (UnauthorizedAccessException)
                {
                    // Protected file — expected on some Windows systems.
                    skippedFiles++;
                }
                catch (IOException)
                {
                    // Locked or currently in use.
                    skippedFiles++;
                }
                catch (Exception ex)
                {
                    skippedFiles++;

                    if (errors.Count < 10)
                    {
                        errors.Add(
                            $"{Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            /*
             * IgnoreInaccessible should normally prevent this while
             * traversing child directories. This catch protects the
             * operation if the root TEMP directory itself changes
             * permissions during execution.
             */
            errors.Add(
                $"TEMP enumeration access denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            errors.Add(
                $"TEMP enumeration error: {ex.Message}");
        }

        /*
         * Remove empty directories after deleting files.
         *
         * Enumerate deepest directories first so children are handled
         * before their parents.
         */
        try
        {
            var directories =
                Directory
                    .EnumerateDirectories(
                        tempPath,
                        "*",
                        CreateSafeEnumerationOptions())
                    .OrderByDescending(
                        directory =>
                            directory.Length);

            foreach (var directory in directories)
            {
                try
                {
                    if (!Directory
                            .EnumerateFileSystemEntries(
                                directory)
                            .Any())
                    {
                        Directory.Delete(
                            directory);

                        removedDirs++;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Protected directory — safely skip.
                }
                catch (IOException)
                {
                    // Directory still contains or is using resources.
                }
                catch
                {
                    // Non-critical cleanup failure.
                }
            }
        }
        catch
        {
            /*
             * Directory cleanup is secondary to deleting the files.
             * A failure here must not crash the remediation.
             */
        }

        // Measure accessible TEMP state after cleanup.
        var bytesAfter =
            MeasureDirectorySize(tempPath);

        var fileCountAfter =
            CountFiles(tempPath);

        var outputLines =
            new List<string>
            {
                $"Temp directory: {tempPath}",
                $"Accessible files before: {fileCountBefore}",
                $"Files deleted: {deletedFiles}",
                $"Files skipped (locked/inaccessible): {skippedFiles}",
                $"Empty directories removed: {removedDirs}",
                $"Accessible files after: {fileCountAfter}",
                $"Bytes freed: {bytesFreed:N0}"
            };

        if (errors.Count > 0)
        {
            outputLines.Add(
                $"Non-fatal errors ({errors.Count}):");

            outputLines.AddRange(
                errors.Take(10));
        }

        _logger.LogInformation(
            "ClearTempFiles completed: deleted={Deleted}, skipped={Skipped}, directoriesRemoved={DirectoriesRemoved}, bytesFreed={BytesFreed}",
            deletedFiles,
            skippedFiles,
            removedDirs,
            bytesFreed);

        var success =
            deletedFiles > 0 ||
            fileCountBefore == 0;

        var summary =
            deletedFiles > 0
                ? $"Deleted {deletedFiles} temp files and freed {FormatBytes(bytesFreed)}."
                : fileCountBefore == 0
                    ? "The accessible user TEMP directory was already clean."
                    : "No deletable temp files were found. Remaining files may be locked, protected, or currently in use.";

        return new ExecutionResult
        {
            Success = success,

            Summary = summary,

            OutputLog =
                string.Join(
                    Environment.NewLine,
                    outputLines),

            Proof = new List<ExecutionProof>
            {
                new()
                {
                    Label = "File Count",

                    Before =
                        fileCountBefore.ToString(),

                    After =
                        fileCountAfter.ToString(),

                    Status =
                        deletedFiles > 0
                            ? "Reduced"
                            : "Unchanged",

                    Meaning =
                        deletedFiles > 0
                            ? $"{deletedFiles} accessible temporary files were removed."
                            : "No accessible temporary files were removed."
                },

                new()
                {
                    Label = "Directory Size",

                    Before =
                        FormatBytes(
                            bytesBefore),

                    After =
                        FormatBytes(
                            bytesAfter),

                    Status =
                        bytesFreed > 0
                            ? "Reduced"
                            : "Unchanged",

                    Meaning =
                        bytesFreed > 0
                            ? $"{FormatBytes(bytesFreed)} of temporary data was removed."
                            : "No measurable temporary disk space was recovered."
                }
            }
        };
    }

    /// <summary>
    /// Provides safe recursive filesystem enumeration.
    ///
    /// IgnoreInaccessible prevents one protected directory from
    /// terminating the whole cleanup.
    ///
    /// ReparsePoint is skipped so RigMD does not recursively follow
    /// junctions/symbolic links that could lead outside TEMP.
    /// </summary>
    private static EnumerationOptions
        CreateSafeEnumerationOptions()
    {
        return new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,

            AttributesToSkip =
                FileAttributes.ReparsePoint
        };
    }

    private static long
        MeasureDirectorySize(
            string path)
    {
        try
        {
            return Directory
                .EnumerateFiles(
                    path,
                    "*",
                    CreateSafeEnumerationOptions())
                .Sum(
                    filePath =>
                    {
                        try
                        {
                            return new FileInfo(
                                    filePath)
                                .Length;
                        }
                        catch
                        {
                            return 0L;
                        }
                    });
        }
        catch
        {
            return 0L;
        }
    }

    private static int
        CountFiles(
            string path)
    {
        try
        {
            return Directory
                .EnumerateFiles(
                    path,
                    "*",
                    CreateSafeEnumerationOptions())
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string
        FormatBytes(
            long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return
                $"{bytes / 1024.0:F1} KB";
        }

        if (bytes <
            1024L * 1024 * 1024)
        {
            return
                $"{bytes / (1024.0 * 1024):F1} MB";
        }

        return
            $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}