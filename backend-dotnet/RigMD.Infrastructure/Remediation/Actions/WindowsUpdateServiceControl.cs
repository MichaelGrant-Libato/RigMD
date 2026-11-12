using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Infrastructure.Remediation.Actions;

internal enum UpdateServiceState { Unknown, Stopped, Running }

internal interface IUpdateServiceControl
{
    UpdateServiceState ReadState();
    Task<bool> StopAsync(CancellationToken cancellation);
    Task<bool> RestoreAsync(UpdateServiceState original);
}

// Uses the fixed Windows service identity; no shell, executable lookup or user-supplied command.
internal sealed class WindowsUpdateServiceControl : IUpdateServiceControl
{
    public UpdateServiceState ReadState()
    {
        using var service = Open(4);
        return State(service);
    }

    public Task<bool> StopAsync(CancellationToken cancellation) => TransitionAsync(false, cancellation);

    public async Task<bool> RestoreAsync(UpdateServiceState original)
    {
        if (original is not (UpdateServiceState.Running or UpdateServiceState.Stopped)) return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        return await TransitionAsync(original == UpdateServiceState.Running, timeout.Token);
    }

    private static async Task<bool> TransitionAsync(bool start, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var service = Open(4 | (start ? 0x10u : 0x20u));
        var desired = start ? UpdateServiceState.Running : UpdateServiceState.Stopped;
        // A canceled stop may still be pending in Windows. Let it settle before restoring.
        var watch = Stopwatch.StartNew();
        var current = State(service);
        while (current == UpdateServiceState.Unknown && watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(200, cancellation);
            current = State(service);
        }
        if (current == desired) return true;
        if (current == UpdateServiceState.Unknown) return false;
        var accepted = start ? StartService(service, 0, IntPtr.Zero) : ControlService(service, 1, out _);
        if (!accepted && Marshal.GetLastWin32Error() != (start ? 1056 : 1062)) return false;
        watch.Restart();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            cancellation.ThrowIfCancellationRequested();
            if (State(service) == desired) return true;
            await Task.Delay(200, cancellation);
        }
        return State(service) == desired;
    }

    private static SafeServiceHandle Open(uint access)
    {
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var service = OpenService(manager, "wuauserv", access);
        if (service.IsInvalid) { var code = Marshal.GetLastWin32Error(); service.Dispose(); throw new Win32Exception(code); }
        return service;
    }

    private static UpdateServiceState State(SafeServiceHandle service)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, (uint)Marshal.SizeOf<ServiceStatusProcess>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return status.CurrentState switch { 1 => UpdateServiceState.Stopped, 4 => UpdateServiceState.Running, _ => UpdateServiceState.Unknown };
    }

    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus
    { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatusProcess
    { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool QueryServiceStatusEx(SafeServiceHandle service, int level, out ServiceStatusProcess status, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ControlService(SafeServiceHandle service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool StartService(SafeServiceHandle service, uint count, IntPtr arguments);
    [DllImport("advapi32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CloseServiceHandle(IntPtr service);
}
