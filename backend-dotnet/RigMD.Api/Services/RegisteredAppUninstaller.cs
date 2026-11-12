using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RigMD.Api.Services;

// Server-only identity. The selected registration, full command, signer and bytes are approval-bound.
public sealed record RegisteredUninstallTarget(string Executable, string ArgumentsJson, string Sha256, string Signer, string UserSid)
{
    public string Arguments => string.Join(" ", JsonSerializer.Deserialize<string[]>(ArgumentsJson)!.Select(RegisteredAppUninstaller.Quote));
}

public static class RegisteredAppUninstaller
{
    public static bool LocalExecutable(string path) => Regex.IsMatch(path, @"\A[A-Za-z]:\\") &&
        Path.IsPathFullyQualified(path) && !path[2..].Contains(':') && !path.Contains('"') && !path.Any(char.IsControl) &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    private static bool SafeArgument(string argument) => argument.Length <= 512 && !argument.Any(char.IsControl) &&
        !argument.Any(character => "&|<>^%".Contains(character)) &&
        !Regex.IsMatch(argument, @"\A[-/]{1,2}(?:c|k|command|encodedcommand|enc|file|execute|exec|eval|script|install|repair|reinstall|force|force-uninstall)(?:[=:]|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        !Regex.IsMatch(argument, @"\.(?:ps1|psm1|bat|cmd|vbs|vbe|js|jse|wsf|wsh|hta|dll)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool UninstallPurpose(string path, string[] arguments) =>
        Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"unins|uninstall|uninst|remove", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        arguments.Any(argument => Regex.IsMatch(argument, @"\A(?:--?(?:uninstall|remove)(?:[-_][a-z0-9_-]+)?(?:=.*)?|/(?:uninstall|remove))\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public static bool PublisherMatches(string publisher, string signer)
    {
        static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var expected = Normalize(publisher);
        return expected.Length >= 3 && expected == Normalize(signer);
    }

    public static RegisteredUninstallTarget? Parse(StorageAppEntry app, string? command, object? noRemove)
    {
        if (StorageAppInventory.ProtectedApp(app.Name, app.Publisher) || noRemove is not (null or 0) || string.IsNullOrWhiteSpace(app.Publisher) ||
            string.IsNullOrWhiteSpace(command) || command.Length > 1023 || command.Any(char.IsControl) || command.Contains('%')) return null;
        var pointer = CommandLineToArgv(command, out var count);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            if (count is < 1 or > 33) return null;
            var parts = Enumerable.Range(0, count).Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size)) ?? "").ToArray();
            if (!Regex.IsMatch(parts[0], @"\A[A-Za-z]:[\\/]") || !parts[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            var path = Path.GetFullPath(parts[0].Replace('/', '\\'));
            var arguments = parts.Skip(1).ToArray();
            if (!LocalExecutable(path) || !arguments.All(SafeArgument) || !UninstallPurpose(path, arguments)) return null;
            return new(path, JsonSerializer.Serialize(arguments), "", "", "");
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        finally { _ = LocalFree(pointer); }
    }

    public static bool ValidTarget(RegisteredUninstallTarget target)
    {
        try
        {
            var arguments = JsonSerializer.Deserialize<string[]>(target.ArgumentsJson);
            return LocalExecutable(target.Executable) && Regex.IsMatch(target.Sha256, @"\A[0-9A-Fa-f]{64}\z") && !string.IsNullOrWhiteSpace(target.Signer) &&
                !string.IsNullOrWhiteSpace(target.UserSid) && arguments is { Length: <= 32 } &&
                arguments.All(argument => argument != null && SafeArgument(argument)) && UninstallPurpose(target.Executable, arguments) &&
                ($"\"{target.Executable}\" " + string.Join(" ", arguments.Select(Quote))).Length <= 1023;
        }
        catch (JsonException) { return false; }
    }

    public static string Quote(string argument)
    {
        var quoted = new StringBuilder("\""); var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character); slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }

    public static StorageAppEntry Identify(StorageAppEntry app, string? command, object? noRemove,
        Func<string, (string Sha256, string Signer)> verify, Func<string?> desktopUser)
    {
        app = app with { CanUninstall = false, RegisteredTarget = null, UninstallMethod = "none" };
        var target = Parse(app, command, noRemove);
        if (target == null) return app;
        try
        {
            var sid = desktopUser();
            if (sid == null) return app with { UninstallReason = "This app’s uninstaller requires the same account’s normal Windows desktop. No administrator fallback is used." };
            var verified = verify(target.Executable);
            if (!PublisherMatches(app.Publisher, verified.Signer))
                return app with { UninstallReason = "The uninstaller’s verified publisher does not match this app’s Windows registration. Use Windows to review it." };
            target = target with { Sha256 = verified.Sha256, Signer = verified.Signer, UserSid = sid };
            return ValidTarget(target) ? app with { CanUninstall = true, RegisteredTarget = target, UninstallMethod = "registered_exe",
                UninstallReason = "Windows-registered signed uninstaller; review and approval are required. The app may show its own prompts." } : app;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException or System.Security.Cryptography.CryptographicException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return app with { UninstallReason = "This app’s registered uninstaller could not be verified. Use Windows Installed apps to review it." }; }
    }

    [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr CommandLineToArgv(string command, out int count);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr LocalFree(IntPtr memory);
}
