namespace RigMD.Application.Contracts.Persistence;

public sealed record StorageOperationEntry(string Id, string Kind, Guid SessionId, string MetricId,
    string Payload, string? BootId, bool MayBeRunning, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IStorageOperationStore
{
    void Initialize();
    StorageOperationEntry[] ReadAll();
    void Save(StorageOperationEntry entry);
}

public interface IStorageBootIdentity
{
    string? Read();
}
