using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace RigMD.Api.Services;

// Runs as the same desktop user, never as the elevated API. No web server starts in this mode.
// Windows ShellExecute handles the vendor executable's manifest and any required UAC prompt.
public static class RegisteredUninstallHelper
{
    public const string Switch = "--registered-uninstall-helper";
    public const string RequestVariable = "RIGMD_REGISTERED_UNINSTALL_REQUEST";
    public const int TrackingLostCode = 0x6FFFFFFE;

    public static async Task<int> RunFromEnvironmentAsync()
    {
        var request = Environment.GetEnvironmentVariable(RequestVariable);
        Environment.SetEnvironmentVariable(RequestVariable, null);
        var launched = false;
        try
        {
            if (request == null || request.Length > 16384) return 87;
            var target = JsonSerializer.Deserialize<RegisteredUninstallTarget>(request);
            if (target == null || !RegisteredAppUninstaller.ValidTarget(target)) return 87;
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ||
                identity.User?.Value != target.UserSid || NativeUninstallRunner.DesktopUser() != target.UserSid) return 5;
            using var verified = VerifiedUninstallExecutable.Open(target.Executable);
            if (verified.Sha256 != target.Sha256 || verified.Signer != target.Signer) return 13;
            var start = new ProcessStartInfo
            {
                FileName = target.Executable, Arguments = target.Arguments,
                WorkingDirectory = Path.GetDirectoryName(target.Executable)!,
                UseShellExecute = true, Verb = "open"
            };
            using var process = Process.Start(start);
            // A successful ShellExecute without a handle cannot be treated as a failed launch or retried.
            launched = true;
            if (process == null) return TrackingLostCode;
            verified.Dispose();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception error) when (!launched) { return error.NativeErrorCode; }
        catch (Exception) { return launched ? TrackingLostCode : 13; }
    }
}
