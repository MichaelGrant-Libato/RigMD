using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Api.Services;

public sealed class UninstallTrackingException(Exception inner) : Exception("Uninstaller tracking was lost; further system actions must remain blocked.", inner);

public static class NativeUninstallRunner
{
    public static bool AllowedIdentity(string? current, string? desktop, uint integrity, int currentSession, int desktopSession) =>
        current != null && current == desktop && integrity >= 0x2000 && integrity < 0x3000 && currentSession == desktopSession;

    public static string? DesktopUser()
    {
        try { using var token = DesktopToken(); using var identity = new WindowsIdentity(token.DangerousGetHandle()); return identity.User?.Value; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    private static SafeAccessTokenHandle DesktopToken()
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("A normal Windows desktop is required.");
        _ = GetWindowThreadProcessId(shell, out var id);
        using var process = OpenProcess(0x1000, false, id);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var name = new StringBuilder(32768); uint length = (uint)name.Capacity;
        if (!QueryFullProcessImageName(process, 0, name, ref length) || !name.ToString().Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The desktop process could not be verified.");
        if (!OpenProcessToken(process, 0xB, out var original)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (original)
        using (var current = WindowsIdentity.GetCurrent())
        using (var desktop = new WindowsIdentity(original.DangerousGetHandle()))
        using (var desktopProcess = Process.GetProcessById(checked((int)id)))
        using (var currentProcess = Process.GetCurrentProcess())
        {
            _ = GetTokenInformation(original, 25, IntPtr.Zero, 0, out var size);
            if (size <= 0 || size > 65536) throw new InvalidOperationException("Desktop integrity could not be verified.");
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(original, 25, buffer, size, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var sid = Marshal.ReadIntPtr(buffer); var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) throw new InvalidOperationException("Desktop integrity could not be verified.");
                var integrity = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))));
                if (!AllowedIdentity(current.User?.Value, desktop.User?.Value, integrity, currentProcess.SessionId, desktopProcess.SessionId))
                    throw new InvalidOperationException("The same account and normal desktop are required; no elevated fallback is used.");
                if (!DuplicateTokenEx(original, 0x02000000, IntPtr.Zero, 2, 1, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return token;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    public static async Task<int> RunAsync(RegisteredUninstallTarget target)
    {
        if (!RegisteredAppUninstaller.ValidTarget(target)) throw new InvalidOperationException("Invalid approved uninstall target.");
        using var verified = VerifiedUninstallExecutable.Open(target.Executable);
        if (verified.Sha256 != target.Sha256 || verified.Signer != target.Signer) throw new InvalidOperationException("The uninstaller changed. Review a fresh preview.");
        return await RunHelperAsync(target.UserSid, RegisteredUninstallHelper.Switch, RegisteredUninstallHelper.RequestVariable, JsonSerializer.Serialize(target), verified.Dispose);
    }

    internal static async Task<int> RunHelperAsync(string userSid, string helperSwitch, string variable, string request, Action? onStarted = null)
    {
        if (helperSwitch != RegisteredUninstallHelper.Switch && helperSwitch != StorageCleanupWorker.Switch)
            throw new InvalidOperationException("Unsupported helper operation.");
        using var token = DesktopToken();
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        if (identity.User?.Value != userSid) throw new InvalidOperationException("The desktop account changed.");
        using var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var host = Environment.ProcessPath;
        if (host == null || Path.GetFileName(host) != "RigMD.Api.exe" || !Path.IsPathFullyQualified(host))
            throw new InvalidOperationException("The RigMD application host is required for official uninstaller prompts.");
        host = Path.Combine(Path.GetDirectoryName(host)!, "RigMD.Uninstall.exe");
        if (!File.Exists(host)) throw new InvalidOperationException("The uninstall helper is missing. Rebuild or reinstall RigMD before reviewing removal.");
        if (!CreateEnvironmentBlock(out var environment, token, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr helperEnvironment = IntPtr.Zero;
        NativeProcess info;
        try
        {
            var entries = new List<string>(); var offset = 0;
            while (offset < 32768)
            {
                var entry = Marshal.PtrToStringUni(environment + offset * 2) ?? "";
                if (entry.Length == 0) break;
                offset += entry.Length + 1;
                if (!entry.StartsWith(variable + "=", StringComparison.OrdinalIgnoreCase) && !entry.StartsWith("RIGMD_LOCAL_API_TOKEN=", StringComparison.OrdinalIgnoreCase)) entries.Add(entry);
            }
            if (offset >= 32768) throw new InvalidOperationException("Desktop environment could not be read.");
            if (request.Length > 24000) throw new InvalidOperationException("The selected cleanup request is too large.");
            entries.Add(variable + "=" + request);
            helperEnvironment = Marshal.StringToHGlobalUni(string.Join('\0', entries) + "\0\0");
            var start = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
            var command = new StringBuilder($"\"{host}\" {helperSwitch}");
            if (command.Length > 1023) throw new InvalidOperationException("The application host path is too long.");
            // Suspended until tracking is attached; no inherited administrator token, shell, profile script or password.
            if (!CreateProcessWithToken(token, 1, host, command, 0x400 | 0x4 | 0x01000000, helperEnvironment, AppContext.BaseDirectory, ref start, out info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { if (helperEnvironment != IntPtr.Zero) Marshal.FreeHGlobal(helperEnvironment); _ = DestroyEnvironmentBlock(environment); }
        using var process = new SafeProcessHandle(info.Process, true);
        using var thread = new SafeFileHandle(info.Thread, true);
        if (!AssignProcessToJobObject(job, process)) { var error = Marshal.GetLastWin32Error(); _ = TerminateProcess(process, 1); throw new Win32Exception(error); }
        if (ResumeThread(thread) == uint.MaxValue) { var error = Marshal.GetLastWin32Error(); _ = TerminateProcess(process, 1); throw new Win32Exception(error); }
        onStarted?.Invoke();
        try
        {
            // Do not terminate a running uninstaller or release the gate while its tracked descendants remain.
            while (true)
            {
                if (!QueryInformationJobObject(job, 1, out var accounting, Marshal.SizeOf<JobAccounting>(), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (accounting.Active == 0) break;
                await Task.Delay(500);
            }
            if (!GetExitCodeProcess(process, out var code) || code == 259) throw new InvalidOperationException("Uninstaller result unavailable.");
            if (code == RegisteredUninstallHelper.TrackingLostCode) throw new InvalidOperationException("The official uninstaller could not be tracked.");
            if ((code & 0x80000000) != 0) throw new InvalidOperationException("The helper or uninstaller ended abnormally; completion is uncertain.");
            return unchecked((int)code);
        }
        catch (Exception error) { throw new UninstallTrackingException(error); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int Size; public string? Reserved, Desktop, Title; public uint X, Y, Width, Height, CountX, CountY, Fill, Flags; public ushort Show, ReservedLength; public IntPtr ReservedBytes, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeProcess { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct JobAccounting { public long User, Kernel, PeriodUser, PeriodKernel; public uint Faults, Total, Active, Terminated; }
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint length);
    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, IntPtr buffer, int size, out int required);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr security, int impersonation, int type, out SafeAccessTokenHandle copy);
    [DllImport("userenv.dll", ExactSpelling = true, SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);
    [DllImport("userenv.dll", ExactSpelling = true)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithTokenW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern bool CreateProcessWithToken(SafeAccessTokenHandle token, uint logon, string application, StringBuilder command, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out NativeProcess process);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int type, out JobAccounting data, int length, IntPtr returned);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint code);
}
