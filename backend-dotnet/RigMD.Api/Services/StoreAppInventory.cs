using System.Diagnostics;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace RigMD.Api.Services;

public sealed record StoreInventorySnapshot(IReadOnlyList<StorageAppEntry> Apps, bool Partial);

/// <summary>Read-only Windows Store inventory. WMI does not establish user/removal scope, so no removal is offered.</summary>
public static class StoreAppInventory
{
    private static readonly object ReadLock = new();
    private static Task<StoreInventorySnapshot>? _read;
    public static StorageAppEntry? ReadEntry(Func<string, object?> read)
    {
        string Text(string name) => read(name) is string value ? new string(value.Where(character => !char.IsControl(character)).Take(512).ToArray()).Trim() : "";
        var identity = Text("ProgramId");
        var name = Text("Name");
        if (identity.Length == 0 || name.Length == 0) return null;
        return new(name, Text("Vendor"), Text("Version"), null, "Windows Store inventory; account/removal scope not verified")
        {
            Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("store:" + identity))),
            SourceType = "store", CanUninstall = false,
            UninstallReason = "Windows reports this Store app, but RigMD cannot verify its package-removal scope here. No removal is offered."
        };
    }

    public static StoreInventorySnapshot Capture(CancellationToken cancellationToken)
    {
        Task<StoreInventorySnapshot> read;
        lock (ReadLock)
        {
            // A stalled Windows provider cannot block desktop app results or accumulate background workers.
            if (_read == null || _read.IsCompleted) _read = Task.Run(() => CaptureCore(CancellationToken.None));
            read = _read;
        }
        try { return read.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).GetAwaiter().GetResult(); }
        catch (TimeoutException) { return new([], true); }
    }

    private static StoreInventorySnapshot CaptureCore(CancellationToken cancellationToken)
    {
        var packages = WindowsPackageInventory.Capture(cancellationToken);
        // Legacy inventory is display-only when Windows cannot establish package scope.
        if (packages.Apps.Count > 0 || !packages.Partial) return packages;
        var legacy = CaptureLegacy(cancellationToken);
        return legacy with { Partial = true };
    }

    private static StoreInventorySnapshot CaptureLegacy(CancellationToken cancellationToken)
    {
        var apps = new List<StorageAppEntry>();
        var partial = false;
        var watch = Stopwatch.StartNew();
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"),
                new ObjectQuery("SELECT ProgramId, Name, Vendor, Version FROM Win32_InstalledStoreProgram"),
                new System.Management.EnumerationOptions { ReturnImmediately = false, Rewindable = false, Timeout = TimeSpan.FromSeconds(3), BlockSize = 32 });
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (apps.Count >= 1024 || watch.Elapsed >= TimeSpan.FromSeconds(3)) { partial = true; break; }
                    var entry = ReadEntry(name => row[name]);
                    if (entry == null) partial = true; else apps.Add(entry);
                }
            }
        }
        catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.Security.SecurityException or InvalidOperationException)
        { partial = true; }
        return new(apps, partial);
    }
}
