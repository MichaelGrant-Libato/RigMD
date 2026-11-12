using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RigMD.Api.Services;

public sealed record StorageFileSize(string Name, long Bytes);
public sealed record StorageFolderSize(long? FileBytes, bool Partial, int FilesCounted, StorageFileSize[] LargestFiles);
public sealed record StorageCategory(string Id, string Name, string Description, string State, StorageFolderSize? Measurement);
public sealed record StorageOverview(DateTimeOffset CapturedAt, StorageVolumeReading Volume, StorageCategory[] Categories);

/// <summary>Read-only metadata for fixed, current-account folders. Never accepts a file path from a request.</summary>
public static class StorageOverviewReader
{
    public static string? DriveForMetric(string metricId, string systemDrive)
    {
        if (metricId == "storage_usage") return systemDrive.TrimEnd('\\') + "\\";
        return Regex.IsMatch(metricId, @"\Astorage_usage_[A-Za-z]:\\?\z")
            ? metricId[14..].TrimEnd('\\').ToUpperInvariant() + "\\" : null;
    }

    public static StorageOverview Capture(StorageVolumeReading volume, CancellationToken cancellationToken)
    {
        var categories = new List<StorageCategory>
        {
            new("installed_apps", "Installed apps", "Find large apps and review supported removals. App estimates cannot be assigned reliably to this drive.", "review", null)
        };
        // One shared budget bounds all categories together, including large user folders.
        var watch = Stopwatch.StartNew();
        var remaining = 15000;
        var folders = new (string Id, string Name, string Path)[]
        {
            ("temporary_files", "Temporary files", Path.GetTempPath()),
            ("documents", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("pictures", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("desktop", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("music", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("videos", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))
        };
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OnDrive(folder.Path, volume.Drive))
            {
                categories.Add(new(folder.Id, folder.Name, "This account’s folder is unavailable or located on another drive.", "unavailable", null));
                continue;
            }
            var measured = MeasureFolder(folder.Path, ref remaining, watch, TimeSpan.FromSeconds(2), cancellationToken);
            categories.Add(new(folder.Id, folder.Name,
                folder.Id == "temporary_files" ? "This account’s temporary folder. Review cleanup separately before removing anything."
                    : "This account’s accessible local files. Select personal files below, review them, and move only your chosen files to the Recycle Bin.",
                measured.FileBytes == null ? "unavailable" : measured.Partial ? "partial" : "measured", measured));
        }
        categories.Add(new("system_other", "System & other storage", "Includes Windows, reserved space, other accounts and folders outside this review. RigMD has no reliable category total; it does not calculate this by subtracting folder sizes.", "unavailable", null));
        categories.Add(new("cloud", "Cloud files", "Online-only and linked files are skipped. This review does not download cloud files or change their availability.", "unavailable", null));
        return new(DateTimeOffset.UtcNow, volume, categories.ToArray());
    }

    public static bool OnDrive(string path, string drive) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        && string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), drive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // Reparse points include junctions, symlinks and many cloud placeholders. Recall flags must also be skipped.
    public static bool MustSkip(FileAttributes attributes) => (attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000)) != 0;

    public static StorageFolderSize MeasureFolder(string root, ref int remaining, Stopwatch watch, TimeSpan budget, CancellationToken cancellationToken, int largestLimit = 5, Func<string, bool>? includeLargest = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ancestors = new Stack<DirectoryInfo>();
            for (var ancestor = new DirectoryInfo(root); ancestor != null; ancestor = ancestor.Parent)
                ancestors.Push(ancestor);
            // Inspect parents first: do not query a child through a known link or cloud-recall parent.
            foreach (var ancestor in ancestors)
            {
                var attributes = ancestor.Attributes;
                if (MustSkip(attributes) || (attributes & FileAttributes.Directory) == 0) return new(null, true, 0, []);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, true, 0, []); }

        long bytes = 0;
        var count = 0;
        var partial = false;
        var largest = new List<StorageFileSize>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remaining <= 0 || watch.Elapsed >= budget) { partial = true; break; }
            try
            {
                // Check again at use time, rather than trusting attributes seen in the parent enumeration.
                if (MustSkip(File.GetAttributes(directory))) { partial = true; continue; }
                foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (remaining-- <= 0 || watch.Elapsed >= budget) { partial = true; break; }
                    try
                    {
                        if (MustSkip(item.Attributes)) { partial = true; continue; }
                        if (item is DirectoryInfo) { pending.Push(item.FullName); continue; }
                        var length = ((FileInfo)item).Length;
                        bytes = checked(bytes + length);
                        count++;
                        // Only names and lengths are returned, never file contents or paths usable by a mutation endpoint.
                        var name = new string(Path.GetRelativePath(root, item.FullName).Where(character => !char.IsControl(character)).Take(260).ToArray());
                        if (includeLargest == null || includeLargest(name)) largest.Add(new(name, length));
                        largest = largest.OrderByDescending(file => file.Bytes).ThenBy(file => file.Name, StringComparer.Ordinal).Take(Math.Clamp(largestLimit, 1, 40)).ToList();
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or OverflowException)
                    { partial = true; }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { partial = true; }
        }
        return new(bytes, partial, count, largest.ToArray());
    }
}
