using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public sealed class WindowsAppUninstallService : IAppUninstallService
{
    private readonly TimeProvider _clock;
    private readonly IWindowsAppUninstallPlatform _platform;
    private readonly Dictionary<Guid, PendingReview> _reviews = new();
    private int _uninstalling;

    public WindowsAppUninstallService(TimeProvider clock) : this(clock, new WindowsAppUninstallPlatform()) { }

    internal WindowsAppUninstallService(TimeProvider clock, IWindowsAppUninstallPlatform platform)
    {
        _clock = clock;
        _platform = platform;
    }

    public async Task<AppUninstallReview> ReviewAsync(string appId, CancellationToken cancellationToken)
    {
        if (_platform.Inspect(appId) is not { } target)
            return new(null, "RigMD could not verify this installation. Use Manage in Windows.");

        string? winGet;
        try { winGet = await _platform.FindWinGetAsync(cancellationToken); }
        catch (Exception ex) when (IsLaunchFailure(ex)) { winGet = null; }
        if (winGet is null)
            return new(null, "A compatible WinGet installation is unavailable. Use Manage in Windows.");

        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.GetUtcNow();
        lock (_reviews)
        {
            foreach (var expiredId in _reviews.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
                _reviews.Remove(expiredId);
            if (_reviews.Count >= 32) return new(null, "Too many pending confirmations. Try again shortly.");
            var id = Guid.NewGuid();
            _reviews.Add(id, new(target, winGet, now.AddMinutes(3)));
            return new(id, "WinGet will open the app’s uninstaller. This installation is shared by everyone on this PC.",
                target.Name, target.Version, MachineMsiRegistration.FormatCode(target.ProductCode));
        }
    }

    public async Task<AppUninstallResult> UninstallAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _uninstalling, 1, 0) != 0)
            return new("busy", "An uninstall is already running. Finish it before trying again.");

        try
        {
            PendingReview? selected;
            lock (_reviews) _reviews.Remove(approvalId, out selected);
            if (selected is null || selected.Expires <= _clock.GetUtcNow())
                return new("unavailable", "This confirmation expired or was already used. Review the app again.");

            var target = selected.Target;
            var code = MachineMsiRegistration.FormatCode(target.ProductCode);
            if (_platform.Inspect(target.AppId) != target)
                return new("unavailable", "This app's installation changed. Refresh the list and review it again.", target.Name, code);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Once started, the interactive uninstaller belongs to the user. A lost HTTP request must not stop it.
                var exit = await _platform.RunUninstallAsync(selected.WinGetPath, target.ProductCode);
                var (status, message) = InterpretResult(exit, exit == 0 && _platform.IsGone(target.ProductCode));
                return new(status, message, target.Name, code, $"0x{unchecked((uint)exit):X8}");
            }
            catch (Exception ex) when (IsLaunchFailure(ex))
            {
                return new("failed", "RigMD could not complete the WinGet request. Check the app in Windows Settings.",
                    target.Name, code);
            }
        }
        finally { Volatile.Write(ref _uninstalling, 0); }
    }

    internal static (string Status, string Message) InterpretResult(int exit, bool gone) => unchecked((uint)exit) switch
    {
        0 when gone => ("removed", "Windows Installer confirmed the app was removed."),
        0 => ("unverified", "WinGet finished, but RigMD could not verify removal. Check again in Windows Settings."),
        1602 or 1223 or 0x800704C7 or 0x80070642 or 0x80004004 => ("cancelled", "The uninstall was cancelled."),
        1641 or 3010 or 0x80070669 or 0x80070BC2 => ("restart-required", "Windows reported that a restart is required."),
        0x8A150014 or 0x8A150016 => ("unavailable", "WinGet could not identify one installed match. Use Manage in Windows."),
        _ => ("failed", "WinGet reported that the uninstall did not complete. Check Windows Settings for more information.")
    };

    private static bool IsLaunchFailure(Exception ex) =>
        ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or SecurityException or TimeoutException;

    private sealed record PendingReview(MachineMsiApp Target, string WinGetPath, DateTimeOffset Expires);
}

internal interface IWindowsAppUninstallPlatform
{
    MachineMsiApp? Inspect(string appId);
    Task<string?> FindWinGetAsync(CancellationToken cancellationToken);
    Task<int> RunUninstallAsync(string path, Guid productCode);
    bool IsGone(Guid productCode);
}

internal sealed class WindowsAppUninstallPlatform : IWindowsAppUninstallPlatform
{
    internal const string OfficialAppInstallerPredicate = """
        $_.PackageFamilyName -eq 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -and
        $_.SignatureKind -in @('Store','System') -and -not $_.IsDevelopmentMode
        """;
    private const string FindAppInstaller = $$"""
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $ErrorActionPreference = 'Stop'
        $packages = @(Get-AppxPackage -Name Microsoft.DesktopAppInstaller -PackageTypeFilter Main | Where-Object {
            {{OfficialAppInstallerPredicate}}
        })
        if ($packages.Count -eq 1) { [Console]::WriteLine($packages[0].InstallLocation) }
        """;

    public MachineMsiApp? Inspect(string appId) => MachineMsiRegistration.Inspect(appId);
    public bool IsGone(Guid productCode) => MachineMsiRegistration.IsGone(productCode);

    public async Task<string?> FindWinGetAsync(CancellationToken cancellationToken)
    {
        var location = await WindowsSoftwareInventory.RunInventoryCommandAsync(
            Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-Command", FindAppInstaller], System.Text.Encoding.UTF8, cancellationToken);
        if (WindowsSoftwareInventory.NormalizeInstallPath(location) is not { } directory) return null;
        var executable = Path.Combine(directory, "winget.exe");
        if (!File.Exists(executable)) return null;
        var help = await WindowsSoftwareInventory.RunInventoryCommandAsync(executable, ["uninstall", "--help"],
            null, cancellationToken);
        return help.Contains("--product-code", StringComparison.Ordinal) && help.Contains("--scope", StringComparison.Ordinal)
            && help.Contains("--interactive", StringComparison.Ordinal) && help.Contains("--disable-interactivity", StringComparison.Ordinal)
            ? executable : null;
    }

    internal static string[] BuildArguments(Guid productCode) =>
        ["uninstall", "--product-code", MachineMsiRegistration.FormatCode(productCode), "--scope", "machine",
            "--exact", "--interactive", "--disable-interactivity", "--source", "winget"];

    public async Task<int> RunUninstallAsync(string path, Guid productCode)
    {
        var options = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        // The local desktop credential is for RigMD, not a vendor's uninstaller.
        options.Environment.Remove("RIGMD_LOCAL_API_TOKEN");
        foreach (var argument in BuildArguments(productCode)) options.ArgumentList.Add(argument);
        using var process = Process.Start(options) ?? throw new InvalidOperationException("WinGet could not be started.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
