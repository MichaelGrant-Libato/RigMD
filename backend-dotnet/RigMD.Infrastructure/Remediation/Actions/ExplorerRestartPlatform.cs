using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed record ExplorerContext(int SessionId, string UserSid, bool Elevated);
internal sealed record ExplorerIdentity(int Pid, long StartedUtcTicks, int SessionId, string UserSid, string Executable);
internal interface IExplorerRestartPlatform
{
    ExplorerContext Context();
    ExplorerIdentity? ReadShell();
    bool IsRunning(ExplorerIdentity identity);
    Task<bool> StopAsync(ExplorerIdentity identity, CancellationToken cancellation);
    void StartShell();
    Task PauseAsync(CancellationToken cancellation);
}

internal sealed class WindowsExplorerRestartPlatform : IExplorerRestartPlatform
{
    private static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
    public ExplorerContext Context()
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        return new(process.SessionId, identity.User?.Value ?? "", new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
    }
    private static ExplorerIdentity Inspect(Process process)
    {
        if (!OpenProcessToken(process.SafeHandle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.SessionId, identity.User?.Value ?? "",
                process.MainModule?.FileName ?? "");
    }
    public ExplorerIdentity? ReadShell()
    {
        var window = GetShellWindow();
        if (window == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0) throw new InvalidOperationException("The desktop shell identity could not be read.");
        using var process = Process.GetProcessById(checked((int)pid));
        return Inspect(process);
    }
    public bool IsRunning(ExplorerIdentity identity)
    {
        try { using var process = Process.GetProcessById(identity.Pid); return !process.HasExited && Inspect(process) == identity; }
        catch (ArgumentException) { return false; }
    }
    public async Task<bool> StopAsync(ExplorerIdentity identity, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var current = Context();
        if (current.Elevated || !RestartExplorerAction.BelongsTo(identity, current) || ReadShell() != identity) return false;
        using var process = Process.GetProcessById(identity.Pid);
        if (Inspect(process) != identity) return false;
        process.Kill();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        return process.HasExited;
    }
    public void StartShell()
    {
        if (Context().Elevated) throw new InvalidOperationException("An elevated replacement shell is not allowed.");
        Process.Start(new ProcessStartInfo { FileName = ExplorerPath, UseShellExecute = true })?.Dispose();
    }
    public Task PauseAsync(CancellationToken cancellation) => Task.Delay(250, cancellation);
    [DllImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
