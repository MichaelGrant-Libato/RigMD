using System.Collections.Concurrent;
using System.Text.Json;

namespace RigMD.Api.Services;

public sealed record AppUninstallSnapshot(DateTimeOffset CapturedAt, string InstallerStatus, StorageVolumeReading[] Volumes);
public sealed record AppUninstallPreview(StorageAppEntry App, bool CanExecute, string Summary,
    AppUninstallSnapshot Before, string? ApprovalToken = null, DateTimeOffset? ApprovalExpiresAt = null);
public sealed record AppUninstallOperation(string Id, Guid SessionId, string MetricId, StorageAppEntry App,
    string Status, string Summary, AppUninstallSnapshot Before, AppUninstallSnapshot? After = null,
    int? ExitCode = null, bool RebootRequired = false, string? PersistenceWarning = null);

public sealed class AppUninstallService(IAppUninstallPlatform platform, RemediationApprovalStore approvals,
    LocalRemediationExecutionGate gate, TimeProvider clock, ILogger<AppUninstallService> logger, StorageOperationJournal? journal = null)
{
    private readonly ConcurrentDictionary<string, AppUninstallOperation> _operations = new();

    private StorageAppEntry Resolve(string appId)
    {
        if (appId.Length != 64 || !appId.All(Uri.IsHexDigit)) throw new InvalidOperationException("Choose an app from a fresh inventory.");
        var app = platform.Resolve(appId);
        if (app.Id != appId) throw new InvalidOperationException("The selected app registration changed. Update the app list.");
        return app;
    }

    private static bool Supported(StorageAppEntry app) => app.CanUninstall && !StorageAppInventory.ProtectedApp(app.Name, app.Publisher) &&
        (app.ProductCode != null || app.RegisteredTarget is {} executable && RegisteredAppUninstaller.ValidTarget(executable) ||
            app.PackageTarget is { FullName.Length: > 0, FamilyName.Length: > 0, UserSid.Length: > 0 });

    private static bool Matches(StorageAppEntry app, InstallerEvidence evidence) =>
        evidence.Status == "installed" && evidence.Name == app.Name && evidence.Publisher == app.Publisher && evidence.Version == app.Version;

    private AppUninstallSnapshot Snapshot(string status) => new(clock.GetUtcNow(), status, platform.ReadVolumes());

    private static bool HasCurrentWarning(AppUninstallSnapshot snapshot, string metricId)
    {
        var drive = metricId == "storage_usage" ? null : metricId.StartsWith("storage_usage_", StringComparison.Ordinal) ? metricId[14..].TrimEnd('\\') : "";
        return snapshot.Volumes.Any(volume => (drive == null || volume.Drive.TrimEnd('\\').Equals(drive, StringComparison.OrdinalIgnoreCase)) &&
            volume.TotalBytes > 0 && volume.FreeBytes >= 0 && volume.FreeBytes <= volume.TotalBytes &&
            1d - (double)volume.FreeBytes / volume.TotalBytes >= 0.8);
    }

    private static RemediationApprovalStore.Scope Scope(Guid sessionId, string metricId, StorageAppEntry app) =>
        RemediationApprovalStore.CreateScope("uninstall", sessionId, "uninstall_installed_app",
            JsonSerializer.Serialize(new { metricId, app.Id, app.Name, app.Publisher, app.Version, app.Scope, app.SourceType, app.ProductCode, app.UninstallMethod, app.RegisteredTarget, app.PackageTarget }));

    public AppUninstallPreview Preview(Guid sessionId, string metricId, string appId)
    {
        journal?.EnsureAvailable();
        var app = Resolve(appId);
        var evidence = platform.Inspect(app);
        var before = Snapshot(evidence.Status);
        if (!Supported(app))
            return new(app, false, app.UninstallReason, before);
        if (!Matches(app, evidence)) return new(app, false, "Windows could not confirm that this is the exact app shown. No removal is offered.", before);
        if (!HasCurrentWarning(before, metricId)) return new(app, false, "Fresh storage readings no longer verify the selected warning. Check current storage status before choosing an uninstall.", before);
        if (!platform.IsAdministrator) return new(app, false, "Windows administrator permission is required. Reopen RigMD as administrator, then review a fresh preview.", before);
        var approval = approvals.Issue(Scope(sessionId, metricId, app));
        var method = app.RegisteredTarget != null ? "The app’s official uninstaller will open and may ask for confirmation or Windows administrator approval. Complete or cancel its prompts. "
            : app.PackageTarget != null ? "Windows will remove this package for your current account only. It may close the app and remove unused package dependencies. Windows may refuse removal under its protection or organization policies. " : "Windows Installer will remove this installation. ";
        var description = method + $"Removing {app.Name} may remove app settings or saved data. Shared files may remain. Space saved may differ from the estimate. RigMD cannot undo removal.";
        return new(app, true, description,
            before, approval.Token, approval.ExpiresAt);
    }

    public AppUninstallOperation Start(Guid sessionId, string metricId, string appId, string? token, bool confirmed)
    {
        if (!confirmed) throw new InvalidOperationException("Review and explicitly approve this uninstall first.");
        journal?.EnsureAvailable();
        var lease = gate.TryEnter() ?? throw new InvalidOperationException(gate.RecoveryBlockReason ?? "Another system action is running. Wait for it to finish.");
        try
        {
            var app = Resolve(appId);
            if (!Supported(app) || !platform.IsAdministrator)
                throw new InvalidOperationException("This app cannot be uninstalled with the current permissions or installer support.");
            var evidence = platform.Inspect(app);
            if (!Matches(app, evidence)) throw new InvalidOperationException("The app changed since preview. Refresh and review it again.");
            var before = Snapshot(evidence.Status);
            if (!HasCurrentWarning(before, metricId)) throw new InvalidOperationException("Current storage readings do not verify the selected warning. No uninstall was started.");
            if (!approvals.TryConsume(token, Scope(sessionId, metricId, app)))
                throw new InvalidOperationException("This approval expired, changed, or was already used. Review a new preview.");
            var operation = new AppUninstallOperation(Guid.NewGuid().ToString("N"), sessionId, metricId, app,
                "running", "Removing the app. Keep RigMD open until the check afterward finishes. Do not start another removal.", before);
            foreach (var oldest in _operations.Values.Where(item => item.Status != "running").OrderBy(item => item.Before.CapturedAt).Take(Math.Max(0, _operations.Count - 63)))
                _operations.TryRemove(oldest.Id, out _);
            journal?.Save("uninstall", operation.Id, sessionId, metricId, operation, true);
            _operations[operation.Id] = operation;
            _ = Task.Run(() => CompleteAsync(operation, lease));
            return operation;
        }
        catch { lease.Dispose(); throw; }
    }

    private async Task CompleteAsync(AppUninstallOperation operation, IDisposable lease)
    {
        int? code = null;
        var trackingLost = false;
        try
        {
            var current = Resolve(operation.App.Id);
            if (current != operation.App || !Matches(current, platform.Inspect(current)))
                throw new InvalidOperationException("The app changed before execution.");
            var before = Snapshot("installed");
            if (!HasCurrentWarning(before, operation.MetricId)) throw new InvalidOperationException("The storage warning is no longer verified.");
            operation = operation with { Before = before };
            _operations[operation.Id] = operation;
            journal?.Save("uninstall", operation.Id, operation.SessionId, operation.MetricId, operation, true);
            code = await platform.UninstallAsync(current);
            var after = Snapshot(platform.Inspect(current).Status);
            var completed = Assess(operation, code.Value, after);
            _operations[operation.Id] = Persist(completed, false);
        }
        catch (Exception error)
        {
            trackingLost = error is UninstallTrackingException;
            logger.LogWarning("App uninstall verification failed for operation {OperationId}, error type {ErrorType}.", operation.Id, error.GetType().Name);
            AppUninstallSnapshot? after = null;
            try { after = Snapshot(platform.Inspect(operation.App).Status); } catch { }
            _operations[operation.Id] = Persist(operation with { Status = "unverified", Summary = trackingLost
                ? "RigMD lost contact with the uninstaller. Further system actions and normal exit remain blocked because removal may still be running. Check the official uninstaller; do not start another removal."
                : "The uninstall or its verification could not be completed. Review the installer status before taking another action; do not retry automatically.", ExitCode = code, After = after }, trackingLost);
        }
        finally { if (!trackingLost) lease.Dispose(); }
    }

    public static AppUninstallOperation Assess(AppUninstallOperation operation, int code, AppUninstallSnapshot after)
    {
        var msi = operation.App.ProductCode != null;
        var reboot = msi && code is 3010 or 1641;
        var removed = after.InstallerStatus == "absent";
        var accepted = code == 0 || msi && code is 3010 or 1641;
        var status = accepted && removed ? reboot ? "reboot_required" : "removed"
            : (msi && code == 1602 || !msi && code == 1223) && !removed ? "canceled" : !accepted && !removed ? "failed" : "unverified";
        var summary = status switch
        {
            "removed" => "Windows reports completion and no longer lists this installation. Saved data may remain. Check storage again to see whether the warning has cleared.",
            "reboot_required" => code == 1641 ? "Windows Installer reports that a restart was initiated. RigMD requested no automatic restart. Review Windows status before continuing."
                : "Windows Installer reports completion with a restart required. The machine installer registration is absent. Save your work and restart Windows yourself.",
            "canceled" => "Removal was canceled. RigMD has not confirmed that the app was removed.",
            "failed" => "Windows could not finish removing the app. Nothing was retried automatically. More information is available under Details for support.",
            _ => "RigMD could not confirm whether removal finished. Review the new status before trying again."
        };
        return operation with { Status = status, Summary = summary, After = after, ExitCode = code, RebootRequired = reboot };
    }

    private AppUninstallOperation Persist(AppUninstallOperation operation, bool mayBeRunning)
    {
        try { journal?.Save("uninstall", operation.Id, operation.SessionId, operation.MetricId, operation, mayBeRunning); return operation; }
        catch
        {
            const string warning = "The latest result could not be saved. Do not repeat this action to save history. System changes are blocked until action history is available again.";
            return operation with { PersistenceWarning = warning, Summary = operation.Summary + " " + warning };
        }
    }

    public AppUninstallOperation? Find(string id) => _operations.GetValueOrDefault(id) ?? journal?.Find<AppUninstallOperation>("uninstall", id);
    public AppUninstallOperation[] Recent(Guid sessionId) => _operations.Values.Concat(journal?.All<AppUninstallOperation>("uninstall") ?? []).DistinctBy(item => item.Id).Where(item => item.SessionId == sessionId)
        .OrderByDescending(item => item.Before.CapturedAt).Take(16).ToArray();
}
