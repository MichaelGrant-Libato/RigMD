using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed class ExplorerRestartPlan(ExplorerContext context, ExplorerIdentity original, DateTimeOffset captured)
{
    internal ExplorerContext Context { get; } = context;
    internal ExplorerIdentity Original { get; } = original;
    internal DateTimeOffset Captured { get; } = captured;
    private int _used;
    internal bool IsCurrent(DateTimeOffset now) => now >= Captured && now < Captured.AddMinutes(5) && Volatile.Read(ref _used) == 0;
    internal bool Consume(DateTimeOffset now) => IsCurrent(now) && Interlocked.CompareExchange(ref _used, 1, 0) == 0;
}

public class RestartExplorerAction(ILogger<RestartExplorerAction> logger)
{
    public ToolDryRunPreview Preview() => new() { ToolName = "restart_windows_explorer", CanExecute = false,
        RequiresUserConfirmation = true, WhatWillHappen = "A current-user desktop-shell preview and approval are required." };
    public Task<ExecutionResult> ExecuteAsync(Action<string>? progressReporter = null) => Task.FromResult(new ExecutionResult
    { Success = false, Summary = "An approved desktop-shell identity is required. No shell was stopped." });

    internal static bool BelongsTo(ExplorerIdentity shell, ExplorerContext context) =>
        !context.Elevated && context.UserSid.Length > 0 && shell.UserSid == context.UserSid && shell.SessionId == context.SessionId &&
        shell.Pid > 0 && shell.StartedUtcTicks > 0 && Path.GetFullPath(shell.Executable).Equals(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase);

    internal async Task<ExecutionResult> ExecutePlanAsync(ExplorerRestartPlan plan, IExplorerRestartPlatform platform,
        TimeProvider clock, CancellationToken cancellation, Action<string>? report = null)
    {
        var stopAttempted = false;
        var stopVerified = false;
        var success = false;
        ExplorerIdentity? after = null;
        string? failure = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!plan.Consume(clock.GetUtcNow()) || platform.Context() != plan.Context || !BelongsTo(plan.Original, plan.Context) ||
                platform.ReadShell() != plan.Original)
                throw new InvalidOperationException("The approved shell or Windows session changed. Review a fresh preview. No shell was stopped.");
            stopAttempted = true;
            try { report?.Invoke("Restarting the reviewed shell in your Windows session..."); } catch { }
            stopVerified = await platform.StopAsync(plan.Original, cancellation);
            if (!stopVerified || platform.IsRunning(plan.Original))
                failure = "The original desktop shell could not be confirmed stopped. A restart is not verified.";
        }
        catch (OperationCanceledException) { failure = "The restart request was canceled. Any shell recovery is checked separately."; }
        catch (Exception error)
        {
            logger.LogWarning("Explorer restart stopped; error type {ErrorType}.", error.GetType().Name);
            failure = error is InvalidOperationException ? error.Message : "The desktop restart could not be verified.";
        }
        finally
        {
            // Recover after a stop that was accepted even if the caller canceled. Never stop another process here.
            if (stopAttempted)
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                try
                {
                    var launched = false;
                    for (var attempt = 0; attempt < 40; attempt++)
                    {
                        recovery.Token.ThrowIfCancellationRequested();
                        if (platform.Context() != plan.Context) break;
                        after = platform.ReadShell();
                        if (after != null)
                        {
                            if (after != plan.Original && BelongsTo(after, plan.Context) && !platform.IsRunning(plan.Original))
                                success = stopVerified && failure == null;
                            break;
                        }
                        if (!launched && !platform.IsRunning(plan.Original)) { platform.StartShell(); launched = true; }
                        await platform.PauseAsync(recovery.Token);
                    }
                }
                catch (Exception error)
                { logger.LogWarning("Desktop-shell recovery was not verified; error type {ErrorType}.", error.GetType().Name); }
            }
        }
        var summary = success ? "A replacement Windows Explorer shell window was verified in your current Windows session."
            : failure ?? "A replacement desktop shell could not be verified. Check the desktop before trying again.";
        return new() { Success = success, Summary = summary, OutputLog = summary,
            Proof = [new() { Label = "Windows Explorer desktop shell", Before = $"PID {plan.Original.Pid}, started {plan.Original.StartedUtcTicks}",
                After = after == null ? "Not verified" : $"PID {after.Pid}, started {after.StartedUtcTicks}", Status = success ? "Replacement shell verified" : "Restart not verified",
                Meaning = "A restart requires a different verified process identity owning the desktop shell window in the same user/session, with the original process gone. This does not verify the original device problem." }] };
    }
}
