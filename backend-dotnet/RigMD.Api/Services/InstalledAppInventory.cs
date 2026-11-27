using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Windows.Management.Deployment;

namespace RigMD.Api.Services;

public sealed record InstalledApp(
    string Id, string Name, string? Publisher, string? Version, string? Description, string Source,
    string? InstallPath, string? Drive, long? EstimatedBytes = null, string? RegisteredDate = null, string? IconPng = null);

public sealed record InstalledAppSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<InstalledApp> Apps, bool Partial);

internal static class InstalledAppInventory
{
    private const int MaxPackages = 1024;
    private const int IconBudget = 2 * 1024 * 1024; // Base64 characters
    private static readonly TimeSpan IconTimeLimit = TimeSpan.FromSeconds(6);

    public static InstalledAppSnapshot Capture(CancellationToken token)
    {
        var entries = new List<(InstalledApp App, string? IconReference)>();
        var seen = new HashSet<(RegistryHive, string, string, string, string, string?, long?)>();
        var partial = StorageAppInventory.VisitDesktopApps(token, (app, entry, hive, view, keyName) =>
        {
            string? Read(string name) => entry.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            var id = StorageAppInventory.AppId(StorageAppInventory.RegistrationIdentity(hive, view, keyName));
            var metadata = ReadDesktopMetadata(app, id, Read, hive == RegistryHive.LocalMachine);
            if (seen.Add((hive, keyName.ToUpperInvariant(), app.Name, app.Publisher, app.Version, metadata.InstallPath, app.EstimatedBytes)))
                entries.Add((metadata, Read("DisplayIcon")));
        });

        partial |= ReadPackages(entries, token);
        var icons = new WindowsAppIconReader(DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed).Select(drive => drive.RootDirectory.FullName));
        var iconClock = Stopwatch.StartNew();
        var remaining = IconBudget;
        var apps = new List<InstalledApp>(entries.Count);
        foreach (var (app, iconReference) in entries)
        {
            token.ThrowIfCancellationRequested();
            var icon = remaining > 0 && iconClock.Elapsed < IconTimeLimit
                ? app.Source == "Windows package" ? icons.ReadPackageIcon(app.InstallPath) : icons.ReadDesktopIcon(iconReference)
                : null;
            if (icon is not null && icon.Length <= remaining)
            {
                remaining -= icon.Length;
                apps.Add(app with { IconPng = icon });
            }
            else apps.Add(app);
        }
        return new(DateTimeOffset.UtcNow, apps.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.Publisher, StringComparer.OrdinalIgnoreCase).ToArray(), partial);
    }

    internal static InstalledApp ReadDesktopMetadata(StorageAppEntry registration, string id, Func<string, string?> read, bool machine)
    {
        var path = WindowsAppIconReader.NormalizeInstallPath(read("InstallLocation"));
        var date = DateOnly.TryParseExact(read("InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        return new(id, registration.Name, Clean(registration.Publisher), Clean(registration.Version),
            Clean(read("Comments"), 1000) ?? Clean(read("DisplayDescription"), 1000),
            machine ? "Desktop app · This PC" : "Desktop app · Current account",
            path, DriveLabel(path), registration.EstimatedBytes, date);
    }

    private static bool ReadPackages(List<(InstalledApp App, string? IconReference)> entries, CancellationToken token)
    {
        var partial = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Current account unavailable.");
            var inspected = 0;
            var manager = new PackageManager();
            foreach (var package in manager.FindPackagesForUser(sid))
            {
                token.ThrowIfCancellationRequested();
                if (++inspected > MaxPackages) return true;
                try
                {
                    if (!WindowsPackageInventory.IsMainApp(package)) continue;
                    var identityName = package.Id;
                    var name = ReadPackageText(() => package.DisplayName) ?? Clean(identityName.Name);
                    if (name is null) { partial = true; continue; }
                    var version = identityName.Version;
                    var path = WindowsAppIconReader.NormalizeInstallPath(ReadPackageText(() => package.InstalledLocation.Path, 32767));
                    var id = StorageAppInventory.AppId($"appx:{identityName.FullName}");
                    entries.Add((new(id, name, ReadPackageText(() => package.PublisherDisplayName),
                        $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                        ReadPackageText(() => package.Description, 1000), "Windows package", path, DriveLabel(path)), null));
                }
                catch (Exception ex) when (IsPackageReadFailure(ex)) { partial = true; }
            }
        }
        catch (Exception ex) when (IsPackageReadFailure(ex) || ex is TypeLoadException) { partial = true; }
        return partial;
    }

    private static string? ReadPackageText(Func<string> getValue, int limit = 200)
    {
        try { return Clean(getValue(), limit); }
        catch (Exception ex) when (IsPackageReadFailure(ex)) { return null; }
    }

    private static bool IsPackageReadFailure(Exception ex) =>
        ex is UnauthorizedAccessException or COMException or InvalidOperationException or IOException or SecurityException;

    private static string? DriveLabel(string? path) => path is null ? null : path[..2].ToUpperInvariant();

    private static string? Clean(string? value, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = new string(value.Take(limit).Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        return clean.Length == 0 ? null : clean;
    }
}
