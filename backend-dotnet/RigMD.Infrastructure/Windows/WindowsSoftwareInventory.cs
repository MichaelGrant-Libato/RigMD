using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public sealed partial class WindowsSoftwareInventory(IStorageProvider storage) : ISoftwareInventoryProvider
{
    private const string AppxQuery = $$"""
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $ErrorActionPreference = 'Stop'
        $names = @{}
        try {
            foreach ($entry in Get-StartApps) {
                if ($entry.AppID -notlike '*!*') { continue }
                $family = ($entry.AppID -split '!', 2)[0]
                if (-not $names.ContainsKey($family)) { $names[$family] = $entry.Name }
                elseif ($names[$family] -ne $entry.Name) { $names[$family] = $null }
            }
        } catch { }
        $packages = @(Get-AppxPackage -PackageTypeFilter Main | ForEach-Object {
            $name = $names[$_.PackageFamilyName]
            if (-not $name) { $name = $_.Name }
            @{
                Id = $_.PackageFullName; Name = $name; Publisher = $_.Publisher
                Version = $_.Version.ToString(); InstallPath = $_.InstallLocation
                NonRemovable = ($_.NonRemovable -eq $true)
                OfficialAppInstaller = ({{WindowsAppUninstallPlatform.OfficialAppInstallerPredicate}})
            }
        })
        ConvertTo-Json -InputObject $packages -Compress
        """;

    public async Task<InstalledAppInventory> ReadAppsAsync(CancellationToken cancellationToken)
    {
        var localVolumes = storage.GetAllDisks();
        var icons = new WindowsAppIconReader(localVolumes.Select(volume => volume.Drive));
        var entries = new List<InstalledApp>();
        var registrations = new HashSet<(RegistryHive, string, string, string?, string?, string?, long?)>();
        var partial = false;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(MachineMsiRegistration.UninstallKey);
                foreach (var keyName in uninstall?.GetSubKeyNames() ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = uninstall!.OpenSubKey(keyName);
                        if (key is null || RegistryNumber(key, "SystemComponent") == 1) continue;
                        var name = RegistryText(key, "DisplayName");
                        if (name is null || RegistryText(key, "ParentKeyName") is not null) continue;
                        var publisher = RegistryText(key, "Publisher");
                        var version = RegistryText(key, "DisplayVersion");
                        var path = NormalizeInstallPath(RegistryText(key, "InstallLocation"));
                        var kilobytes = RegistryNumber(key, "EstimatedSize");
                        var bytes = kilobytes is > 0 and <= (long.MaxValue / 1024) ? kilobytes * 1024 : null;
                        if (!registrations.Add((hive, keyName.ToUpperInvariant(), name, publisher, version, path, bytes)))
                            continue;

                        var source = hive == RegistryHive.LocalMachine ? "Desktop app · This PC" : "Desktop app · Current user";
                        var appId = $"registry/{hive}/{view}/{keyName}";
                        var (availability, reason) = MachineMsiRegistration.Availability(key, appId, name);
                        entries.Add(new(appId, name, publisher, version, path, bytes,
                            source, IconPng: icons.ReadDesktopIcon(RegistryText(key, "DisplayIcon")),
                            UninstallAvailability: availability, UninstallReason: reason));
                    }
                    catch (Exception ex) when (IsRegistryReadError(ex)) { partial = true; }
                }
            }
            catch (Exception ex) when (IsRegistryReadError(ex)) { partial = true; }
        }

        var winGetRegistrations = 0;
        try
        {
            var json = await RunInventoryCommandAsync(
                Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-Command", AppxQuery], Encoding.UTF8, cancellationToken);
            foreach (var package in JsonSerializer.Deserialize<AppPackage[]>(json) ?? [])
            {
                if (Clean(package.Name) is not { } name || Clean(package.Id) is not { } id) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var path = NormalizeInstallPath(package.InstallPath);
                if (package.OfficialAppInstaller && path is not null && File.Exists(Path.Combine(path, "winget.exe")))
                    winGetRegistrations++;
                entries.Add(new($"package/{id}", name, Clean(package.Publisher), Clean(package.Version),
                    path, null, "Windows package · Current user", IconPng: icons.ReadPackageIcon(path),
                    UninstallAvailability: package.NonRemovable ? "protected" : "settings",
                    UninstallReason: package.NonRemovable ? "Windows marks this package as non-removable." : MachineMsiRegistration.SettingsReason));
            }
        }
        catch (Exception ex) when (IsInventoryCommandError(ex) || ex is JsonException) { partial = true; }

        var volumes = localVolumes
            .Where(volume => volume.DiskIndex.HasValue)
            .ToDictionary(volume => volume.Drive, volume => volume.DiskIndex!.Value, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var app = entries[i];
            if (winGetRegistrations != 1 && app.UninstallAvailability == "review")
                app = app with { UninstallAvailability = "settings", UninstallReason = "WinGet is unavailable. Use Manage in Windows." };
            // Windows can mount a different volume under a directory on C:. Ask for the actual mount point.
            if (app.InstallPath is { } path && volumes.ContainsKey(Path.GetPathRoot(path)!)
                && ReadVolumeRoot(path) is { } root && volumes.TryGetValue(root, out var diskIndex))
            {
                app = app with { Volume = root.TrimEnd('\\').ToUpperInvariant(), DiskIndex = diskIndex };
            }
            entries[i] = app;
        }
        var drives = localVolumes.Where(volume => volume.DiskIndex.HasValue)
            .GroupBy(volume => volume.DiskIndex!.Value)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var letters = group.Select(volume => volume.Drive.TrimEnd('\\').ToUpperInvariant())
                    .Order(StringComparer.Ordinal).ToArray();
                double? freeGb = group.All(volume =>
                    double.IsFinite(volume.TotalGb) && double.IsFinite(volume.UsedGb)
                    && volume.UsedGb >= 0 && volume.UsedGb <= volume.TotalGb)
                    ? group.Sum(volume => volume.TotalGb - volume.UsedGb) : null;
                return new SoftwareInventoryDrive(group.Key, letters, freeGb);
            }).ToArray();
        return new(entries.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), partial, drives);
    }

    public async Task<SoftwareInventory<InstalledDriver>> ReadDriversAsync(CancellationToken cancellationToken)
    {
        var entries = new List<InstalledDriver>();
        var partial = false;
        foreach (var (wmiClass, properties) in new[]
        {
            ("Win32_PnPSignedDriver", "DeviceID, DeviceName, Description, DriverProviderName, DriverVersion, InfName, IsSigned, Signer"),
            ("Win32_SystemDriver", "Name, DisplayName, State, StartMode, PathName")
        })
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {properties} FROM {wmiClass}");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var results = searcher.Get();
                foreach (ManagementObject row in results)
                using (row)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (wmiClass == "Win32_PnPSignedDriver")
                    {
                        var id = Clean(row["DeviceID"]?.ToString());
                        var name = Clean(row["DeviceName"]?.ToString()) ?? Clean(row["Description"]?.ToString()) ?? id;
                        if (id is null || name is null) continue;
                        entries.Add(new($"device/{id}", name, Clean(row["DriverProviderName"]?.ToString()),
                            Clean(row["DriverVersion"]?.ToString()), "Device driver", Clean(row["InfName"]?.ToString()),
                            row["IsSigned"] switch { true => "Signed", false => "Unsigned", _ => null },
                            Clean(row["Signer"]?.ToString()) is { } signer ? $"Signer: {signer}" : null));
                    }
                    else
                    {
                        var id = Clean(row["Name"]?.ToString());
                        if (id is null) continue;
                        var path = Clean(row["PathName"]?.ToString());
                        var startMode = Clean(row["StartMode"]?.ToString());
                        entries.Add(new($"service/{id}", Clean(row["DisplayName"]?.ToString()) ?? id, null, null,
                            "Driver service", null, Clean(row["State"]?.ToString()),
                            $"Service: {id}\nStart mode: {startMode ?? "Unknown"}\nPath: {path ?? "Unknown"}"));
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
            {
                partial = true;
            }
        }

        try
        {
            var output = await RunInventoryCommandAsync(
                "pnputil.exe", ["/enum-drivers"], encoding: null, cancellationToken);
            entries.AddRange(ReadDriverStore(output));
        }
        catch (Exception ex) when (IsInventoryCommandError(ex)) { partial = true; }

        return new(entries.DistinctBy(driver => driver.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(driver => driver.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), partial);
    }

    internal static IEnumerable<InstalledDriver> ReadDriverStore(string output)
    {
        foreach (var block in DriverStoreSeparator().Split(output))
        {
            var match = PublishedInfLine().Match(block);
            if (!match.Success) continue;
            var details = block.Trim();
            var inf = match.Groups[1].Value;
            // Keep the original metadata, including localized labels when the UI cannot split out a field.
            var fields = details.Split('\n').Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToLookup(parts => parts[0].Trim(), parts => Clean(parts[1]), StringComparer.OrdinalIgnoreCase);
            yield return new($"store/{inf}", fields["Original Name"].FirstOrDefault() ?? inf,
                fields["Provider Name"].FirstOrDefault(), fields["Driver Version"].FirstOrDefault(),
                "Driver Store package", inf, null, details);
        }
    }

    [GeneratedRegex(@"^[^:\r\n]+:[\t ]*(oem\d+\.inf)[\t ]*\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PublishedInfLine();

    [GeneratedRegex(@"\r?\n(?:[\t ]*\r?\n)+")]
    private static partial Regex DriverStoreSeparator();

    internal static string? NormalizeInstallPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')).Replace('/', '\\');
        // Do not inspect UNC paths, device paths, or drive-relative paths from installer metadata.
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(2).Contains(':') || path.IndexOfAny(['"', '<', '>', '*', '?', '|']) >= 0
            || path.Any(char.IsControl))
            return null;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    private static string? ReadVolumeRoot(string path)
    {
        var buffer = new StringBuilder(512);
        return GetVolumePathNameW(path, buffer, (uint)buffer.Capacity) ? buffer.ToString() : null;
    }

    internal static async Task<string> RunInventoryCommandAsync(
        string executable, string[] arguments, Encoding? encoding, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new(Path.Combine(Environment.SystemDirectory, executable))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = encoding,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("Windows inventory could not start.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token));
            if (process.ExitCode != 0) throw new InvalidOperationException("Windows inventory command failed.");
            return await output;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Windows inventory timed out.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? RegistryText(RegistryKey key, string name) => Clean(key.GetValue(name) as string);
    private static long? RegistryNumber(RegistryKey key, string name) =>
        long.TryParse(Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture), out var value) ? value : null;
    private static bool IsRegistryReadError(Exception ex) => ex is UnauthorizedAccessException or SecurityException or IOException;
    private static bool IsInventoryCommandError(Exception ex) => ex is Win32Exception or InvalidOperationException or TimeoutException or IOException;

    private sealed record AppPackage(string? Id, string? Name, string? Publisher, string? Version, string? InstallPath,
        bool NonRemovable, bool OfficialAppInstaller);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePathName, uint bufferLength);
}
