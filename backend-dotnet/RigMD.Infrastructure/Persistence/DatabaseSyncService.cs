using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using RigMD.Domain.Entities;

namespace RigMD.Infrastructure.Persistence;

/// <summary>
/// Service responsible for syncing data between the remote Supabase PostgreSQL database
/// and the local SQLite database. Runs on backend startup to ensure existing history is available.
/// </summary>
public class DatabaseSyncService
{
    private readonly RigMdDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseSyncService> _logger;

    public DatabaseSyncService(
        RigMdDbContext db,
        IConfiguration config,
        ILogger<DatabaseSyncService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    private string? GetNpgsqlConnectionString()
    {
        var databaseUrl = _config["DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl) || databaseUrl.Contains("[YOUR-PASSWORD]"))
            return null;

        try
        {
            var uri = new Uri(databaseUrl);
            var userInfo = uri.UserInfo.Split(':', 2);
            if (userInfo.Length != 2) return null;

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.TrimStart('/'),
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = Uri.UnescapeDataString(userInfo[1]),
                SslMode = SslMode.Require,
                Pooling = true,
                Timeout = 10,
                CommandTimeout = 15
            };
            return builder.ConnectionString;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DATABASE_URL.");
            return null;
        }
    }

    public async Task SyncFromPostgresAsync()
    {
        var connectionString = GetNpgsqlConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogInformation("No valid DATABASE_URL found. Skipping Supabase database sync.");
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to Supabase PostgreSQL to synchronize existing records...");
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Sync Profiles
            await using (var cmd = new NpgsqlCommand(
                "SELECT id, cpu_model, ram_capacity, storage_type, storage_capacity, os_version, gpu_driver, chipset_driver, system_age, created_at, storage_details, client_id FROM profiles;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0);
                    var existing = await _db.SystemProfiles.FirstOrDefaultAsync(p => p.Id == id);
                    if (existing == null)
                    {
                        var profile = new SystemProfile
                        {
                            Id = id,
                            CpuModel = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            RamCapacity = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            StorageType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            StorageCapacity = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                            OsVersion = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                            GpuDriver = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                            ChipsetDriver = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                            SystemAge = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                            CreatedAt = reader.IsDBNull(9) ? DateTimeOffset.UtcNow : reader.GetDateTime(9),
                            StorageDetails = reader.IsDBNull(10) ? null : reader.GetString(10)
                        };
                        _db.SystemProfiles.Add(profile);
                    }
                }
            }
            await _db.SaveChangesAsync();

            // 2. Sync Sessions
            await using (var cmd = new NpgsqlCommand(
                @"SELECT id, profile_id, symptom_type, affected_activity, frequency, severity, duration, 
                         recent_changes, system_state, warning_signs, diagnosed_category, action_category, 
                         confidence_label, ai_explanation, is_recurring, created_at, resolution_status, 
                         resolution_checked_at, resolution_summary, resolution_proof, last_action_status, 
                         last_action_summary, client_id 
                  FROM sessions;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sessionId = reader.GetGuid(0);
                    var existingSession = await _db.DiagnosticSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                    if (existingSession == null)
                    {
                        var profileId = reader.IsDBNull(1) ? Guid.Empty : reader.GetGuid(1);
                        var createdAt = reader.IsDBNull(15) ? DateTimeOffset.UtcNow : reader.GetDateTime(15);

                        var session = new DiagnosticSession
                        {
                            Id = sessionId,
                            SystemProfileId = profileId,
                            CreatedAt = createdAt
                        };

                        var answers = new List<SessionAnswer>
                        {
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "symptom_type", AnswerValue = reader.IsDBNull(2) ? "" : reader.GetString(2) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "affected_activity", AnswerValue = reader.IsDBNull(3) ? "" : reader.GetString(3) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "frequency", AnswerValue = reader.IsDBNull(4) ? "" : reader.GetString(4) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "severity", AnswerValue = reader.IsDBNull(5) ? "" : reader.GetString(5) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "duration", AnswerValue = reader.IsDBNull(6) ? "N/A" : reader.GetString(6) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "recent_changes", AnswerValue = reader.IsDBNull(7) ? "" : reader.GetString(7) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "system_state", AnswerValue = reader.IsDBNull(8) ? "" : reader.GetString(8) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "warning_signs", AnswerValue = reader.IsDBNull(9) ? "" : reader.GetString(9) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "resolution_status", AnswerValue = reader.IsDBNull(16) ? "open" : reader.GetString(16) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "resolution_checked_at", AnswerValue = reader.IsDBNull(17) ? "" : reader.GetDateTime(17).ToString("o") },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "resolution_summary", AnswerValue = reader.IsDBNull(18) ? "" : reader.GetString(18) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "resolution_proof", AnswerValue = reader.IsDBNull(19) ? "[]" : reader.GetString(19) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "last_action_status", AnswerValue = reader.IsDBNull(20) ? "" : reader.GetString(20) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "last_action_summary", AnswerValue = reader.IsDBNull(21) ? "" : reader.GetString(21) },
                            new() { DiagnosticSessionId = sessionId, QuestionKey = "client_id", AnswerValue = reader.IsDBNull(22) ? "" : reader.GetString(22) }
                        };
                        session.Answers = answers;

                        var output = new DiagnosticOutput
                        {
                            DiagnosticSessionId = sessionId,
                            DiagnosedCategory = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            ActionCategory = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            ConfidenceLabel = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            AiExplanation = reader.IsDBNull(13) ? "" : reader.GetString(13)
                        };
                        session.Output = output;

                        _db.DiagnosticSessions.Add(session);
                    }
                }
            }
            await _db.SaveChangesAsync();
            _logger.LogInformation("Successfully synced Supabase PostgreSQL database with local SQLite database.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to synchronize with Supabase PostgreSQL database. Continuing with local SQLite.");
        }
    }
}
