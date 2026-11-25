using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace RigMD.Infrastructure.Windows;

internal sealed record MachineMsiApp(string AppId, Guid ProductCode, string Name, string Version, string InstallerVersion);

internal static class MachineMsiRegistration
{
    internal const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    internal const string SettingsReason = "Use Windows Settings for this app.";
    private const uint MachineContext = 4;
    private const uint UnknownProduct = 1605;

    internal static bool TryParse(string? appId, out RegistryView view, out Guid productCode)
    {
        view = RegistryView.Default;
        productCode = Guid.Empty;
        var parts = appId?.Split('/');
        if (parts is not ["registry", "LocalMachine", _, _]) return false;
        view = parts[2] switch
        {
            "Registry64" => RegistryView.Registry64,
            "Registry32" => RegistryView.Registry32,
            _ => RegistryView.Default
        };
        return view != RegistryView.Default && Guid.TryParseExact(parts[3], "B", out productCode)
            && parts[3].Length == 38 && productCode != Guid.Empty;
    }

    internal static (string Availability, string? Reason) Availability(RegistryKey key, string appId, string name)
    {
        if (Number(key, "NoRemove") == 1)
            return ("protected", "Removal is disabled by the installer.");
        if (IsRigMdDependency(name)) return ("protected", "RigMD uses this component.");
        if (!TryParse(appId, out _, out _) || Number(key, "WindowsInstaller") != 1)
            return ("settings", SettingsReason);
        return Inspect(appId) is not null
            ? ("review", null) : ("settings", "RigMD could not verify this Windows Installer registration.");
    }

    internal static MachineMsiApp? Inspect(string appId)
    {
        if (!TryParse(appId, out var view, out var productCode)) return null;
        try
        {
            var code = FormatCode(productCode);
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hive.OpenSubKey(UninstallKey + "\\" + code);
            if (key is null || Number(key, "WindowsInstaller") != 1 || Number(key, "NoRemove") == 1
                || Number(key, "SystemComponent") == 1 || Text(key, "ParentKeyName") is not null
                || Text(key, "ReleaseType") is not null
                || Text(key, "DisplayName") is not { } name || Text(key, "DisplayVersion") is not { } version
                || IsRigMdDependency(name)) return null;

            // If distinct HKLM registry views disagree about this product, do not choose one.
            var other = view == RegistryView.Registry64 ? RegistryView.Registry32 : RegistryView.Registry64;
            if (Environment.Is64BitOperatingSystem && HasRecord(other, productCode)) return null;
            if (ReadProductInfo(code, "State").Value != "5" || ReadProductInfo(code, "VersionString").Value is not { } nativeVersion
                || ReadProductInfo(code, "InstalledProductName").Value is not { } nativeName
                || !string.Equals(name, nativeName, StringComparison.OrdinalIgnoreCase)) return null;
            return new(appId, productCode, name, version, nativeVersion);
        }
        catch (Exception ex) when (IsReadFailure(ex)) { return null; }
    }

    internal static bool IsGone(Guid productCode)
    {
        try
        {
            return ReadProductInfo(FormatCode(productCode), "State").Status == UnknownProduct
                && !HasRecord(RegistryView.Registry64, productCode) && !HasRecord(RegistryView.Registry32, productCode);
        }
        catch (Exception ex) when (IsReadFailure(ex)) { return false; }
    }

    private static bool HasRecord(RegistryView view, Guid productCode)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = hive.OpenSubKey(UninstallKey + "\\" + FormatCode(productCode));
        return key is not null;
    }

    private static (uint Status, string? Value) ReadProductInfo(string code, string property)
    {
        var buffer = new StringBuilder(512);
        var length = (uint)buffer.Capacity;
        var status = MsiGetProductInfoExW(code, null, MachineContext, property, buffer, ref length);
        return (status, status == 0 && buffer.Length > 0 ? buffer.ToString() : null);
    }

    internal static string FormatCode(Guid code) => code.ToString("B").ToUpperInvariant();
    private static long? Number(RegistryKey key, string name) =>
        long.TryParse(Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture), out var value) ? value : null;
    private static string? Text(RegistryKey key, string name) =>
        key.GetValue(name) is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    private static bool IsRigMdDependency(string name) =>
        name.Equals("RigMD", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PawnIO", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Microsoft Edge WebView2 Runtime", StringComparison.OrdinalIgnoreCase);
    private static bool IsReadFailure(Exception ex) => ex is UnauthorizedAccessException or IOException or SecurityException;

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint MsiGetProductInfoExW(string productCode, string? userSid, uint context,
        string property, StringBuilder value, ref uint valueLength);
}
