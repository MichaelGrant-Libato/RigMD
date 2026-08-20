using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;
using RigMD.Domain.Rules;
using System.Text.Json;

namespace RigMD.Infrastructure.Persistence;

/// <summary>
/// EF Core + SQLite implementation of IDiagnosticSessionRepository.
/// Replaces the Npgsql/Supabase DatabaseSessionService for local offline-first persistence.
/// </summary>
public class DiagnosticSessionRepository : IDiagnosticSessionRepository
{
    private readonly RigMdDbContext _db;

    public DiagnosticSessionRepository(RigMdDbContext db)
    {
        _db = db;
    }

    // ---------------------------------------------------------------
    // SAVE
    // ---------------------------------------------------------------

    public async Task<Guid> SaveDiagnosisAsync(
        DiagnosticSymptomPayload payload,
        HardwareProfileDto hardware,
        string diagnosedCategory,
        string actionCategory,
        string confidenceLabel,
        string aiExplanation,
        string clientId = "")
    {
        // Upsert system profile by fingerprinting cpu name + os
        var cpuName = hardware.Cpu.Name;
        var osVersion = hardware.OsVersion;

        var profile = await _db.SystemProfiles.FirstOrDefaultAsync(p =>
            p.CpuModel == cpuName &&
            p.OsVersion == osVersion);

        if (profile == null)
        {
            var primaryDrive = hardware.StorageDrives.FirstOrDefault();
            profile = new SystemProfile
            {
                CpuModel = cpuName,
                RamCapacity = $"{hardware.Ram.TotalGb:0.#} GB",
                StorageType = hardware.PrimaryStorageType,
                StorageCapacity = primaryDrive != null ? $"{primaryDrive.SizeGb:0.#} GB" : string.Empty,
                StorageDetails = hardware.StorageDrives.Count > 0
                    ? JsonSerializer.Serialize(hardware.StorageDrives)
                    : null,
                OsVersion = osVersion,
                GpuDriver = hardware.Gpu.Driver,
                ChipsetDriver = hardware.ChipsetDriver,
                SystemAge = hardware.SystemAge
            };
            _db.SystemProfiles.Add(profile);
        }

        // Build session entity
        var session = new DiagnosticSession
        {
            SystemProfileId = profile.Id,
            Profile = profile
        };

        // Persist answers as key-value pairs
        var answers = new List<SessionAnswer>
        {
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "symptom_type",       AnswerValue = payload.SymptomType },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "affected_activity",  AnswerValue = payload.AffectedActivity },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "frequency",          AnswerValue = payload.Frequency },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "severity",           AnswerValue = payload.Severity },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "duration",           AnswerValue = payload.Duration },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "recent_changes",     AnswerValue = payload.RecentChanges },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "system_state",       AnswerValue = payload.SystemState },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "warning_signs",      AnswerValue = payload.WarningSigns },
            new() { DiagnosticSessionId = session.Id, Session = session, QuestionKey = "client_id",          AnswerValue = clientId }
        };

        session.Answers = answers;

        // Build diagnostic output entity
        var output = new DiagnosticOutput
        {
            DiagnosticSessionId = session.Id,
            Session = session,
            DiagnosedCategory = diagnosedCategory,
            ActionCategory = actionCategory,
            ConfidenceLabel = confidenceLabel,
            AiExplanation = aiExplanation
        };
        session.Output = output;

        _db.DiagnosticSessions.Add(session);
        await _db.SaveChangesAsync();

        return session.Id;
    }

    // ---------------------------------------------------------------
    // READ
    // ---------------------------------------------------------------

    public async Task<IReadOnlyList<DiagnosticSessionDto>> GetSessionsAsync()
    {
        var sessions = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return sessions.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<DiagnosticSessionDto?> GetSessionAsync(Guid sessionId)
    {
        var session = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        return session == null ? null : MapToDto(session);
    }

    // ---------------------------------------------------------------
    // UPDATE
    // ---------------------------------------------------------------

    public async Task<bool> UpdateResolutionAsync(
        Guid sessionId,
        string resolutionStatus,
        string resolutionCheckedAt,
        string resolutionSummary,
        object[] resolutionProof)
    {
        var session = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        SetOrAddAnswer(session, "resolution_status", resolutionStatus);
        SetOrAddAnswer(session, "resolution_checked_at", resolutionCheckedAt);
        SetOrAddAnswer(session, "resolution_summary", resolutionSummary);
        SetOrAddAnswer(session, "resolution_proof", JsonSerializer.Serialize(resolutionProof));

        foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged))
        {
            Console.WriteLine($"State: {entry.State}, Entity: {entry.Entity.GetType().Name}");
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkNeedsRecheckAsync(Guid sessionId)
    {
        var session = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        SetOrAddAnswer(session, "resolution_status", "needs_recheck");
        SetOrAddAnswer(session, "last_action_status", "completed");
        SetOrAddAnswer(session, "last_action_summary",
            "A safe action was performed. Run a follow-up check to see if the issue improved.");

        await _db.SaveChangesAsync();
        return true;
    }

    // ---------------------------------------------------------------
    // DASHBOARD / AGGREGATES
    // ---------------------------------------------------------------

    public async Task<object> GetDashboardSummaryAsync()
    {
        var total = await _db.DiagnosticSessions.CountAsync();

        var resolved = await _db.DiagnosticSessions
            .Where(s => s.Answers.Any(a =>
                a.QuestionKey == "resolution_status" && a.AnswerValue == "resolved"))
            .CountAsync();

        var needsRecheck = await _db.DiagnosticSessions
            .Where(s => s.Answers.Any(a =>
                a.QuestionKey == "resolution_status" && a.AnswerValue == "needs_recheck"))
            .CountAsync();

        var open = total - resolved - needsRecheck;

        var recentSessions = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new
            {
                session_id = s.Id.ToString(),
                diagnosed_category = s.Output != null ? s.Output.DiagnosedCategory : string.Empty,
                confidence_label = s.Output != null ? s.Output.ConfidenceLabel : string.Empty,
                resolution_status = s.Answers
                    .Where(a => a.QuestionKey == "resolution_status")
                    .Select(a => a.AnswerValue)
                    .FirstOrDefault() ?? "open",
                created_at = s.CreatedAt.ToString("o")
            })
            .ToListAsync();

        return new
        {
            total_sessions = total,
            resolved_sessions = resolved,
            needs_recheck_sessions = needsRecheck,
            open_sessions = open,
            recent_sessions = recentSessions
        };
    }

    public async Task<object> GetRecurringPatternsAsync()
    {
        var sessions = await _db.DiagnosticSessions
            .Include(s => s.Output)
            .Include(s => s.Answers)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var grouped = sessions
            .Where(s => s.Output != null)
            .GroupBy(s => s.Output!.DiagnosedCategory)
            .Select(g => new
            {
                category = g.Key,
                count = g.Count(),
                is_recurring = g.Count() > 1,
                sessions = g.Select(s => new
                {
                    session_id = s.Id.ToString(),
                    created_at = s.CreatedAt.ToString("o"),
                    resolution_status = s.Answers
                        .Where(a => a.QuestionKey == "resolution_status")
                        .Select(a => a.AnswerValue).FirstOrDefault() ?? "open"
                }).ToList()
            })
            .OrderByDescending(g => g.count)
            .ToList();

        return new { patterns = grouped };
    }

    public async Task<object> GetWarningSignsAsync()
    {
        var sessions = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var warnings = sessions.Select(s => new
        {
            session_id = s.Id.ToString(),
            warning_signs = s.Answers
                .Where(a => a.QuestionKey == "warning_signs")
                .Select(a => a.AnswerValue)
                .FirstOrDefault() ?? string.Empty,
            diagnosed_category = s.Output?.DiagnosedCategory ?? string.Empty,
            created_at = s.CreatedAt.ToString("o")
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.warning_signs))
        .ToList();

        return new { warning_signs = warnings };
    }

    public async Task<object> GetProfilesAsync()
    {
        var profiles = await _db.SystemProfiles
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id.ToString(),
                cpu_model = p.CpuModel,
                ram_capacity = p.RamCapacity,
                storage_type = p.StorageType,
                storage_capacity = p.StorageCapacity,
                os_version = p.OsVersion,
                gpu_driver = p.GpuDriver,
                chipset_driver = p.ChipsetDriver,
                system_age = p.SystemAge,
                created_at = p.CreatedAt.ToString("o")
            })
            .ToListAsync();

        return new { profiles };
    }

    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------

    private static DiagnosticSessionDto MapToDto(DiagnosticSession s)
    {
        string Answer(string key) =>
            s.Answers.FirstOrDefault(a => a.QuestionKey == key)?.AnswerValue ?? string.Empty;

        return new DiagnosticSessionDto
        {
            SessionId = s.Id.ToString(),
            ProfileId = s.SystemProfileId.ToString(),
            SymptomType = Answer("symptom_type"),
            AffectedActivity = Answer("affected_activity"),
            Frequency = Answer("frequency"),
            Severity = Answer("severity"),
            Duration = Answer("duration"),
            RecentChanges = Answer("recent_changes"),
            SystemState = Answer("system_state"),
            WarningSigns = Answer("warning_signs"),
            DiagnosedCategory = s.Output?.DiagnosedCategory ?? string.Empty,
            ActionCategory = s.Output?.ActionCategory ?? string.Empty,
            ConfidenceLabel = s.Output?.ConfidenceLabel ?? string.Empty,
            AiExplanation = s.Output?.AiExplanation ?? string.Empty,
            ResolutionStatus = Answer("resolution_status"),
            ResolutionSummary = Answer("resolution_summary"),
            LastActionStatus = Answer("last_action_status"),
            LastActionSummary = Answer("last_action_summary"),
            ClientId = Answer("client_id"),
            CreatedAt = s.CreatedAt.UtcDateTime
        };
    }

    private void SetOrAddAnswer(DiagnosticSession session, string key, string value)
    {
        var existing = session.Answers.FirstOrDefault(a => a.QuestionKey == key);
        if (existing != null)
        {
            existing.AnswerValue = value;
        }
        else
        {
            var newAnswer = new SessionAnswer
            {
                DiagnosticSessionId = session.Id,
                QuestionKey = key,
                AnswerValue = value
            };
            session.Answers.Add(newAnswer);
            _db.SessionAnswers.Add(newAnswer); // Explicitly mark as Added to bypass Guid key heuristics
        }
    }

    public async Task<IReadOnlyList<RecurringSessionDto>> GetRecurringSessionsAsync()
    {
        var sessions = await _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return sessions.Select(s => new RecurringSessionDto
        {
            Id = s.Id,
            SymptomType = s.Answers.FirstOrDefault(a => a.QuestionKey == "symptom_type")?.AnswerValue ?? string.Empty,
            DiagnosedCategory = s.Output?.DiagnosedCategory ?? string.Empty,
            ActionCategory = s.Output?.ActionCategory ?? string.Empty,
            ConfidenceLabel = s.Output?.ConfidenceLabel ?? string.Empty,
            Severity = s.Answers.FirstOrDefault(a => a.QuestionKey == "severity")?.AnswerValue ?? string.Empty,
            Frequency = s.Answers.FirstOrDefault(a => a.QuestionKey == "frequency")?.AnswerValue ?? string.Empty,
            Duration = s.Answers.FirstOrDefault(a => a.QuestionKey == "duration")?.AnswerValue ?? "N/A",
            WarningSigns = s.Answers.FirstOrDefault(a => a.QuestionKey == "warning_signs")?.AnswerValue ?? string.Empty,
            CreatedAt = s.CreatedAt.UtcDateTime
        }).ToList().AsReadOnly();
    }

    public async Task<IEnumerable<string>> GetObservedWarningTextsAsync()
    {
        return await _db.SessionAnswers
            .Where(a => a.QuestionKey == "warning_signs" && !string.IsNullOrWhiteSpace(a.AnswerValue))
            .Select(a => a.AnswerValue)
            .Distinct()
            .ToListAsync();
    }
}

