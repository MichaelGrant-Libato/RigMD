using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

public class ClearBrowserCacheAction(ILogger<ClearBrowserCacheAction> logger)
{
    // Legacy dispatch has no approved file snapshot and cannot start cleanup.
    public Task<ExecutionResult> ExecuteAsync() => Task.FromResult(new ExecutionResult
    { Success = false, Summary = "A fresh approved browser-cache snapshot is required. Nothing was deleted." });

    internal Task<ExecutionResult> ExecutePlanAsync(BrowserCachePlan plan, DateTimeOffset now,
        CancellationToken cancellation, Action<string>? report = null) =>
        ApprovedFileCleanup.ExecuteAsync(plan.Snapshot, now, cancellation, logger, "browser-cache", "Browser cache files deleted", report);
}
