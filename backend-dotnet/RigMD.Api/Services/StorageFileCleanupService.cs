using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace RigMD.Api.Services;

public sealed record CleanupFile(string Id, string Name, long Bytes);
public sealed record CleanupFileList(DateTimeOffset CapturedAt, string Drive, string CategoryId, CleanupFile[] Files, bool Partial);
public sealed record CleanupPreview(DateTimeOffset CapturedAt, string Kind, string MetricId, string Drive, CleanupFile[] Files, long Bytes, RecycleBinReading Bin,
    long FreeBytes, bool CanExecute, string Summary, string? ApprovalToken, DateTimeOffset? ApprovalExpiresAt);
public sealed record CleanupOperation(string Id, Guid SessionId, string MetricId, string Status, string Summary, CleanupPreview Before,
    long? AfterFreeBytes = null, RecycleBinReading? AfterBin = null, int? ExitCode = null, string? PersistenceWarning = null);

public interface IStorageCleanupPlatform
{
    string? DesktopUser { get; }
    (StorageFileTarget[] Files, bool Partial) Scan(string category, string drive, CancellationToken cancellationToken);
    StorageFileTarget Inspect(StorageFileTarget file);
    string Presence(StorageFileTarget file);
    StorageVolumeReading[] ReadVolumes();
    RecycleBinReading ReadBin(string drive);
    Task<int> ExecuteAsync(StorageCleanupTarget target);
}

public sealed class WindowsStorageCleanupPlatform(IAppUninstallPlatform volumes) : IStorageCleanupPlatform
{
    public string? DesktopUser => NativeUninstallRunner.DesktopUser();
    public (StorageFileTarget[] Files, bool Partial) Scan(string category, string drive, CancellationToken cancellationToken)
    {
        var root = StorageFilePolicy.RootFor(category);
        if (string.IsNullOrWhiteSpace(root) || !StorageOverviewReader.OnDrive(root, drive)) throw new InvalidOperationException("This personal folder is not available on the selected drive.");
        var remaining = 20000;
        var measured = StorageOverviewReader.MeasureFolder(root, ref remaining, Stopwatch.StartNew(), TimeSpan.FromSeconds(4), cancellationToken, 20, StorageFilePolicy.EligibleName);
        var files = new List<StorageFileTarget>(); var partial = measured.Partial;
        foreach (var file in measured.LargestFiles.Where(file => StorageFilePolicy.EligibleName(file.Name)).Take(20))
        {
            try { files.Add(StorageFilePolicy.Inspect(category, root, file.Name)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { partial = true; }
        }
        return (files.ToArray(), partial);
    }
    public StorageFileTarget Inspect(StorageFileTarget file) => StorageFilePolicy.Inspect(file.CategoryId, file.Root, file.RelativeName);
    public string Presence(StorageFileTarget file)
    {
        try { _ = File.GetAttributes(StorageFilePolicy.Resolve(file.Root, file.RelativeName)); return "present"; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return "absent"; }
        catch { return "unknown"; }
    }
    public StorageVolumeReading[] ReadVolumes() => volumes.ReadVolumes();
    public RecycleBinReading ReadBin(string drive) => StorageCleanupWorker.ReadBin(drive);
    public Task<int> ExecuteAsync(StorageCleanupTarget target) => NativeUninstallRunner.RunHelperAsync(target.UserSid,
        StorageCleanupWorker.Switch, StorageCleanupWorker.RequestVariable, JsonSerializer.Serialize(target));
}

public sealed class StorageFileCleanupService(IStorageCleanupPlatform platform, RemediationApprovalStore approvals,
    LocalRemediationExecutionGate gate, TimeProvider clock, StorageOperationJournal? journal = null)
{
    private sealed record Candidate(StorageFileTarget Target, string Drive, DateTimeOffset Expires);
    private sealed record Pending(Guid Session, CleanupPreview Preview, StorageCleanupTarget Target);
    private readonly ConcurrentDictionary<string, Candidate> _catalog = new();
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly ConcurrentDictionary<string, CleanupOperation> _operations = new();

    public CleanupFileList Files(string metricId, string category, CancellationToken cancellationToken)
    {
        var volume = Volume(metricId);
        var scan = platform.Scan(category, volume.Drive, cancellationToken);
        foreach (var id in _catalog.Where(pair => pair.Value.Expires <= clock.GetUtcNow()).Select(pair => pair.Key)) _catalog.TryRemove(id, out _);
        var result = new List<CleanupFile>();
        foreach (var file in scan.Files.Take(20))
        {
            if (_catalog.Count >= 512) _catalog.TryRemove(_catalog.Keys.First(), out _);
            var id = Guid.NewGuid().ToString("N");
            _catalog[id] = new(file, volume.Drive, clock.GetUtcNow().AddMinutes(10));
            result.Add(new(id, file.RelativeName, file.Bytes));
        }
        return new(clock.GetUtcNow(), volume.Drive, category, result.ToArray(), scan.Partial);
    }

    private StorageVolumeReading Volume(string metric)
    {
        var drive = StorageOverviewReader.DriveForMetric(metric, Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        var result = platform.ReadVolumes().SingleOrDefault(volume => volume.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        return result is { TotalBytes: > 0, FreeBytes: >= 0 } && result.FreeBytes <= result.TotalBytes ? result : throw new InvalidOperationException("The selected drive could not be read.");
    }
    private static bool Warning(StorageVolumeReading volume) => 1d - (double)volume.FreeBytes / volume.TotalBytes >= .8;
    private static RemediationApprovalStore.Scope Scope(Guid session, string metric, StorageCleanupTarget target) =>
        RemediationApprovalStore.CreateScope("storage_cleanup", session, target.Kind, JsonSerializer.Serialize(new { metric, target }));

    public CleanupPreview Preview(Guid session, string metric, string kind, string[] ids)
    {
        journal?.EnsureAvailable();
        if (kind is not ("recycle_files" or "empty_recycle_bin") || ids.Length > 20 || ids.Distinct().Count() != ids.Length ||
            kind == "recycle_files" && ids.Length == 0 || kind == "empty_recycle_bin" && ids.Length != 0)
            throw new InvalidOperationException("Choose a cleanup action and specific files to review.");
        var volume = Volume(metric);
        var sid = platform.DesktopUser ?? throw new InvalidOperationException("Personal-file cleanup requires the same account’s normal Windows desktop. No elevated deletion fallback is used.");
        var candidates = ids.Select(id => _catalog.TryGetValue(id, out var item) && item.Expires > clock.GetUtcNow() && item.Drive == volume.Drive
            ? item : throw new InvalidOperationException("The file list expired or changed. Scan the folder again.")).ToArray();
        if (candidates.Any(item => platform.Inspect(item.Target) != item.Target)) throw new InvalidOperationException("A selected file changed. Scan and review it again.");
        var files = ids.Select((id, index) => new CleanupFile(id, candidates[index].Target.RelativeName, candidates[index].Target.Bytes)).ToArray();
        var bin = platform.ReadBin(volume.Drive);
        var target = new StorageCleanupTarget(kind, volume.Drive, sid, candidates.Select(item => item.Target).ToArray(), bin);
        var canExecute = Warning(volume) && (kind == "recycle_files" || bin.Items > 0);
        var summary = !Warning(volume) ? "Current readings no longer confirm this storage warning. Check storage again before cleaning."
            : kind == "recycle_files" ? "Move only these selected files to your Recycle Bin. You can restore them there. This does not free drive space yet; emptying the bin is a separate, permanent action. If these files are in a synced folder, the removal may also sync to cloud storage and other devices. This is not a cloud-only ‘free up space’ action."
            : bin.Items == 0 ? "Your Recycle Bin on this drive is already empty."
            : $"Permanently empty ALL {bin.Items} items currently in your Recycle Bin on {volume.Drive}, including items deleted outside RigMD. They cannot be restored from the bin afterward. Contents are checked again before execution.";
        var approval = canExecute ? approvals.Issue(Scope(session, metric, target)) : null;
        var preview = new CleanupPreview(clock.GetUtcNow(), kind, metric, volume.Drive, files, kind == "recycle_files" ? files.Sum(file => file.Bytes) : bin.Bytes,
            bin, volume.FreeBytes, canExecute, summary, approval?.Token, approval?.ExpiresAt);
        foreach (var key in _pending.Where(pair => pair.Value.Preview.ApprovalExpiresAt <= clock.GetUtcNow()).Select(pair => pair.Key)) _pending.TryRemove(key, out _);
        if (approval != null) { if (_pending.Count >= 256) throw new InvalidOperationException("Too many pending previews. Wait and review again."); _pending[approval.Token] = new(session, preview, target); }
        return preview;
    }

    public CleanupOperation Start(Guid session, string metric, string? token, bool confirmed)
    {
        if (!confirmed || token == null) throw new InvalidOperationException("Review and explicitly approve this cleanup first.");
        journal?.EnsureAvailable();
        var lease = gate.TryEnter() ?? throw new InvalidOperationException(gate.RecoveryBlockReason ?? "Another system action is running. Wait for its result.");
        try
        {
            if (!_pending.TryRemove(token, out var pending) || pending.Session != session || pending.Preview.MetricId != metric ||
                !approvals.TryConsume(token, Scope(session, metric, pending.Target))) throw new InvalidOperationException("This preview expired, changed, or was already used.");
            Validate(pending.Target, metric);
            var operation = new CleanupOperation(Guid.NewGuid().ToString("N"), session, metric, "running", "Cleaning your selected storage. Keep RigMD open until the result appears.", pending.Preview with { ApprovalToken = null });
            foreach (var old in _operations.Values.Where(item => item.Status != "running").Take(Math.Max(0, _operations.Count - 63))) _operations.TryRemove(old.Id, out _);
            journal?.Save("cleanup", operation.Id, session, metric, operation, true);
            _operations[operation.Id] = operation;
            _ = Task.Run(() => Complete(operation, pending.Target, lease));
            return operation;
        }
        catch { lease.Dispose(); throw; }
    }
    private void Validate(StorageCleanupTarget target, string metric)
    {
        if (platform.DesktopUser != target.UserSid || !Warning(Volume(metric))) throw new InvalidOperationException("The account or current storage warning changed. Review again.");
        if (target.Files.Any(file => platform.Inspect(file) != file) || target.Kind == "empty_recycle_bin" && platform.ReadBin(target.Drive) != target.Bin)
            throw new InvalidOperationException("The selected files or Recycle Bin contents changed. Review a new preview.");
    }
    private async Task Complete(CleanupOperation operation, StorageCleanupTarget target, IDisposable lease)
    {
        var trackingLost = false;
        try
        {
            Validate(target, operation.MetricId);
            operation = operation with { Before = operation.Before with { FreeBytes = Volume(operation.MetricId).FreeBytes } };
            _operations[operation.Id] = operation;
            journal?.Save("cleanup", operation.Id, operation.SessionId, operation.MetricId, operation, true);
            var code = await platform.ExecuteAsync(target);
            var volume = Volume(operation.MetricId); var bin = platform.ReadBin(target.Drive);
            var absent = target.Files.Count(file => platform.Presence(file) == "absent");
            var status = code == 0 && (target.Kind == "empty_recycle_bin" ? bin.Items == 0 : absent == target.Files.Length)
                ? target.Kind == "empty_recycle_bin" ? "emptied" : "recycled" : absent > 0 ? "partial" : code == 1223 ? "canceled" : "unverified";
            var summary = status == "recycled" ? "Windows confirmed moving the selected files to the Recycle Bin. Space is not freed yet. Review and empty the bin separately when you are ready."
                : status == "emptied" ? "Windows confirmed that this drive’s Recycle Bin is empty. The new free-space reading is shown below. Check the storage warning again."
                : status == "canceled" ? "The cleanup was canceled. Check the file list and Recycle Bin before trying again."
                : "Cleanup could not be fully confirmed. Some files may have moved. Refresh the folder and Recycle Bin before doing anything else; nothing was retried automatically.";
            _operations[operation.Id] = Persist(operation with { Status = status, Summary = summary, AfterFreeBytes = volume.FreeBytes, AfterBin = bin, ExitCode = code }, false);
        }
        catch (Exception error)
        {
            trackingLost = error is UninstallTrackingException;
            _operations[operation.Id] = Persist(operation with { Status = "unverified", Summary = trackingLost
                ? "Cleanup tracking was lost. Further system actions and ordinary exit remain blocked because work may still be running. Do not repeat the cleanup."
                : "Cleanup or its verification could not be completed. Refresh the file list and Recycle Bin before taking another action." }, trackingLost);
        }
        finally { if (!trackingLost) lease.Dispose(); }
    }
    private CleanupOperation Persist(CleanupOperation operation, bool mayBeRunning)
    {
        try { journal?.Save("cleanup", operation.Id, operation.SessionId, operation.MetricId, operation, mayBeRunning); return operation; }
        catch
        {
            const string warning = "The latest result could not be saved. Do not repeat this cleanup to save history. System changes are blocked until action history is available again.";
            return operation with { PersistenceWarning = warning, Summary = operation.Summary + " " + warning };
        }
    }
    public CleanupOperation? Find(string id) => _operations.GetValueOrDefault(id) ?? journal?.Find<CleanupOperation>("cleanup", id);
    public CleanupOperation[] Recent(Guid session, string metric) => _operations.Values.Concat(journal?.All<CleanupOperation>("cleanup") ?? []).DistinctBy(item => item.Id).Where(item => item.SessionId == session && item.MetricId == metric).OrderByDescending(item => item.Before.CapturedAt).Take(16).ToArray();
}
