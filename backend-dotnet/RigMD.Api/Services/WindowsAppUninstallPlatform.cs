using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace RigMD.Api.Services;

public sealed record InstallerEvidence(string Status, string? Name = null, string? Publisher = null, string? Version = null);
public sealed record StorageVolumeReading(string Drive, long FreeBytes, long TotalBytes);

public interface IAppUninstallPlatform
{
    bool IsAdministrator { get; }
    StorageAppSnapshot Capture();
    StorageAppEntry Resolve(string appId)
    {
        var matches = Capture().Apps.Where(app => app.Id == appId).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException("The app registration changed. Update the app list.");
    }
    InstallerEvidence Inspect(string productCode);
    StorageVolumeReading[] ReadVolumes();
    Task<int> UninstallAsync(string productCode);
    InstallerEvidence Inspect(StorageAppEntry app) => app.ProductCode == null ? new("unsupported") : Inspect(app.ProductCode);
    Task<int> UninstallAsync(StorageAppEntry app) => app.ProductCode == null
        ? throw new InvalidOperationException("Unsupported uninstaller.") : UninstallAsync(app.ProductCode);
}

public sealed class WindowsAppUninstallPlatform : IAppUninstallPlatform
{
    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public StorageAppSnapshot Capture() => StorageAppInventory.Capture(CancellationToken.None);
    public StorageAppEntry Resolve(string appId)
    {
        var desktop = StorageAppInventory.Capture(CancellationToken.None, appId, includeStore: false);
        var matches = desktop.Apps.Where(app => app.Id == appId).ToArray();
        if (matches.Length == 0) matches = StorageAppInventory.Capture(CancellationToken.None, appId).Apps.Where(app => app.Id == appId).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException("The app registration changed. Update the app list.");
    }

    public InstallerEvidence Inspect(StorageAppEntry app)
    {
        if (app.ProductCode != null) return Inspect(app.ProductCode);
        if (app.SourceType == "store")
        {
            var packages = WindowsPackageInventory.Capture(CancellationToken.None);
            return InspectRegistration(app, new(DateTimeOffset.UtcNow, packages.Apps, packages.Partial, "Package verification"));
        }
        var snapshot = StorageAppInventory.Capture(CancellationToken.None, includeStore: false);
        return InspectRegistration(app, snapshot);
    }

    public static InstallerEvidence InspectRegistration(StorageAppEntry app, StorageAppSnapshot snapshot)
    {
        var matches = snapshot.Apps.Where(candidate => candidate.Id == app.Id).ToArray();
        if (matches.Length == 0) return new(snapshot.Partial ? "unknown" : "absent");
        if (matches.Length != 1) return new("unknown");
        var registered = matches[0];
        return registered.Name == app.Name && registered.Publisher == app.Publisher && registered.Version == app.Version
            ? new("installed", registered.Name, registered.Publisher, registered.Version) : new("unknown");
    }

    public Task<int> UninstallAsync(StorageAppEntry app) => app.ProductCode != null ? UninstallAsync(app.ProductCode)
        : app.RegisteredTarget != null ? NativeUninstallRunner.RunAsync(app.RegisteredTarget)
        : app.PackageTarget != null ? WindowsPackageInventory.RemoveAsync(app)
        : throw new InvalidOperationException("Unsupported uninstaller.");

    public InstallerEvidence Inspect(string productCode)
    {
        if (!Guid.TryParseExact(productCode, "B", out _)) return new("unknown");
        var machine = Read(productCode, 4, "State");
        if (machine.Code == 1605) return new("absent");
        if (machine.Code != 0 || machine.Value != "5") return new("unknown");
        if (Read(productCode, 1, "State").Code != 1605 || Read(productCode, 2, "State").Code != 1605)
            return new("unknown");
        var name = Read(productCode, 4, "InstalledProductName");
        var publisher = Read(productCode, 4, "Publisher");
        var version = Read(productCode, 4, "VersionString");
        return name.Code == 0 && publisher.Code == 0 && version.Code == 0
            ? new("installed", name.Value, publisher.Value, version.Value) : new("unknown");
    }

    private static (uint Code, string Value) Read(string productCode, uint context, string property)
    {
        var text = new StringBuilder(2048);
        uint size = 2048;
        var code = MsiGetProductInfoEx(productCode, null, context, property, text, ref size);
        return (code, code == 0 ? text.ToString() : "");
    }

    public StorageVolumeReading[] ReadVolumes()
    {
        var readings = new List<StorageVolumeReading>();
        foreach (var drive in DriveInfo.GetDrives().Take(32))
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    readings.Add(new(drive.Name, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return readings.ToArray();
    }

    public async Task<int> UninstallAsync(string productCode)
    {
        if (!Guid.TryParseExact(productCode, "B", out _)) throw new InvalidOperationException("Invalid Windows Installer identity.");
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory
        };
        foreach (var argument in new[] { "/x", productCode, "/qn", "/norestart", "MSIRESTARTMANAGERCONTROL=Disable" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer did not start.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint MsiGetProductInfoEx(string productCode, string? userSid, uint context, string property, StringBuilder value, ref uint length);
}
