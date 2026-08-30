namespace RigMD.Api.Models;

public class AgentRegisterRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
}

public class AgentHeartbeatRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
}

public class AgentSnapshotRequest
{
    public string AgentId { get; set; } = string.Empty;
    public Guid CommandId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public object Hardware { get; set; } = new();
}
public class AgentCommandFailureRequest
{
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class AutomaticDiagnosisRequest
{
    public string AgentId { get; set; } =
        string.Empty;

    public Guid CommandId { get; set; }

    public string Mode { get; set; } =
        "full";

    public List<string> ComponentIds { get; set; } =
        new();

    public string? ScenarioId { get; set; }
}