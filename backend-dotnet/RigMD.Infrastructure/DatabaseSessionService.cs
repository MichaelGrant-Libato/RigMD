using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Domain.Rules;

namespace RigMD.Infrastructure;

public class DatabaseSessionService
{
    private readonly IConfiguration _configuration;

    public DatabaseSessionService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        var databaseUrl = _configuration["DATABASE_URL"];

        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("DATABASE_URL is not configured.");

        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);

        if (userInfo.Length != 2)
            throw new InvalidOperationException(
                "DATABASE_URL contains invalid credentials."
            );

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = SslMode.Require,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 10,
            Timeout = 15,
            CommandTimeout = 30,
            KeepAlive = 0
        };

        return builder.ConnectionString;
    }

    public async Task<bool> UpdateResolutionAsync(
        Guid sessionId,
        string resolutionStatus,
        string resolutionCheckedAt,
        string resolutionSummary,
        object[] resolutionProof)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            UPDATE sessions
            SET
                resolution_status = @resolution_status,
                resolution_checked_at = @resolution_checked_at,
                resolution_summary = @resolution_summary,
                resolution_proof = @resolution_proof::json
            WHERE id = @id;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 30;

        var parsedCheckedAt =
            DateTime.Parse(resolutionCheckedAt);

        parsedCheckedAt =
            DateTime.SpecifyKind(
                parsedCheckedAt,
                DateTimeKind.Unspecified
            );

        command.Parameters.AddWithValue(
            "resolution_status",
            resolutionStatus
        );

        command.Parameters.AddWithValue(
            "resolution_checked_at",
            parsedCheckedAt
        );

        command.Parameters.AddWithValue(
            "resolution_summary",
            resolutionSummary
        );

        command.Parameters.AddWithValue(
            "resolution_proof",
            JsonSerializer.Serialize(resolutionProof)
        );

        command.Parameters.AddWithValue(
            "id",
            sessionId
        );

        var affectedRows =
            await command.ExecuteNonQueryAsync();

        return affectedRows > 0;
    }

    public async Task<bool> MarkNeedsRecheckAsync(
        Guid sessionId)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            UPDATE sessions
            SET
                resolution_status = 'needs_recheck',
                last_action_status = 'completed',
                last_action_summary =
                    'A safe action was performed. Run a follow-up check to see if the issue improved.'
            WHERE id = @id;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 30;

        command.Parameters.AddWithValue(
            "id",
            sessionId
        );

        var affectedRows =
            await command.ExecuteNonQueryAsync();

        return affectedRows > 0;
    }

    public async Task<Guid> SaveDiagnosisAsync(
        DiagnosticSymptomPayload payload,
        HardwareProfileDto hardware,
        string diagnosedCategory,
        string actionCategory,
        string confidenceLabel,
        string aiExplanation)
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            var profileId =
                await GetOrCreateProfileAsync(
                    connection,
                    transaction,
                    hardware
                );

            var sessionId =
                Guid.NewGuid();

            const string sql = """
                INSERT INTO sessions
                (
                    id,
                    profile_id,
                    symptom_type,
                    affected_activity,
                    frequency,
                    severity,
                    duration,
                    recent_changes,
                    system_state,
                    warning_signs,
                    diagnosed_category,
                    action_category,
                    confidence_label,
                    ai_explanation,
                    is_recurring,
                    resolution_status,
                    resolution_summary,
                    resolution_proof,
                    last_action_summary,
                    client_id
                )
                VALUES
                (
                    @id,
                    @profile_id,
                    @symptom_type,
                    @affected_activity,
                    @frequency,
                    @severity,
                    @duration,
                    @recent_changes,
                    @system_state,
                    @warning_signs,
                    @diagnosed_category,
                    @action_category,
                    @confidence_label,
                    @ai_explanation,
                    FALSE,
                    'open',
                    '',
                    '[]'::json,
                    '',
                    @client_id
                );
                """;

            await using var command =
                new NpgsqlCommand(
                    sql,
                    connection,
                    transaction
                );

            command.CommandTimeout = 30;

            command.Parameters.AddWithValue(
                "id",
                sessionId
            );

            command.Parameters.AddWithValue(
                "profile_id",
                profileId
            );

            command.Parameters.AddWithValue(
                "symptom_type",
                payload.SymptomType ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "affected_activity",
                payload.AffectedActivity ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "frequency",
                payload.Frequency ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "severity",
                payload.Severity ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "duration",
                payload.Duration ?? "N/A"
            );

            command.Parameters.AddWithValue(
                "recent_changes",
                payload.RecentChanges ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "system_state",
                payload.SystemState ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "warning_signs",
                payload.WarningSigns ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "diagnosed_category",
                diagnosedCategory
            );

            command.Parameters.AddWithValue(
                "action_category",
                actionCategory
            );

            command.Parameters.AddWithValue(
                "confidence_label",
                confidenceLabel
            );

            command.Parameters.AddWithValue(
                "ai_explanation",
                aiExplanation ?? string.Empty
            );

            command.Parameters.AddWithValue(
                "client_id",
                string.IsNullOrWhiteSpace(hardware.DeviceName)
                    ? "legacy"
                    : hardware.DeviceName
            );

            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return sessionId;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NpgsqlException)
            {
            }

            throw;
        }
    }

    public async Task<HashSet<string>>
        GetRecurringSymptomLookupAsync()
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            SELECT symptom_type
            FROM sessions
            WHERE
                symptom_type IS NOT NULL
                AND TRIM(symptom_type) <> ''
            GROUP BY symptom_type
            HAVING COUNT(id) >= 2;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 30;

        await using var reader =
            await command.ExecuteReaderAsync();

        var recurringSymptoms =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        while (await reader.ReadAsync())
        {
            var symptom =
                reader["symptom_type"]?.ToString();

            if (!string.IsNullOrWhiteSpace(symptom))
                recurringSymptoms.Add(symptom);
        }

        return recurringSymptoms;
    }

    public async Task<int> GetRecurringIssueCountAsync()
    {
        var recurringSymptoms =
            await GetRecurringSymptomLookupAsync();

        return recurringSymptoms.Count;
    }

    public async Task<List<object>> GetSessionsAsync()
    {
        var recurringSymptoms =
            await GetRecurringSymptomLookupAsync();

        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            SELECT
                id,
                profile_id,
                symptom_type,
                affected_activity,
                frequency,
                severity,
                duration,
                recent_changes,
                system_state,
                warning_signs,
                diagnosed_category,
                action_category,
                confidence_label,
                ai_explanation,
                is_recurring,
                created_at,
                resolution_status,
                resolution_checked_at,
                resolution_summary,
                resolution_proof,
                last_action_status,
                last_action_summary,
                client_id
            FROM sessions
            ORDER BY created_at DESC
            LIMIT 100;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 60;

        await using var reader =
            await command.ExecuteReaderAsync();

        var sessions =
            new List<object>();

        while (await reader.ReadAsync())
        {
            sessions.Add(
                MapSession(
                    reader,
                    recurringSymptoms
                )
            );
        }

        return sessions;
    }

    public async Task<object?> GetSessionAsync(
        Guid sessionId)
    {
        var recurringSymptoms =
            await GetRecurringSymptomLookupAsync();

        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            SELECT
                id,
                profile_id,
                symptom_type,
                affected_activity,
                frequency,
                severity,
                duration,
                recent_changes,
                system_state,
                warning_signs,
                diagnosed_category,
                action_category,
                confidence_label,
                ai_explanation,
                is_recurring,
                created_at,
                resolution_status,
                resolution_checked_at,
                resolution_summary,
                resolution_proof,
                last_action_status,
                last_action_summary,
                client_id
            FROM sessions
            WHERE id = @id
            LIMIT 1;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 30;

        command.Parameters.AddWithValue(
            "id",
            sessionId
        );

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapSession(
            reader,
            recurringSymptoms
        );
    }

    private static object MapSession(
        NpgsqlDataReader reader,
        HashSet<string>? recurringSymptoms = null)
    {
        var resolutionProofJson =
            reader["resolution_proof"] == DBNull.Value
                ? "[]"
                : reader["resolution_proof"]?.ToString() ?? "[]";

        object resolutionProof;

        try
        {
            resolutionProof =
                JsonSerializer.Deserialize<object>(
                    resolutionProofJson
                )
                ?? Array.Empty<object>();
        }
        catch
        {
            resolutionProof =
                Array.Empty<object>();
        }

        var symptomType =
            GetString(
                reader,
                "symptom_type"
            );

        var databaseRecurring =
            reader["is_recurring"] != DBNull.Value &&
            Convert.ToBoolean(
                reader["is_recurring"]
            );

        var dynamicallyRecurring =
            recurringSymptoms != null &&
            recurringSymptoms.Contains(symptomType);

        return new
        {
            session_id =
                reader["id"].ToString(),

            profile_id =
                reader["profile_id"].ToString(),

            symptom_type =
                symptomType,

            affected_activity =
                GetString(reader, "affected_activity"),

            frequency =
                GetString(reader, "frequency"),

            severity =
                GetString(reader, "severity"),

            duration =
                GetString(reader, "duration"),

            recent_changes =
                GetString(reader, "recent_changes"),

            system_state =
                GetString(reader, "system_state"),

            warning_signs =
                GetString(reader, "warning_signs"),

            diagnosed_category =
                GetString(reader, "diagnosed_category"),

            action_category =
                GetString(reader, "action_category"),

            confidence_label =
                GetString(reader, "confidence_label"),

            ai_explanation =
                GetString(reader, "ai_explanation"),

            is_recurring =
                databaseRecurring ||
                dynamicallyRecurring,

            created_at =
                reader["created_at"] == DBNull.Value
                    ? null
                    : reader["created_at"],

            resolution_status =
                reader["resolution_status"] == DBNull.Value
                    ? "open"
                    : reader["resolution_status"]?.ToString()
                      ?? "open",

            resolution_checked_at =
                reader["resolution_checked_at"] == DBNull.Value
                    ? null
                    : reader["resolution_checked_at"],

            resolution_summary =
                GetString(reader, "resolution_summary"),

            resolution_proof =
                resolutionProof,

            last_action_status =
                reader["last_action_status"] == DBNull.Value
                    ? null
                    : reader["last_action_status"]?.ToString(),

            last_action_summary =
                GetString(reader, "last_action_summary"),

            client_id =
                GetString(reader, "client_id")
        };
    }

    private static string GetString(
        NpgsqlDataReader reader,
        string columnName)
    {
        return reader[columnName] == DBNull.Value
            ? string.Empty
            : reader[columnName]?.ToString()
              ?? string.Empty;
    }

    public async Task<int> GetRecommendationCountAsync()
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            SELECT COUNT(*)
            FROM recommendations;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 30;

        var result =
            await command.ExecuteScalarAsync();

        if (result == null || result == DBNull.Value)
            return 0;

        return Convert.ToInt32(result);
    }

    public async Task<object> GetDashboardSummaryAsync()
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            SELECT
                id,
                symptom_type,
                diagnosed_category,
                action_category,
                confidence_label,
                warning_signs,
                is_recurring,
                created_at,
                resolution_status,
                last_action_status,
                last_action_summary
            FROM sessions
            ORDER BY created_at DESC
            LIMIT 100;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 30;

        await using var reader =
            await command.ExecuteReaderAsync();

        var sessions =
            new List<DashboardSessionRecord>();

        while (await reader.ReadAsync())
        {
            sessions.Add(
                new DashboardSessionRecord
                {
                    Id =
                        reader["id"] == DBNull.Value
                            ? Guid.Empty
                            : (Guid)reader["id"],

                    SymptomType =
                        GetString(
                            reader,
                            "symptom_type"
                        ),

                    DiagnosedCategory =
                        GetString(
                            reader,
                            "diagnosed_category"
                        ),

                    ActionCategory =
                        GetString(
                            reader,
                            "action_category"
                        ),

                    ConfidenceLabel =
                        GetString(
                            reader,
                            "confidence_label"
                        ),

                    WarningSigns =
                        GetString(
                            reader,
                            "warning_signs"
                        ),

                    IsRecurring =
                        reader["is_recurring"] != DBNull.Value &&
                        Convert.ToBoolean(
                            reader["is_recurring"]
                        ),

                    CreatedAt =
                        reader["created_at"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                reader["created_at"]
                            ),

                    ResolutionStatus =
                        reader["resolution_status"] == DBNull.Value
                            ? "open"
                            : reader["resolution_status"]?.ToString()
                              ?? "open",

                    LastActionStatus =
                        reader["last_action_status"] == DBNull.Value
                            ? null
                            : reader["last_action_status"]?.ToString(),

                    LastActionSummary =
                        GetString(
                            reader,
                            "last_action_summary"
                        )
                }
            );
        }

        await reader.DisposeAsync();

        var now =
            DateTime.UtcNow;

        var latestSession =
            sessions
                .OrderByDescending(
                    session =>
                        session.CreatedAt
                )
                .FirstOrDefault();

        var totalSessions =
            sessions.Count;

        var thisMonthCount =
            sessions.Count(
                session =>
                    session.CreatedAt.HasValue &&
                    session.CreatedAt.Value.Year == now.Year &&
                    session.CreatedAt.Value.Month == now.Month
            );

        var escalatedCount =
            sessions.Count(
                session =>
                    NormalizeDashboardAction(
                        session.ActionCategory
                    ) == "Escalate"
            );

        var recurringIssuesCount =
            sessions
                .Where(
                    session =>
                        !string.IsNullOrWhiteSpace(
                            session.SymptomType
                        )
                )
                .GroupBy(
                    session =>
                        session.SymptomType,
                    StringComparer.OrdinalIgnoreCase
                )
                .Count(
                    group =>
                        group.Count() >= 2
                );

        var warningSessions =
            sessions
                .Where(
                    session =>
                        !string.IsNullOrWhiteSpace(
                            session.WarningSigns
                        ) &&
                        !string.Equals(
                            session.WarningSigns,
                            "none",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        var observedWarningTexts =
            await GetObservedWarningTextsAsync();

        var warningSignService =
            new WarningSignService();

        var warningSignsActiveCount =
            warningSignService
                .GetObservedReferenceCount(
                    observedWarningTexts
                );  

        var monitorCount =
            sessions.Count(
                session =>
                    NormalizeDashboardAction(
                        session.ActionCategory
                    ) == "Monitor"
            );

        var maintainCount =
            sessions.Count(
                session =>
                    NormalizeDashboardAction(
                        session.ActionCategory
                    ) == "Maintain"
            );

        var troubleshootCount =
            sessions.Count(
                session =>
                    NormalizeDashboardAction(
                        session.ActionCategory
                    ) == "Troubleshoot"
            );

        var escalateCount =
            sessions.Count(
                session =>
                    NormalizeDashboardAction(
                        session.ActionCategory
                    ) == "Escalate"
            );

        var sessionFrequency =
            sessions
                .Where(
                    session =>
                        session.CreatedAt.HasValue &&
                        session.CreatedAt.Value >=
                        now.AddDays(-30)
                )
                .GroupBy(
                    session =>
                        session.CreatedAt!.Value.Date
                )
                .OrderBy(
                    group =>
                        group.Key
                )
                .Select(
                    group => new
                    {
                        date =
                            group.Key.ToString(
                                "yyyy-MM-dd"
                            ),

                        label =
                            group.Key.ToString(
                                "MMM dd"
                            ),

                        count =
                            group.Count()
                    }
                )
                .ToArray();

        var warningReferenceRows =
            warningSignService
                .GetObservedReferenceRows(
                    observedWarningTexts
                )
                .OrderByDescending(
                    row =>
                        row.observed_count
                )
                .Take(3)
                .ToArray();

        var recentWarningSigns =
            warningReferenceRows
                .Select(
                    row => new
                    {
                        id =
                            row.id,

                        warning_sign =
                            row.warning_sign,

                        threshold =
                            row.threshold,

                        recommended_action =
                            row.action,

                        observed_count =
                            row.observed_count,

                        display_date =
                            (string?)null
                    }
                )
                .ToArray();

        object? lastDiagnosis = null;
        object? currentActionStatus = null;
        object? lastSavedSession = null;

        if (latestSession != null)
        {
            var createdAt =
                latestSession.CreatedAt ?? now;

            var daysAgo =
                Math.Max(
                    0,
                    (int)(
                        now.Date -
                        createdAt.Date
                    ).TotalDays
                );

            lastDiagnosis =
                new
                {
                    session_id =
                        latestSession.Id.ToString(),

                    days_ago =
                        daysAgo,

                    display_date =
                        createdAt
                            .ToLocalTime()
                            .ToString(
                                "MMM dd, yyyy"
                            ),

                    symptom_type =
                        latestSession.SymptomType,

                    diagnosed_category =
                        latestSession.DiagnosedCategory,

                    action_category =
                        NormalizeDashboardAction(
                            latestSession.ActionCategory
                        )
                };

            currentActionStatus =
                new
                {
                    action_category =
                        NormalizeDashboardAction(
                            latestSession.ActionCategory
                        ),

                    diagnosed_category =
                        latestSession.DiagnosedCategory,

                    resolution_status =
                        latestSession.ResolutionStatus,

                    last_action_status =
                        latestSession.LastActionStatus,

                    last_action_summary =
                        latestSession.LastActionSummary
                };

            lastSavedSession =
                new
                {
                    session_id =
                        latestSession.Id.ToString(),

                    symptom_type =
                        latestSession.SymptomType,

                    diagnosed_category =
                        latestSession.DiagnosedCategory,

                    action_category =
                        NormalizeDashboardAction(
                            latestSession.ActionCategory
                        ),

                    confidence_label =
                        latestSession.ConfidenceLabel,

                    display_date =
                        createdAt
                            .ToLocalTime()
                            .ToString(
                                "MMM dd, yyyy h:mm tt"
                            )
                };
        }

        return new
        {
            server_time =
                now.ToString("o"),

            totals =
                new
                {
                    total_sessions =
                        totalSessions,

                    this_month_count =
                        thisMonthCount,

                    escalated_count =
                        escalatedCount
                },

            last_diagnosis =
                lastDiagnosis,

            current_action_status =
                currentActionStatus,

            recurring_issues_count =
                recurringIssuesCount,

            warning_signs_active_count =
                warningSignsActiveCount,

            action_distribution =
                new[]
                {
                    new
                    {
                        label = "Monitor",
                        count = monitorCount
                    },

                    new
                    {
                        label = "Maintain",
                        count = maintainCount
                    },

                    new
                    {
                        label = "Troubleshoot",
                        count = troubleshootCount
                    },

                    new
                    {
                        label = "Escalate",
                        count = escalateCount
                    }
                },

            session_frequency =
                sessionFrequency,

            recent_warning_signs =
                recentWarningSigns,

            last_saved_session =
                lastSavedSession,

            database_warning =
                (string?)null
        };
    }

    public async Task<List<RecurringSessionDto>>
        GetRecurringSessionsAsync()
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        const string sql = """
            SELECT
                id,
                symptom_type,
                diagnosed_category,
                action_category,
                confidence_label,
                severity,
                frequency,
                duration,
                warning_signs,
                created_at
            FROM sessions
            ORDER BY created_at ASC;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.CommandTimeout = 60;

        await using var reader =
            await command.ExecuteReaderAsync();

        var sessions =
            new List<RecurringSessionDto>();

        while (await reader.ReadAsync())
        {
            var duration =
                GetString(
                    reader,
                    "duration"
                );

            sessions.Add(
                new RecurringSessionDto
                {
                    Id =
                        reader["id"] == DBNull.Value
                            ? Guid.Empty
                            : (Guid)reader["id"],

                    SymptomType =
                        GetString(
                            reader,
                            "symptom_type"
                        ),

                    DiagnosedCategory =
                        GetString(
                            reader,
                            "diagnosed_category"
                        ),

                    ActionCategory =
                        GetString(
                            reader,
                            "action_category"
                        ),

                    ConfidenceLabel =
                        GetString(
                            reader,
                            "confidence_label"
                        ),

                    Severity =
                        GetString(
                            reader,
                            "severity"
                        ),

                    Frequency =
                        GetString(
                            reader,
                            "frequency"
                        ),

                    Duration =
                        string.IsNullOrWhiteSpace(duration)
                            ? "N/A"
                            : duration,

                    WarningSigns =
                        GetString(
                            reader,
                            "warning_signs"
                        ),

                    CreatedAt =
                        reader["created_at"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                reader["created_at"]
                            )
                }
            );
        }

        return sessions;
    }

    public async Task<List<string>>
        GetObservedWarningTextsAsync()
    {
        await using var connection =
            new NpgsqlConnection(GetConnectionString());

        await connection.OpenAsync();

        var observedTexts =
            new List<string>();

        const string recommendationSql = """
            SELECT warning_sign
            FROM recommendations
            WHERE
                warning_sign IS NOT NULL
                AND TRIM(warning_sign) <> '';
            """;

        await using (
            var recommendationCommand =
                new NpgsqlCommand(
                    recommendationSql,
                    connection
                )
        )
        {
            recommendationCommand.CommandTimeout =
                30;

            await using var recommendationReader =
                await recommendationCommand
                    .ExecuteReaderAsync();

            while (
                await recommendationReader
                    .ReadAsync()
            )
            {
                var warning =
                    recommendationReader[
                        "warning_sign"
                    ]?.ToString();

                if (!string.IsNullOrWhiteSpace(warning))
                    observedTexts.Add(warning);
            }
        }

        const string sessionSql = """
            SELECT warning_signs
            FROM sessions
            WHERE
                diagnosed_category <>
                    'No active issue detected'
                AND warning_signs IS NOT NULL
                AND TRIM(warning_signs) <> '';
            """;

        await using (
            var sessionCommand =
                new NpgsqlCommand(
                    sessionSql,
                    connection
                )
        )
        {
            sessionCommand.CommandTimeout =
                30;

            await using var sessionReader =
                await sessionCommand
                    .ExecuteReaderAsync();

            while (
                await sessionReader.ReadAsync()
            )
            {
                var raw =
                    sessionReader[
                        "warning_signs"
                    ]?.ToString();

                foreach (
                    var warning
                    in WarningSignService
                        .SplitWarningSigns(raw)
                )
                {
                    observedTexts.Add(warning);
                }
            }
        }

        return observedTexts;
    }

    private async Task<Guid> GetOrCreateProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HardwareProfileDto hardware)
    {
        var clientId =
            string.IsNullOrWhiteSpace(
                hardware.DeviceName
            )
                ? "legacy"
                : hardware.DeviceName;

        const string findSql = """
            SELECT id
            FROM profiles
            WHERE client_id = @client_id
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using (
            var findCommand =
                new NpgsqlCommand(
                    findSql,
                    connection,
                    transaction
                )
        )
        {
            findCommand.CommandTimeout = 30;

            findCommand.Parameters.AddWithValue(
                "client_id",
                clientId
            );

            var existing =
                await findCommand.ExecuteScalarAsync();

            if (existing is Guid existingId)
            {
                await UpdateProfileAsync(
                    connection,
                    transaction,
                    existingId,
                    hardware
                );

                return existingId;
            }
        }

        var profileId =
            Guid.NewGuid();

        const string insertSql = """
            INSERT INTO profiles
            (
                id,
                cpu_model,
                ram_capacity,
                storage_type,
                storage_capacity,
                storage_details,
                os_version,
                gpu_driver,
                chipset_driver,
                system_age,
                client_id
            )
            VALUES
            (
                @id,
                @cpu_model,
                @ram_capacity,
                @storage_type,
                @storage_capacity,
                @storage_details::jsonb,
                @os_version,
                @gpu_driver,
                @chipset_driver,
                @system_age,
                @client_id
            );
            """;

        await using var command =
            new NpgsqlCommand(
                insertSql,
                connection,
                transaction
            );

        command.CommandTimeout = 30;

        AddProfileParameters(
            command,
            profileId,
            hardware,
            clientId
        );

        await command.ExecuteNonQueryAsync();

        return profileId;
    }

    private async Task UpdateProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid profileId,
        HardwareProfileDto hardware)
    {
        const string sql = """
            UPDATE profiles
            SET
                cpu_model = @cpu_model,
                ram_capacity = @ram_capacity,
                storage_type = @storage_type,
                storage_capacity = @storage_capacity,
                storage_details = @storage_details::jsonb,
                os_version = @os_version,
                gpu_driver = @gpu_driver,
                chipset_driver = @chipset_driver,
                system_age = @system_age
            WHERE id = @id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction
            );

        command.CommandTimeout = 30;

        var clientId =
            string.IsNullOrWhiteSpace(
                hardware.DeviceName
            )
                ? "legacy"
                : hardware.DeviceName;

        AddProfileParameters(
            command,
            profileId,
            hardware,
            clientId
        );

        await command.ExecuteNonQueryAsync();
    }

    private static void AddProfileParameters(
        NpgsqlCommand command,
        Guid profileId,
        HardwareProfileDto hardware,
        string clientId)
    {
        var totalStorage =
            hardware.StorageDrives.Sum(
                drive =>
                    drive.SizeGb
            );

        command.Parameters.AddWithValue(
            "id",
            profileId
        );

        command.Parameters.AddWithValue(
            "cpu_model",
            hardware.Cpu.Name ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "ram_capacity",
            $"{hardware.Ram.TotalGb:0.##} GB"
        );

        command.Parameters.AddWithValue(
            "storage_type",
            hardware.PrimaryStorageType
            ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "storage_capacity",
            $"{totalStorage:0.##} GB"
        );

        command.Parameters.AddWithValue(
            "storage_details",
            JsonSerializer.Serialize(
                hardware.StorageDrives
            )
        );

        command.Parameters.AddWithValue(
            "os_version",
            hardware.OsVersion
            ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "gpu_driver",
            hardware.Gpu.Driver
            ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "chipset_driver",
            hardware.ChipsetDriver
            ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "system_age",
            hardware.SystemAge
            ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "client_id",
            clientId
        );
    }

    private static string NormalizeDashboardAction(
        string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "Monitor";

        if (
            action.Contains(
                "escalate",
                StringComparison.OrdinalIgnoreCase
            ) ||
            action.Contains(
                "professional",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "Escalate";
        }

        if (
            action.Contains(
                "troubleshoot",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "Troubleshoot";
        }

        if (
            action.Contains(
                "maintain",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "Maintain";
        }

        if (
            action.Contains(
                "monitor",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "Monitor";
        }

        return "Monitor";
    }

    private sealed class DashboardSessionRecord
    {
        public Guid Id { get; init; }

        public string SymptomType { get; init; } =
            string.Empty;

        public string DiagnosedCategory { get; init; } =
            string.Empty;

        public string ActionCategory { get; init; } =
            string.Empty;

        public string ConfidenceLabel { get; init; } =
            string.Empty;

        public string WarningSigns { get; init; } =
            string.Empty;

        public bool IsRecurring { get; init; }

        public DateTime? CreatedAt { get; init; }

        public string ResolutionStatus { get; init; } =
            "open";

        public string? LastActionStatus { get; init; }

        public string LastActionSummary { get; init; } =
            string.Empty;
    }

    public async Task<ProfileResponseDto> SaveProfileAsync(
    SaveProfileRequestDto request)
{
    await using var connection =
        new NpgsqlConnection(GetConnectionString());

    await connection.OpenAsync();

    const string findSql = """
        SELECT
            id,
            cpu_model,
            ram_capacity,
            storage_type,
            storage_capacity,
            storage_details,
            os_version,
            gpu_driver,
            chipset_driver,
            system_age,
            created_at
        FROM profiles
        WHERE
            cpu_model = @cpu_model
            AND ram_capacity = @ram_capacity
            AND storage_type = @storage_type
            AND storage_capacity = @storage_capacity
            AND os_version = @os_version
        ORDER BY created_at DESC
        LIMIT 1;
        """;

    await using (
        var findCommand =
            new NpgsqlCommand(findSql, connection)
    )
    {
        findCommand.Parameters.AddWithValue(
            "cpu_model",
            request.cpu_model
        );

        findCommand.Parameters.AddWithValue(
            "ram_capacity",
            request.ram_capacity
        );

        findCommand.Parameters.AddWithValue(
            "storage_type",
            request.storage_type
        );

        findCommand.Parameters.AddWithValue(
            "storage_capacity",
            request.storage_capacity
        );

        findCommand.Parameters.AddWithValue(
            "os_version",
            request.os_version
        );

        await using var reader =
            await findCommand.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var existingId =
                reader.GetGuid(
                    reader.GetOrdinal("id")
                );

            await reader.DisposeAsync();

            if (request.storage_details != null)
            {
                const string updateStorageSql = """
                    UPDATE profiles
                    SET storage_details = @storage_details::jsonb
                    WHERE id = @id;
                    """;

                await using var updateCommand =
                    new NpgsqlCommand(
                        updateStorageSql,
                        connection
                    );

                updateCommand.Parameters.AddWithValue(
                    "storage_details",
                    JsonSerializer.Serialize(
                        request.storage_details
                    )
                );

                updateCommand.Parameters.AddWithValue(
                    "id",
                    existingId
                );

                await updateCommand.ExecuteNonQueryAsync();
            }

            return await GetProfileByIdAsync(
                connection,
                existingId
            );
        }
    }

    var profileId =
        Guid.NewGuid();

    const string insertSql = """
        INSERT INTO profiles
        (
            id,
            cpu_model,
            ram_capacity,
            storage_type,
            storage_capacity,
            storage_details,
            os_version,
            gpu_driver,
            chipset_driver,
            system_age,
            client_id
        )
        VALUES
        (
            @id,
            @cpu_model,
            @ram_capacity,
            @storage_type,
            @storage_capacity,
            @storage_details::jsonb,
            @os_version,
            @gpu_driver,
            @chipset_driver,
            @system_age,
            'legacy'
        );
        """;

    await using var insertCommand =
        new NpgsqlCommand(
            insertSql,
            connection
        );

    insertCommand.Parameters.AddWithValue(
        "id",
        profileId
    );

    insertCommand.Parameters.AddWithValue(
        "cpu_model",
        request.cpu_model
    );

    insertCommand.Parameters.AddWithValue(
        "ram_capacity",
        request.ram_capacity
    );

    insertCommand.Parameters.AddWithValue(
        "storage_type",
        request.storage_type
    );

    insertCommand.Parameters.AddWithValue(
        "storage_capacity",
        request.storage_capacity
    );

    insertCommand.Parameters.AddWithValue(
        "storage_details",
        JsonSerializer.Serialize(
            request.storage_details
        )
    );

    insertCommand.Parameters.AddWithValue(
        "os_version",
        request.os_version
    );

    insertCommand.Parameters.AddWithValue(
        "gpu_driver",
        (object?)request.gpu_driver
        ?? DBNull.Value
    );

    insertCommand.Parameters.AddWithValue(
        "chipset_driver",
        (object?)request.chipset_driver
        ?? DBNull.Value
    );

    insertCommand.Parameters.AddWithValue(
        "system_age",
        (object?)request.system_age
        ?? DBNull.Value
    );

    await insertCommand.ExecuteNonQueryAsync();

    return await GetProfileByIdAsync(
        connection,
        profileId
    );
}

private static async Task<ProfileResponseDto>
    GetProfileByIdAsync(
        NpgsqlConnection connection,
        Guid profileId)
{
    const string sql = """
        SELECT
            id,
            cpu_model,
            ram_capacity,
            storage_type,
            storage_capacity,
            storage_details,
            os_version,
            gpu_driver,
            chipset_driver,
            system_age,
            created_at
        FROM profiles
        WHERE id = @id
        LIMIT 1;
        """;

    await using var command =
        new NpgsqlCommand(
            sql,
            connection
        );

    command.Parameters.AddWithValue(
        "id",
        profileId
    );

    await using var reader =
        await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
        throw new InvalidOperationException(
            "Saved profile could not be loaded."
        );

    object? storageDetails =
        null;

    if (
        reader["storage_details"]
        != DBNull.Value
    )
    {
        try
        {
            storageDetails =
                JsonSerializer.Deserialize<object>(
                    reader["storage_details"]
                        .ToString()!
                );
        }
        catch
        {
            storageDetails = null;
        }
    }

    return new ProfileResponseDto
    {
        id =
            (Guid)reader["id"],

        cpu_model =
            reader["cpu_model"]
                .ToString()
            ?? string.Empty,

        ram_capacity =
            reader["ram_capacity"]
                .ToString()
            ?? string.Empty,

        storage_type =
            reader["storage_type"]
                .ToString()
            ?? string.Empty,

        storage_capacity =
            reader["storage_capacity"]
                .ToString()
            ?? string.Empty,

        storage_details =
            storageDetails,

        os_version =
            reader["os_version"]
                .ToString()
            ?? string.Empty,

        gpu_driver =
            reader["gpu_driver"]
                == DBNull.Value
                ? null
                : reader["gpu_driver"]
                    .ToString(),

        chipset_driver =
            reader["chipset_driver"]
                == DBNull.Value
                ? null
                : reader["chipset_driver"]
                    .ToString(),

        system_age =
            reader["system_age"]
                == DBNull.Value
                ? null
                : reader["system_age"]
                    .ToString(),

        created_at =
            Convert.ToDateTime(
                reader["created_at"]
            )
    };
    }
}