using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

/// <summary>
/// Clears deletable files from a TEMP directory.
///
/// Safety behavior:
/// - Uses the current process TEMP directory by default.
/// - Can accept an explicit TEMP path for Agent-side execution.
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

    public Task<ExecutionResult> ExecuteAsync()
    {
        return ExecuteAsync(
            Path.GetTempPath());
    }

    public async Task<ExecutionResult> ExecuteAsync(
        string tempPath)
    {
        if (string.IsNullOrWhiteSpace(tempPath))
        {
            return new ExecutionResult
            {
                Success = false,
                Summary =
                    "Temp directory path was not provided.",
                OutputLog =
                    "No action was taken because the cleanup path was empty."
            };
        }

        string normalizedTempPath;

        try
        {
            normalizedTempPath =
                Path.GetFullPath(
                    tempPath);
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                Success = false,
                Summary =
                    "The provided temp directory path is invalid.",
                OutputLog =
                    $"No action was taken. Path validation failed: {ex.Message}"
            };
        }

        if (!Directory.Exists(
                normalizedTempPath))
        {
            return new ExecutionResult
            {
                Success = false,
                Summary =
                    $"Temp directory does not exist: {normalizedTempPath}",
                OutputLog =
                    "No action was taken because the TEMP directory could not be found."
            };
        }

        tempPath =
            normalizedTempPath;

        // Keep the async contract without moving filesystem
        // work to another thread unnecessarily.
        await Task.Yield();

        var bytesBefore =
            MeasureDirectorySize(
                tempPath);

        var fileCountBefore =
            CountFiles(
                tempPath);

        var deletedFiles = 0;
        var skippedFiles = 0;
        var removedDirs = 0;

        long bytesFreed = 0;

        var errors =
            new List<string>();

        /*
         * Delete files first.
         *
         * IgnoreInaccessible prevents protected directories
         * from terminating the entire remediation operation.
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
                        new FileInfo(
                            filePath);

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
                    skippedFiles++;
                }
                catch (IOException)
                {
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
         * Deepest directories are handled first.
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
                    // Protected directory.
                }
                catch (IOException)
                {
                    // Directory is still in use
                    // or contains resources.
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
             * Directory cleanup is secondary to deleting
             * the files, so failure here must not crash
             * the remediation.
             */
        }

        var bytesAfter =
            MeasureDirectorySize(
                tempPath);

        var fileCountAfter =
            CountFiles(
                tempPath);

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
            "ClearTempFiles completed: path={TempPath}, deleted={Deleted}, skipped={Skipped}, directoriesRemoved={DirectoriesRemoved}, bytesFreed={BytesFreed}",
            tempPath,
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
                    ? "The accessible TEMP directory was already clean."
                    : "No deletable temp files were found. Remaining files may be locked, protected, or currently in use.";

        return new ExecutionResult
        {
            Success =
                success,

            Summary =
                summary,

            OutputLog =
                string.Join(
                    Environment.NewLine,
                    outputLines),

            Proof =
                new List<ExecutionProof>
                {
                    new()
                    {
                        Label =
                            "File Count",

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
                        Label =
                            "Directory Size",

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
    /// IgnoreInaccessible prevents one protected directory
    /// from terminating the whole cleanup.
    ///
    /// ReparsePoint is skipped so RigMD does not recursively
    /// follow junctions or symbolic links that could lead
    /// outside the requested TEMP directory.
    /// </summary>
    private static EnumerationOptions
        CreateSafeEnumerationOptions()
    {
        return new EnumerationOptions
        {
            RecurseSubdirectories =
                true,

            IgnoreInaccessible =
                true,

            ReturnSpecialDirectories =
                false,

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

        if (bytes <
            1024 * 1024)
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