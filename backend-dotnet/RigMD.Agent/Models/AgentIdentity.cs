namespace RigMD.Agent.Models;

public class AgentIdentity
{
    public string AgentId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string AgentVersion { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}