using System.Text.Json;
using RigMD.Agent.Models;

namespace RigMD.Agent.Services;

public class AgentIdentityService
{
    private readonly string _identityPath;

    public AgentIdentityService()
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "RigMD");

        Directory.CreateDirectory(basePath);

        _identityPath =
            Path.Combine(basePath, "agent.json");
    }

    public AgentIdentity GetOrCreateIdentity()
    {
        if (File.Exists(_identityPath))
        {
            var json =
                File.ReadAllText(_identityPath);

            var existing =
                JsonSerializer.Deserialize<AgentIdentity>(
                    json);

            if (existing != null &&
                !string.IsNullOrWhiteSpace(
                    existing.AgentId))
            {
                return existing;
            }
        }

        var identity = new AgentIdentity
        {
            AgentId = Guid.NewGuid().ToString(),
            DeviceName = Environment.MachineName,
            AgentVersion = "0.1.0",
            RegisteredAt = DateTimeOffset.UtcNow
        };

        var serialized =
            JsonSerializer.Serialize(
                identity,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(
            _identityPath,
            serialized);

        return identity;
    }

    public string GetIdentityPath()
    {
        return _identityPath;
    }
}