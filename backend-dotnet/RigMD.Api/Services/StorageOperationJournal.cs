using System.Text.Json;
using System.Text.Json.Nodes;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Api.Services;

public sealed class StorageOperationJournal(IStorageOperationStore store, IStorageBootIdentity boot,
    LocalRemediationExecutionGate gate, TimeProvider clock, ILogger<StorageOperationJournal> logger)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StorageOperationEntry> _entries = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string? _bootId;
    private bool _available;
    public const string RecoveryMessage = "A previous storage action was interrupted and may still be running. Check its saved result and any Windows prompts. Save your work and restart Windows before another system action. RigMD will not repeat the request.";
    private const string UnavailableMessage = "RigMD cannot verify or save its action history. System changes are unavailable. Close and reopen RigMD after checking available storage; do not repeat an uncertain action.";

    public void Initialize()
    {
        lock (_sync)
        {
            try
            {
                _bootId = boot.Read(); store.Initialize();
                var records = store.ReadAll();
                foreach (var entry in records)
                {
                    Validate(entry);
                    var recovered = entry;
                    if (entry.MayBeRunning)
                    {
                        var anotherBoot = _bootId != null && entry.BootId != null && _bootId != entry.BootId;
                        var payload = JsonNode.Parse(entry.Payload)!.AsObject();
                        payload["status"] = "unverified";
                        payload["summary"] = anotherBoot
                            ? "Windows restarted after this request. RigMD did not repeat it and cannot confirm its final outcome. Check the current app registration, files or Recycle Bin before choosing another action."
                            : RecoveryMessage;
                        recovered = entry with { Payload = payload.ToJsonString(_json), MayBeRunning = !anotherBoot, UpdatedAt = clock.GetUtcNow() };
                        store.Save(recovered);
                    }
                    _entries[entry.Id] = recovered;
                }
                _available = true;
                gate.SetRecoveryBlock(_entries.Values.Any(item => item.MayBeRunning) ? RecoveryMessage
                    : _bootId == null ? "Windows restart information could not be verified. Storage changes are unavailable until RigMD can read it." : null);
            }
            catch (Exception error)
            {
                _available = false; gate.SetRecoveryBlock(UnavailableMessage);
                logger.LogError("Action history initialization failed: {ErrorType}.", error.GetType().Name);
            }
        }
    }

    private void Validate(StorageOperationEntry entry)
    {
        var node = JsonNode.Parse(entry.Payload)!.AsObject();
        if (entry.Kind is not ("uninstall" or "cleanup") || node["id"]?.GetValue<string>() != entry.Id ||
            node["sessionId"]?.GetValue<Guid>() != entry.SessionId || node["metricId"]?.GetValue<string>() != entry.MetricId || node["receiptVersion"]?.GetValue<int>() != 1)
            throw new InvalidOperationException("Invalid saved action identity.");
    }

    public void EnsureAvailable()
    {
        if (!Monitor.TryEnter(_sync))
            throw new InvalidOperationException(gate.RecoveryBlockReason ?? UnavailableMessage);
        try
        {
            if (!_available || _bootId == null || gate.RecoveryBlockReason != null)
                throw new InvalidOperationException(gate.RecoveryBlockReason ?? UnavailableMessage);
        }
        finally { Monitor.Exit(_sync); }
    }

    public void Save<T>(string kind, string id, Guid sessionId, string metricId, T operation, bool mayBeRunning)
    {
        lock (_sync)
        {
            if (!_available) throw new InvalidOperationException(UnavailableMessage);
            var now = clock.GetUtcNow(); var old = _entries.GetValueOrDefault(id);
            var payload = JsonSerializer.SerializeToNode(operation, _json)!.AsObject(); payload["receiptVersion"] = 1;
            var entry = new StorageOperationEntry(id, kind, sessionId, metricId, payload.ToJsonString(_json),
                old?.BootId ?? _bootId, mayBeRunning, old?.CreatedAt ?? now, now);
            Validate(entry);
            try
            {
                store.Save(entry); _entries[id] = entry;
                foreach (var oldId in _entries.Values.Where(item => !item.MayBeRunning).OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id).Skip(200).Select(item => item.Id).ToArray())
                    _entries.Remove(oldId);
            }
            catch (Exception error)
            {
                _available = false; gate.SetRecoveryBlock(UnavailableMessage);
                logger.LogError("Saving action receipt failed for {OperationId}: {ErrorType}.", id, error.GetType().Name);
                throw new InvalidOperationException(UnavailableMessage, error);
            }
        }
    }

    public T? Find<T>(string kind, string id) where T : class
    {
        lock (_sync) return _entries.TryGetValue(id, out var entry) && entry.Kind == kind ? JsonSerializer.Deserialize<T>(entry.Payload, _json) : null;
    }
    public T[] All<T>(string kind)
    {
        lock (_sync) return _entries.Values.Where(entry => entry.Kind == kind).Select(entry => JsonSerializer.Deserialize<T>(entry.Payload, _json)!).ToArray();
    }
}
