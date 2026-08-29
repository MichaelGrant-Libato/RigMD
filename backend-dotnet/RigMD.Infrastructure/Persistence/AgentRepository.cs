using Microsoft.Extensions.Configuration;
using Npgsql;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Infrastructure.Persistence;

public class AgentRepository : IAgentRepository
{
    private readonly IConfiguration _configuration;

    public AgentRepository(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<AgentDeviceRecord> RegisterAsync(
        string agentId,
        string clientId,
        string deviceName,
        string agentVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO agent_devices
            (
                agent_id,
                client_id,
                device_name,
                agent_version,
                registered_at,
                last_seen
            )
            VALUES
            (
                @agentId,
                @clientId,
                @deviceName,
                @agentVersion,
                NOW(),
                NOW()
            )
            ON CONFLICT (agent_id)
            DO UPDATE SET
                client_id = EXCLUDED.client_id,
                device_name = EXCLUDED.device_name,
                agent_version = EXCLUDED.agent_version,
                last_seen = NOW()
            RETURNING
                agent_id,
                client_id,
                device_name,
                agent_version,
                registered_at,
                last_seen;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "clientId",
            clientId);

        command.Parameters.AddWithValue(
            "deviceName",
            deviceName);

        command.Parameters.AddWithValue(
            "agentVersion",
            agentVersion);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        await reader.ReadAsync(cancellationToken);

        return ReadAgent(reader);
    }

    public async Task<AgentDeviceRecord?> HeartbeatAsync(
        string agentId,
        string deviceName,
        string agentVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE agent_devices
            SET
                device_name = @deviceName,
                agent_version = @agentVersion,
                last_seen = NOW()
            WHERE agent_id = @agentId
            RETURNING
                agent_id,
                client_id,
                device_name,
                agent_version,
                registered_at,
                last_seen;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "deviceName",
            deviceName);

        command.Parameters.AddWithValue(
            "agentVersion",
            agentVersion);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAgent(reader);
    }

    public async Task SaveSnapshotAsync(
        string agentId,
        DateTimeOffset capturedAt,
        string hardwareJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO agent_snapshots
            (
                id,
                agent_id,
                captured_at,
                hardware_json
            )
            VALUES
            (
                @id,
                @agentId,
                @capturedAt,
                CAST(@hardwareJson AS jsonb)
            );
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "id",
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "capturedAt",
            capturedAt);

        command.Parameters.AddWithValue(
            "hardwareJson",
            hardwareJson);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<AgentDeviceRecord?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                agent_id,
                client_id,
                device_name,
                agent_version,
                registered_at,
                last_seen
            FROM agent_devices
            WHERE agent_id = @agentId
            LIMIT 1;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAgent(reader);
    }

    public async Task<AgentSnapshotRecord?> GetLatestSnapshotAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                id,
                agent_id,
                captured_at,
                hardware_json::text
            FROM agent_snapshots
            WHERE agent_id = @agentId
            ORDER BY captured_at DESC
            LIMIT 1;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AgentSnapshotRecord
        {
            Id = reader.GetGuid(0),
            AgentId = reader.GetString(1),
            CapturedAt = reader.GetFieldValue<DateTimeOffset>(2),
            HardwareJson = reader.GetString(3)
        };
    }

    private string GetConnectionString()
    {
        var databaseUrl =
            _configuration["DATABASE_URL"] ??
            Environment.GetEnvironmentVariable(
                "DATABASE_URL");

        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException(
                "DATABASE_URL is not configured.");
        }

        var uri =
            new Uri(databaseUrl);

        var userInfo =
            uri.UserInfo.Split(':', 2);

        if (userInfo.Length != 2)
        {
            throw new InvalidOperationException(
                "DATABASE_URL contains invalid credentials.");
        }

        var builder =
            new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0
                    ? uri.Port
                    : 5432,
                Database =
                    uri.AbsolutePath.TrimStart('/'),
                Username =
                    Uri.UnescapeDataString(
                        userInfo[0]),
                Password =
                    Uri.UnescapeDataString(
                        userInfo[1]),
                SslMode = SslMode.Require,
                Pooling = true,
                Timeout = 10,
                CommandTimeout = 15
            };

        return builder.ConnectionString;
    }

    private static AgentDeviceRecord ReadAgent(
        NpgsqlDataReader reader)
    {
        return new AgentDeviceRecord
        {
            AgentId = reader.GetString(0),
            ClientId = reader.GetString(1),
            DeviceName = reader.GetString(2),
            AgentVersion = reader.GetString(3),
            RegisteredAt =
                reader.GetFieldValue<DateTimeOffset>(4),
            LastSeen =
                reader.GetFieldValue<DateTimeOffset>(5)
        };
    }
}