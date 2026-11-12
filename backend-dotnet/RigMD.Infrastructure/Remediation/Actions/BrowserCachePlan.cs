using System.Diagnostics;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed record BrowserCachePlan(BrowserCacheSpecification Specification, string LocalAppData,
    TempCleanupPlan Snapshot, string[] Warnings)
{
    internal static string NormalizeBase(string path)
    {
        // Validate a derived cache path; the account's base folder is never a deletion target.
        TempCleanupFiles.NormalizeRoot(Path.Combine(path, "Google", "Chrome", "User Data", "Default", "Cache"));
        return Path.GetFullPath(path).TrimEnd('\\');
    }

    internal static BrowserCachePlan Capture(BrowserCacheSpecification spec, string localAppData,
        DateTimeOffset now, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var basePath = NormalizeBase(localAppData);
        using var basePins = TempCleanupFiles.PinParents(basePath);
        var roots = new List<string>();
        var warnings = new List<string>();
        var partial = false;
        var watch = Stopwatch.StartNew();
        foreach (var browser in spec.Browsers)
        {
            cancellation.ThrowIfCancellationRequested();
            if (browser is "chrome" or "edge")
            {
                AddIfPresent(Path.Combine(basePath, browser == "chrome" ? "Google" : "Microsoft",
                    browser == "chrome" ? "Chrome" : "Edge", "User Data", "Default", "Cache"));
                continue;
            }
            var profiles = Path.Combine(basePath, "Mozilla", "Firefox", "Profiles");
            try
            {
                if (!Directory.Exists(profiles)) continue;
                using var profilePins = TempCleanupFiles.PinParents(profiles);
                var count = 0;
                foreach (var profile in new DirectoryInfo(profiles).EnumerateDirectories())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++count > 128 || watch.Elapsed > TimeSpan.FromSeconds(2)) { partial = true; break; }
                    if (TempCleanupFiles.Excluded((uint)profile.Attributes)) { partial = true; continue; }
                    AddIfPresent(Path.Combine(profile.FullName, "cache2"));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { partial = true; }
        }
        var snapshot = TempCleanupPlan.Capture(new(false, 0), roots, now, cancellation);
        if (partial || snapshot.Partial)
            warnings.Add("This is a partial, bounded snapshot. Only captured files may be deleted. Skipped, inaccessible and newly created files require another review.");
        return new(spec, basePath, snapshot, warnings.ToArray());

        void AddIfPresent(string path)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0)
                    throw new InvalidOperationException("A browser-cache folder could not be verified.");
                roots.Add(TempCleanupFiles.NormalizeRoot(path));
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { partial = true; }
        }
    }
}
