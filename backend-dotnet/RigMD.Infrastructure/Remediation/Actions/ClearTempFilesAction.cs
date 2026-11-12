using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

public class ClearTempFilesAction(ILogger<ClearTempFilesAction> logger)
{
    // Legacy dispatch cannot select a new target or bypass the approved snapshot.
    public Task<ExecutionResult> ExecuteAsync(Action<string>? progressReporter = null) => Task.FromResult(ReviewRequired());
    public Task<ExecutionResult> ExecuteAsync(string tempPath, Action<string>? progressReporter = null) => Task.FromResult(ReviewRequired());
    private static ExecutionResult ReviewRequired() => new() { Success = false, Summary = "A fresh approved temporary-file snapshot is required. Nothing was deleted." };

    internal Task<ExecutionResult> ExecutePlanAsync(TempCleanupPlan plan, DateTimeOffset now,
        CancellationToken cancellation, Action<string>? report = null) =>
        ApprovedFileCleanup.ExecuteAsync(plan, now, cancellation, logger, "temporary", "Temporary files deleted", report);
}
