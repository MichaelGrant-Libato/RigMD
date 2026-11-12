using System.Globalization;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Tools;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed record UpdateCachePlan(TempCleanupPlan Files, UpdateServiceState OriginalState);

public class ClearWindowsUpdateCacheAction(ILogger<ClearWindowsUpdateCacheAction> logger)
{
    // Legacy execution has neither the approved file identities nor the approved service state.
    public Task<ExecutionResult> ExecuteAsync() => Task.FromResult(new ExecutionResult
    { Success = false, Summary = "A fresh approved update-cache snapshot is required. Nothing was deleted and no service was changed." });

    internal async Task<ExecutionResult> ExecutePlanAsync(UpdateCachePlan plan, IUpdateServiceControl service,
        TimeProvider clock, CancellationToken cancellation, Action<string>? progress = null)
    {
        ExecutionResult? cleanup = null;
        string? failure = null;
        var restoreRequired = false;
        var restored = false;
        UpdateServiceState? afterState = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!plan.Files.IsCurrent(clock.GetUtcNow()) || plan.Files.Files.Length == 0)
                throw new InvalidOperationException("The approved update-cache snapshot is empty, expired or already used.");
            if (plan.OriginalState is not (UpdateServiceState.Running or UpdateServiceState.Stopped) || service.ReadState() != plan.OriginalState)
                throw new InvalidOperationException("The Windows Update service state changed. Review a fresh preview.");
            // Restore even after a canceled/failed stop: Windows may have accepted that request.
            restoreRequired = true;
            if (plan.OriginalState == UpdateServiceState.Running && !await service.StopAsync(cancellation))
                throw new InvalidOperationException("Windows Update could not be confirmed stopped. No cleanup was started.");
            if (service.ReadState() != UpdateServiceState.Stopped)
                throw new InvalidOperationException("Windows Update is not confirmed stopped. No cleanup was started.");
            cleanup = await ApprovedFileCleanup.ExecuteAsync(plan.Files, clock.GetUtcNow(), cancellation, logger,
                "Windows Update download-cache", "Windows Update cache files deleted", progress);
        }
        catch (OperationCanceledException) { failure = "Update-cache cleanup was canceled. Review the receipt for any files already removed."; }
        catch (Exception error)
        {
            logger.LogWarning("Update-cache cleanup stopped; error type {ErrorType}.", error.GetType().Name);
            failure = error is InvalidOperationException ? error.Message : "The update-cache operation could not be verified.";
        }
        finally
        {
            if (restoreRequired)
            {
                try
                {
                    restored = await service.RestoreAsync(plan.OriginalState);
                    afterState = service.ReadState();
                    restored &= afterState == plan.OriginalState;
                }
                catch (Exception error)
                { logger.LogWarning("Windows Update service recovery could not be verified; error type {ErrorType}.", error.GetType().Name); }
            }
        }
        var proof = cleanup?.Proof?.ToList() ?? [];
        static int? Count(List<ExecutionProof> rows, string label) => int.TryParse(rows.FirstOrDefault(row => row.Label == label)?.After,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : null;
        var deleted = Count(proof, "Windows Update cache files deleted");
        var complete = cleanup?.Success == true && deleted == plan.Files.Files.Length && Count(proof, "Previewed files not removed") == 0;
        var success = complete && restored && failure == null;
        var summary = failure ?? (complete ? $"Removed all {deleted} unchanged update-cache files included in this preview."
            : cleanup?.Summary ?? "No update-cache files were removed.");
        summary += !restoreRequired ? " No service change was started."
            : restored ? $" Windows Update service state verified: {plan.OriginalState}."
            : " Windows Update service recovery could not be verified. This action is not successful; check Windows Update before continuing.";
        if (cleanup != null && !complete) summary += " Not all approved files were removed. Do not repeat this request automatically.";
        if (plan.Files.Partial) summary += " The preview was a bounded subset; other cache files may remain.";
        summary += " Check available storage separately; this does not verify a repair.";
        proof.Add(new() { Label = "Windows Update service", Before = plan.OriginalState.ToString(), After = afterState?.ToString() ?? "Not verified",
            Status = restored ? "Original state verified" : restoreRequired ? "Recovery not verified" : "Not changed",
            Meaning = "Success requires verified restoration of the service's original state, not merely deleted files." });
        return new() { Success = success, Summary = summary, OutputLog = summary, Proof = proof };
    }
}
