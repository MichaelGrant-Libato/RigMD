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
        Guid commandId,
        DateTimeOffset capturedAt,
        string hardwareJson,
        CancellationToken cancellationToken = default);
    
    Task<AgentSnapshotRecord?> GetSnapshotByCommandAsync(
    string agentId,
    Guid commandId,
    CancellationToken cancellationToken = default);

    Task<AgentDeviceRecord?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentSnapshotRecord?> GetLatestSnapshotAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentCommandRecord> CreateCommandAsync(
        string agentId,
        string commandType,
        CancellationToken cancellationToken = default);

    Task<AgentCommandRecord?> ClaimNextCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentCommandRecord?> CompleteCommandAsync(
        string agentId,
        Guid commandId,
        CancellationToken cancellationToken = default);

    Task<AgentCommandRecord?> FailCommandAsync(
        string agentId,
        Guid commandId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    Task<AgentCommandRecord?> GetCommandAsync(
        string agentId,
        Guid commandId,
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
    public Guid? CommandId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string HardwareJson { get; set; } = "{}";
}

public class AgentCommandRecord
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}