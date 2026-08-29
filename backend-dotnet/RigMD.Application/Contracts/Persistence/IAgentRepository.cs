namespace RigMD.Application.Contracts.Persistence;

public interface IAgentRepository
{
    Task<AgentDeviceRecord> RegisterAsync(
        string agentId,
        string clientId,
        string deviceName,
        string agentVersion,
        CancellationToken cancellationToken = default);

    Task<AgentDeviceRecord?> HeartbeatAsync(
        string agentId,
        string deviceName,
        string agentVersion,
        CancellationToken cancellationToken = default);

    Task SaveSnapshotAsync(
        string agentId,
        DateTimeOffset capturedAt,
        string hardwareJson,
        CancellationToken cancellationToken = default);

    Task<AgentDeviceRecord?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentSnapshotRecord?> GetLatestSnapshotAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}

public class AgentDeviceRecord
{
    public string AgentId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

public class AgentSnapshotRecord
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public string HardwareJson { get; set; } = "{}";
}