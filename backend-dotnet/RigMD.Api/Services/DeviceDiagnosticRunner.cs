using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace RigMD.Api.Services;

public sealed class DeviceDiagnosticRunner
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    public async Task<T?> TryRunAsync<T>(string check, Func<CancellationToken, Task<T>> read, CancellationToken token)
        where T : class
    {
        if (!_running.TryAdd(check, 0)) return null;
        try
        {
            token.ThrowIfCancellationRequested();
            return await read(token);
        }
        finally { _running.TryRemove(check, out _); }
    }

    internal static Task<(int ExitCode, string Output)> RunPowerShellAsync(string script, CancellationToken token)
    {
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var command = "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + script;
        return RunAsync(shell, ["-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(command))], token);
    }

    internal static async Task<(int ExitCode, string Output)> RunAsync(string executable, string[] arguments,
        CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var until = timeout.Token;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows utility unavailable.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(until);
            var errors = process.StandardError.ReadToEndAsync(until);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(until));
            return (process.ExitCode, await output);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
