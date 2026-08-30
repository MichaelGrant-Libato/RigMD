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
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

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
            new NpgsqlCommand(
                sql,
                connection);

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

        await reader.ReadAsync(
            cancellationToken);

        return ReadAgent(reader);
    }

    public async Task<AgentDeviceRecord?> HeartbeatAsync(
        string agentId,
        string deviceName,
        string agentVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

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
            new NpgsqlCommand(
                sql,
                connection);

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

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadAgent(reader);
    }

    public async Task SaveSnapshotAsync(
        string agentId,
        Guid commandId,
        DateTimeOffset capturedAt,
        string hardwareJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            INSERT INTO agent_snapshots
            (
                id,
                agent_id,
                command_id,
                captured_at,
                hardware_json
            )
            VALUES
            (
                @id,
                @agentId,
                @commandId,
                @capturedAt,
                CAST(@hardwareJson AS jsonb)
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "id",
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "commandId",
            commandId);

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
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

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
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
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
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            SELECT
                id,
                agent_id,
                command_id,
                captured_at,
                hardware_json::text
            FROM agent_snapshots
            WHERE agent_id = @agentId
            ORDER BY captured_at DESC
            LIMIT 1;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<AgentSnapshotRecord?> GetSnapshotByCommandAsync(
        string agentId,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            SELECT
                id,
                agent_id,
                command_id,
                captured_at,
                hardware_json::text
            FROM agent_snapshots
            WHERE agent_id = @agentId
              AND command_id = @commandId
            ORDER BY captured_at DESC
            LIMIT 1;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "commandId",
            commandId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<AgentCommandRecord> CreateCommandAsync(
        string agentId,
        string commandType,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            INSERT INTO agent_commands
            (
                id,
                agent_id,
                command_type,
                status,
                requested_at
            )
            VALUES
            (
                @id,
                @agentId,
                @commandType,
                'pending',
                NOW()
            )
            RETURNING
                id,
                agent_id,
                command_type,
                status,
                requested_at,
                claimed_at,
                completed_at,
                error_message;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "id",
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "commandType",
            commandType);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        await reader.ReadAsync(
            cancellationToken);

        return ReadCommand(reader);
    }

    public async Task<AgentCommandRecord?> ClaimNextCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            WITH next_command AS
            (
                SELECT id
                FROM agent_commands
                WHERE agent_id = @agentId
                  AND status = 'pending'
                ORDER BY requested_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE agent_commands
            SET
                status = 'running',
                claimed_at = NOW()
            WHERE id IN
            (
                SELECT id
                FROM next_command
            )
            RETURNING
                id,
                agent_id,
                command_type,
                status,
                requested_at,
                claimed_at,
                completed_at,
                error_message;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadCommand(reader);
    }

    public async Task<AgentCommandRecord?> CompleteCommandAsync(
        string agentId,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            UPDATE agent_commands
            SET
                status = 'completed',
                completed_at = NOW(),
                error_message = NULL
            WHERE id = @commandId
              AND agent_id = @agentId
              AND status = 'running'
            RETURNING
                id,
                agent_id,
                command_type,
                status,
                requested_at,
                claimed_at,
                completed_at,
                error_message;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "commandId",
            commandId);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadCommand(reader);
    }

    public async Task<AgentCommandRecord?> FailCommandAsync(
        string agentId,
        Guid commandId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            UPDATE agent_commands
            SET
                status = 'failed',
                completed_at = NOW(),
                error_message = @errorMessage
            WHERE id = @commandId
              AND agent_id = @agentId
              AND status = 'running'
            RETURNING
                id,
                agent_id,
                command_type,
                status,
                requested_at,
                claimed_at,
                completed_at,
                error_message;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "commandId",
            commandId);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        command.Parameters.AddWithValue(
            "errorMessage",
            errorMessage);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadCommand(reader);
    }

    public async Task<AgentCommandRecord?> GetCommandAsync(
        string agentId,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(
                GetConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            SELECT
                id,
                agent_id,
                command_type,
                status,
                requested_at,
                claimed_at,
                completed_at,
                error_message
            FROM agent_commands
            WHERE id = @commandId
              AND agent_id = @agentId
            LIMIT 1;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "commandId",
            commandId);

        command.Parameters.AddWithValue(
            "agentId",
            agentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadCommand(reader);
    }

    private string GetConnectionString()
    {
        var databaseUrl =
            _configuration["DATABASE_URL"] ??
            Environment.GetEnvironmentVariable(
                "DATABASE_URL");

        if (string.IsNullOrWhiteSpace(
                databaseUrl))
        {
            throw new InvalidOperationException(
                "DATABASE_URL is not configured.");
        }

        var uri =
            new Uri(databaseUrl);

        var userInfo =
            uri.UserInfo.Split(
                ':',
                2);

        if (userInfo.Length != 2)
        {
            throw new InvalidOperationException(
                "DATABASE_URL contains invalid credentials.");
        }

        var builder =
            new NpgsqlConnectionStringBuilder
            {
                Host =
                    uri.Host,

                Port =
                    uri.Port > 0
                        ? uri.Port
                        : 5432,

                Database =
                    uri.AbsolutePath
                        .TrimStart('/'),

                Username =
                    Uri.UnescapeDataString(
                        userInfo[0]),

                Password =
                    Uri.UnescapeDataString(
                        userInfo[1]),

                SslMode =
                    SslMode.Require,

                Pooling =
                    true,

                Timeout =
                    10,

                CommandTimeout =
                    15
            };

        return builder.ConnectionString;
    }

    private static AgentDeviceRecord ReadAgent(
        NpgsqlDataReader reader)
    {
        return new AgentDeviceRecord
        {
            AgentId =
                reader.GetString(0),

            ClientId =
                reader.GetString(1),

            DeviceName =
                reader.GetString(2),

            AgentVersion =
                reader.GetString(3),

            RegisteredAt =
                reader.GetFieldValue<DateTimeOffset>(
                    4),

            LastSeen =
                reader.GetFieldValue<DateTimeOffset>(
                    5)
        };
    }

    private static AgentSnapshotRecord ReadSnapshot(
        NpgsqlDataReader reader)
    {
        return new AgentSnapshotRecord
        {
            Id =
                reader.GetGuid(0),

            AgentId =
                reader.GetString(1),

            CommandId =
                reader.IsDBNull(2)
                    ? null
                    : reader.GetGuid(2),

            CapturedAt =
                reader.GetFieldValue<DateTimeOffset>(
                    3),

            HardwareJson =
                reader.GetString(4)
        };
    }

    private static AgentCommandRecord ReadCommand(
        NpgsqlDataReader reader)
    {
        return new AgentCommandRecord
        {
            Id =
                reader.GetGuid(0),

            AgentId =
                reader.GetString(1),

            CommandType =
                reader.GetString(2),

            Status =
                reader.GetString(3),

            RequestedAt =
                reader.GetFieldValue<DateTimeOffset>(
                    4),

            ClaimedAt =
                reader.IsDBNull(5)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(
                        5),

            CompletedAt =
                reader.IsDBNull(6)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(
                        6),

            ErrorMessage =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetString(7)
        };
    }
}