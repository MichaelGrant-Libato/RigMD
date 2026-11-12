using Microsoft.Win32;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RigMD.Api.Services;

public sealed record StorageAppEntry(string Name, string Publisher, string Version, long? EstimatedBytes, string Scope)
{
    public string Id { get; init; } = "";
    public bool CanUninstall { get; init; }
    public string UninstallReason { get; init; } = "This app uses an unsupported installer. RigMD cannot uninstall it directly yet.";
    [JsonIgnore] public string? ProductCode { get; init; }
    [JsonIgnore] public RegisteredUninstallTarget? RegisteredTarget { get; init; }
    [JsonIgnore] public PackageRemovalTarget? PackageTarget { get; init; }
    public string UninstallMethod { get; init; } = "none";
    public string SourceType { get; init; } = "desktop";
}
public sealed record StorageAppSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<StorageAppEntry> Apps, bool Partial, string Summary);

public static class StorageAppInventory
{
    public static RegistryView[] ViewsFor(RegistryHive hive, bool is64BitOperatingSystem) =>
        hive == RegistryHive.CurrentUser ? [RegistryView.Default]
            : is64BitOperatingSystem ? [RegistryView.Registry64, RegistryView.Registry32] : [RegistryView.Registry32];

    public static string RegistrationIdentity(RegistryHive hive, RegistryView view, string keyName) =>
        $"{hive}:{(hive == RegistryHive.CurrentUser ? RegistryView.Default : view)}:{keyName.ToUpperInvariant()}";

    public static bool ProtectedApp(string name, string publisher) =>
        Regex.IsMatch(publisher, @"crowdstrike|sentinelone|sophos|\beset\b|bitdefender|mcafee|norton|symantec|avast|kaspersky|malwarebytes|fortinet|zscaler|trend micro|palo alto", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(name, @"rigmd|windows|driver|runtime|redistributable|framework|security|anti.?virus|defender|firewall|endpoint|\bsdk\b|\bupdate\b|installer|\bvpn\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static StorageAppEntry Identify(StorageAppEntry app, string identity, bool machine, string keyName, object? windowsInstaller, object? noRemove)
    {
        var protectedApp = ProtectedApp(app.Name, app.Publisher);
        var supported = machine && windowsInstaller is int installer && installer == 1 &&
            Guid.TryParseExact(keyName, "B", out _) && (noRemove is null or 0) && !protectedApp;
        return app with
        {
            Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),
            ProductCode = supported ? keyName.ToUpperInvariant() : null,
            CanUninstall = supported,
            UninstallMethod = supported ? "windows_installer" : "none",
            UninstallReason = supported ? "Windows Installer registration; a fresh preview and approval are required."
                : protectedApp ? "Protected or shared system software. Direct removal is blocked in RigMD."
                : app.UninstallReason
        };
    }
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const int MaxEntries = 2048;

    public static StorageAppEntry? ReadEntry(Func<string, object?> read, string scope)
    {
        string Text(string name) => read(name) is string value
            ? new string(value.Where(character => !char.IsControl(character)).Take(200).ToArray()).Trim() : "";
        var name = Text("DisplayName");
        if (name.Length == 0 || read("SystemComponent") is int system && system != 0 ||
            Text("ParentKeyName").Length > 0 || Text("ReleaseType").Length > 0) return null;
        var size = read("EstimatedSize");
        long? bytes = size is int kilobytes && kilobytes > 0 ? (long)kilobytes * 1024 : null;
        return new(name, Text("Publisher"), Text("DisplayVersion"), bytes, scope);
    }

    public static StorageAppSnapshot Capture(CancellationToken cancellationToken, string? verifyAppId = null, bool includeStore = true)
    {
        var apps = new List<StorageAppEntry>();
        var partial = false;
        var inspected = 0;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in ViewsFor(hive, Environment.Is64BitOperatingSystem))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey, writable: false);
                if (uninstall == null) continue;
                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++inspected > MaxEntries) { partial = true; break; }
                    try
                    {
                        using var entry = uninstall.OpenSubKey(keyName, writable: false);
                        if (entry == null) { partial = true; continue; }
                        var app = ReadEntry(name => entry.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
                            hive == RegistryHive.LocalMachine ? "Machine registration" : "Current Windows account registration");
                        if (app != null)
                        {
                            app = Identify(app, RegistrationIdentity(hive, view, keyName), hive == RegistryHive.LocalMachine,
                                keyName, entry.GetValue("WindowsInstaller"), entry.GetValue("NoRemove"));
                            // MSI registrations never fall through to a registry-supplied executable.
                            if (entry.GetValue("WindowsInstaller") is not 1 && !app.CanUninstall)
                            {
                                var command = entry.GetValue("UninstallString", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                                var noRemove = entry.GetValue("NoRemove");
                                if (app.Id == verifyAppId)
                                    app = RegisteredAppUninstaller.Identify(app, command, noRemove, VerifiedUninstallExecutable.Inspect, NativeUninstallRunner.DesktopUser);
                                else if (RegisteredAppUninstaller.Parse(app, command, noRemove) != null)
                                    app = app with { CanUninstall = true, UninstallMethod = "registered_exe", UninstallReason = "An official uninstall option is registered. RigMD must verify it before offering removal." };
                            }
                            apps.Add(app);
                        }
                    }
                    catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
                    { partial = true; }
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
            { partial = true; }
        }
        var store = includeStore ? StoreAppInventory.Capture(cancellationToken) : new StoreInventorySnapshot([], false);
        var totalAvailable = apps.Count + store.Apps.Count;
        apps.AddRange(store.Apps.Take(Math.Max(0, MaxEntries - apps.Count)));
        partial |= store.Partial || totalAvailable > MaxEntries;
        var ordered = apps.Distinct().OrderByDescending(app => app.EstimatedBytes ?? -1)
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(DateTimeOffset.UtcNow, ordered, partial,
            (includeStore ? "Read from this PC’s Windows desktop registrations and current-account Windows packages. " : "Desktop apps read from this PC. Store apps are checked separately. ") + "No sample app list is included. Desktop sizes are installer estimates, not measured disk use or guaranteed savings. Portable apps, app data and some registrations may be missing. Usage history and removal safety are unknown; registrations do not establish which drive holds the data.");
    }
}
