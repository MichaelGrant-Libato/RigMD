using System.Diagnostics;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed class TempCleanupPlan(TempCleanupSpecification specification, DateTimeOffset captured, TempCleanupRoot[] roots, TempCleanupFile[] files, bool partial)
{
    internal TempCleanupSpecification Specification { get; } = specification;
    internal DateTimeOffset Captured { get; } = captured;
    internal TempCleanupRoot[] Roots { get; } = roots;
    internal TempCleanupFile[] Files { get; } = files;
    internal bool Partial { get; } = partial;
    private int _used;
    internal bool Consume(DateTimeOffset now) => now >= Captured && now < Captured.AddMinutes(5) && Interlocked.CompareExchange(ref _used, 1, 0) == 0;
    internal bool IsCurrent(DateTimeOffset now) => now >= Captured && now < Captured.AddMinutes(5) && Volatile.Read(ref _used) == 0;

    internal static TempCleanupPlan Capture(TempCleanupSpecification spec, IEnumerable<string> paths, DateTimeOffset now, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var roots = paths.Select(TempCleanupFiles.NormalizeRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var rootSnapshots = new List<TempCleanupRoot>(); var files = new List<TempCleanupFile>();
        var watch = Stopwatch.StartNew(); var inspected = 0; var partial = false;
        var cutoff = now.UtcDateTime.AddMinutes(-spec.MinAgeMinutes).ToFileTimeUtc();
        foreach (var root in roots)
        {
            using var pins = TempCleanupFiles.PinParents(root);
            rootSnapshots.Add(new(root, TempCleanupFiles.DirectoryIdentity(root, pins)));
        }
        foreach (var root in roots)
        {
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                cancellation.ThrowIfCancellationRequested();
                if (watch.Elapsed > TimeSpan.FromSeconds(3) || inspected >= 10000 || files.Count >= 500) { partial = true; break; }
                try
                {
                    using var pins = TempCleanupFiles.PinParents(directory);
                    foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (++inspected > 10000 || files.Count >= 500 || watch.Elapsed > TimeSpan.FromSeconds(3)) { partial = true; break; }
                        try
                        {
                            if (TempCleanupFiles.Excluded((uint)item.Attributes)) { partial = true; continue; }
                            if (item is DirectoryInfo) { pending.Push(item.FullName); continue; }
                            using var handle = TempCleanupFiles.OpenFile(item.FullName, false);
                            var info = TempCleanupFiles.Read(handle);
                            if (TempCleanupFiles.ExcludedFile(info)) { partial = true; continue; }
                            var identity = TempCleanupFiles.Identity(info);
                            if (identity.Created <= cutoff && identity.Written <= cutoff) files.Add(new(root, item.FullName, identity));
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { partial = true; }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { partial = true; }
            }
        }
        return new(spec, now, rootSnapshots.ToArray(), files.ToArray(), partial);
    }
}
