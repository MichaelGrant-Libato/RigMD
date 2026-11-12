using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

// Both cleanup tools use the same approval-bound identity checks and deletion handles.
internal static class ApprovedFileCleanup
{
    internal static Task<ExecutionResult> ExecuteAsync(TempCleanupPlan plan, DateTimeOffset now, CancellationToken cancellation,
        ILogger logger, string fileKind, string deletedLabel, Action<string>? report = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!plan.Consume(now)) return Task.FromResult(new ExecutionResult { Success = false, Summary = "The approved cleanup snapshot expired or was already used. Nothing was deleted." });
        var handles = new List<(TempCleanupFile File, SafeFileHandle Handle)>();
        var pins = new List<IDisposable>();
        var skipped = 0; var changed = 0; var deleted = 0; long logicalBytes = 0;
        try
        {
            var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in plan.Roots)
            {
                cancellation.ThrowIfCancellationRequested();
                var rootPins = TempCleanupFiles.PinParents(root.Path); pins.Add(rootPins);
                var current = TempCleanupFiles.DirectoryIdentity(root.Path, rootPins);
                if (current.Volume != root.Identity.Volume || current.IndexHigh != root.Identity.IndexHigh || current.IndexLow != root.Identity.IndexLow || current.Created != root.Identity.Created)
                    throw new InvalidOperationException("Cleanup folder identity changed.");
                pinned.Add(root.Path);
            }
            // Validate all available candidates before deletion. Held handles deny writes/renames.
            foreach (var file in plan.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!plan.Roots.Any(root => root.Path == file.Root) || !file.Path.StartsWith(file.Root + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid cleanup target.");
                SafeFileHandle? handle = null;
                try
                {
                    var parent = Path.GetDirectoryName(file.Path)!;
                    if (!pinned.Contains(parent))
                    {
                        // Record a directory as verified only after its complete parent chain is pinned.
                        pins.Add(TempCleanupFiles.PinParents(parent)); pinned.Add(parent);
                    }
                    handle = TempCleanupFiles.OpenFile(file.Path, true);
                    var info = TempCleanupFiles.Read(handle);
                    if (TempCleanupFiles.ExcludedFile(info) || TempCleanupFiles.Identity(info) != file.Identity)
                    { changed++; handle.Dispose(); continue; }
                    handles.Add((file, handle));
                }
                catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode is 2 or 3)
                { changed++; handle?.Dispose(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { skipped++; handle?.Dispose(); }
            }
            foreach (var (file, handle) in handles)
            {
                cancellation.ThrowIfCancellationRequested();
                try { TempCleanupFiles.MarkDeleted(handle); deleted++; logicalBytes += file.Identity.Bytes; }
                catch (System.ComponentModel.Win32Exception) { skipped++; }
                handle.Dispose();
                try { report?.Invoke($"[CLEANUP] Processed {deleted} approved {fileKind} files."); } catch { }
            }
            return Task.FromResult(Result(deleted > 0 || plan.Files.Length == 0, false));
        }
        catch (OperationCanceledException) { return Task.FromResult(Result(false, true)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("Approved cleanup stopped after {Deleted} files; error type {ErrorType}.", deleted, error.GetType().Name);
            return Task.FromResult(Result(false, false, "A file or folder changed or could not be verified. Review a fresh preview before continuing."));
        }
        finally { foreach (var item in handles) item.Handle.Dispose(); foreach (var pin in pins.AsEnumerable().Reverse()) pin.Dispose(); }

        ExecutionResult Result(bool success, bool canceled, string? failure = null)
        {
            var counts = $"{changed} changed or missing file(s) and {skipped} locked, protected or unverifiable file(s) were skipped.";
            var summary = canceled ? $"Cleanup canceled after {deleted} file deletion(s). Some files may already be removed; review the result before taking another action."
                : failure ?? (plan.Files.Length == 0 ? "No eligible files were included in this snapshot. Nothing was deleted."
                : deleted == 0 ? $"No files were removed. {counts} Review a fresh preview before trying again."
                : $"Deleted {deleted} unchanged approved {fileKind} file(s). {counts} Files created after preview were left alone. Review skipped files in a new preview if further cleanup is needed.");
            return new()
            {
                Success = success, Summary = summary, OutputLog = summary,
                Proof = [new() { Label = "Files included in preview", After = plan.Files.Length.ToString(), Status = "Reviewed" },
                    new() { Label = deletedLabel, After = deleted.ToString(), Status = success ? "Completed" : canceled ? "Canceled" : "Not fully completed", Meaning = summary },
                    new() { Label = "Previewed files not removed", After = (plan.Files.Length - deleted).ToString(), Status = "Observed", Meaning = counts },
                    new() { Label = "Size of deleted files", After = logicalBytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes", Status = "Observed", Meaning = "Logical file sizes are not guaranteed recovered drive space. Other handles, compression and other activity can affect available space. Recheck storage separately." }]
            };
        }
    }
}
