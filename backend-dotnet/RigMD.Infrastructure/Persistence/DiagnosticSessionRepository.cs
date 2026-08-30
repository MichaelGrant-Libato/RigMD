using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;
using RigMD.Domain.Rules;
using System.Text.Json;

namespace RigMD.Infrastructure.Persistence;

/// <summary>
/// EF Core + SQLite implementation of IDiagnosticSessionRepository.
/// Scopes all queries and writes to the current client identity for strict data isolation.
/// </summary>
public class DiagnosticSessionRepository : IDiagnosticSessionRepository
{
    private readonly RigMdDbContext _db;
    private readonly ICurrentClientProvider _clientProvider;

    public DiagnosticSessionRepository(RigMdDbContext db, ICurrentClientProvider clientProvider)
    {
        _db = db;
        _clientProvider = clientProvider;
    }

    private string GetCurrentClientId() => _clientProvider.GetCurrentClientId();

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
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = GetCurrentClientId();
        }

        // Upsert system profile by fingerprinting cpu name + os + client_id
        var cpuName = hardware.Cpu.Name;
        var osVersion = hardware.OsVersion;

        var profile = await _db.SystemProfiles.FirstOrDefaultAsync(p =>
            (string.IsNullOrEmpty(clientId) || p.ClientId == clientId) &&
            p.CpuModel == cpuName &&
            p.OsVersion == osVersion);

        if (profile == null)
        {
            var primaryDrive = hardware.StorageDrives.FirstOrDefault();
            profile = new SystemProfile
            {
                ClientId = clientId,
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

    public async Task<Guid> SaveAutomaticDiagnosisAsync(
    HardwareProfileDto hardware,
    string diagnosisMode,
    IReadOnlyList<string> componentIds,
    string? scenarioId,
    Guid commandId,
    string agentId,
    string diagnosedCategory,
    string actionCategory,
    string confidenceLabel,
    string explanation,
    string clientId = "")
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId =
                GetCurrentClientId();
        }

        var cpuName =
            hardware.Cpu.Name;

        var osVersion =
            hardware.OsVersion;

        var profile =
            await _db.SystemProfiles
                .FirstOrDefaultAsync(
                    p =>
                        (string.IsNullOrEmpty(clientId) ||
                        p.ClientId == clientId) &&
                        p.CpuModel == cpuName &&
                        p.OsVersion == osVersion);

        if (profile == null)
        {
            var primaryDrive =
                hardware.StorageDrives
                    .FirstOrDefault();

            profile =
                new SystemProfile
                {
                    ClientId =
                        clientId,

                    CpuModel =
                        cpuName,

                    RamCapacity =
                        $"{hardware.Ram.TotalGb:0.#} GB",

                    StorageType =
                        hardware.PrimaryStorageType,

                    StorageCapacity =
                        primaryDrive != null
                            ? $"{primaryDrive.SizeGb:0.#} GB"
                            : string.Empty,

                    StorageDetails =
                        hardware.StorageDrives.Count > 0
                            ? JsonSerializer.Serialize(
                                hardware.StorageDrives)
                            : null,

                    OsVersion =
                        osVersion,

                    GpuDriver =
                        hardware.Gpu.Driver,

                    ChipsetDriver =
                        hardware.ChipsetDriver,

                    SystemAge =
                        hardware.SystemAge
                };

            _db.SystemProfiles.Add(profile);
        }

        var session =
            new DiagnosticSession
            {
                SystemProfileId =
                    profile.Id,

                Profile =
                    profile
            };

        var answers =
            new List<SessionAnswer>
            {
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "diagnosis_mode",
                    AnswerValue = diagnosisMode
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "component_ids",
                    AnswerValue = JsonSerializer.Serialize(componentIds)
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "scenario_id",
                    AnswerValue = scenarioId ?? string.Empty
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "agent_id",
                    AnswerValue = agentId
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "agent_command_id",
                    AnswerValue = commandId.ToString()
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "client_id",
                    AnswerValue = clientId
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "resolution_status",
                    AnswerValue = "open"
                }
            };

        session.Answers =
            answers;

        var output =
            new DiagnosticOutput
            {
                DiagnosticSessionId =
                    session.Id,

                Session =
                    session,

                DiagnosedCategory =
                    diagnosedCategory,

                ActionCategory =
                    actionCategory,

                ConfidenceLabel =
                    confidenceLabel,

                AiExplanation =
                    explanation
            };

        session.Output =
            output;

        _db.DiagnosticSessions.Add(
            session);

        await _db.SaveChangesAsync();

        return session.Id;
    }

    // ---------------------------------------------------------------
    // READ
    // ---------------------------------------------------------------

    public async Task<IReadOnlyList<DiagnosticSessionDto>> GetSessionsAsync()
    {
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var sessions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
        return sessions.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<DiagnosticSessionDto?> GetSessionAsync(Guid sessionId)
    {
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .Where(s => s.Id == sessionId);

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var session = await query.FirstOrDefaultAsync();
        return session == null ? null : MapToDto(session);
    }

    public async Task<DiagnosticOutput?> GetDiagnosticOutputAsync(Guid sessionId)
    {
        var clientId = GetCurrentClientId();

        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .Where(s => s.Id == sessionId);

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s =>
                s.Profile.ClientId == clientId ||
                s.Answers.Any(a =>
                    a.QuestionKey == "client_id" &&
                    a.AnswerValue == clientId));
        }

        var session = await query.FirstOrDefaultAsync();

        return session?.Output;
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
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Profile)
            .Where(s => s.Id == sessionId);

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var session = await query.FirstOrDefaultAsync();
        if (session == null) return false;

        SetOrAddAnswer(session, "resolution_status", resolutionStatus);
        SetOrAddAnswer(session, "resolution_checked_at", resolutionCheckedAt);
        SetOrAddAnswer(session, "resolution_summary", resolutionSummary);
        SetOrAddAnswer(session, "resolution_proof", JsonSerializer.Serialize(resolutionProof));

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkNeedsRecheckAsync(Guid sessionId)
    {
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Profile)
            .Where(s => s.Id == sessionId);

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var session = await query.FirstOrDefaultAsync();
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
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var sessions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();

        var total = sessions.Count;
        var thisMonthCount = sessions.Count(s => s.CreatedAt.Year == DateTime.UtcNow.Year && s.CreatedAt.Month == DateTime.UtcNow.Month);
        var escalatedCount = sessions.Count(s => string.Equals(s.Output?.ActionCategory, "Escalate", StringComparison.OrdinalIgnoreCase));

        var lastSavedSession = sessions.FirstOrDefault();
        var lastSavedSessionDto = lastSavedSession != null ? MapToDto(lastSavedSession) : null;

        var recurringIssuesCount = sessions.Where(s => s.Output != null && !string.IsNullOrWhiteSpace(s.Output.DiagnosedCategory))
                                           .GroupBy(s => s.Output!.DiagnosedCategory)
                                           .Count(g => g.Count() > 1);

        var warningSignsSessions = sessions.Where(s => s.Answers.Any(a => a.QuestionKey == "warning_signs" && !string.IsNullOrWhiteSpace(a.AnswerValue) && !a.AnswerValue.Equals("None", StringComparison.OrdinalIgnoreCase))).ToList();

        var warningSignsActiveCount = warningSignsSessions.Count;

        var labels = new[] { "Monitor", "Maintain", "Troubleshoot", "Escalate" };
        var actionDistribution = labels.Select(label => new 
        { 
            label, 
            count = sessions.Count(s => string.Equals(s.Output?.ActionCategory, label, StringComparison.OrdinalIgnoreCase)) 
        }).ToList();

        var sessionFrequency = sessions.GroupBy(s => s.CreatedAt.Date)
                                       .OrderBy(g => g.Key)
                                       .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), count = g.Count() })
                                       .ToList();

        var recentWarningSigns = warningSignsSessions.Take(5).Select(s => new {
            id = s.Id.ToString(),
            warning_sign = s.Answers.FirstOrDefault(a => a.QuestionKey == "warning_signs")?.AnswerValue ?? "Unknown",
            threshold = "N/A",
            recommended_action = s.Output?.ActionCategory ?? "Monitor",
            created_at = s.CreatedAt.ToString("o"),
            display_date = s.CreatedAt.ToLocalTime().ToString("MMM dd, yyyy")
        }).ToList();

        return new
        {
            server_time = DateTime.UtcNow.ToString("o"),
            totals = new {
                total_sessions = total,
                this_month_count = thisMonthCount,
                escalated_count = escalatedCount
            },
            last_diagnosis = lastSavedSessionDto,
            current_action_status = lastSavedSessionDto,
            recurring_issues_count = recurringIssuesCount,
            warning_signs_active_count = warningSignsActiveCount,
            action_distribution = actionDistribution,
            session_frequency = sessionFrequency,
            recent_warning_signs = recentWarningSigns,
            last_saved_session = lastSavedSessionDto
        };
    }

    public async Task<object> GetRecurringPatternsAsync()
    {
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Output)
            .Include(s => s.Answers)
            .Include(s => s.Profile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var sessions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();

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
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var sessions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();

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
        var clientId = GetCurrentClientId();
        var query = _db.SystemProfiles.AsQueryable();

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(p => p.ClientId == clientId);
        }

        var profiles = await query
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

    public async Task<object> SaveProfileAsync(SaveProfilePayload payload)
    {
        var clientId = GetCurrentClientId();
        var profile = await _db.SystemProfiles.FirstOrDefaultAsync(p =>
            (string.IsNullOrEmpty(clientId) || p.ClientId == clientId) &&
            p.CpuModel == payload.CpuModel &&
            p.OsVersion == payload.OsVersion);

        if (profile == null)
        {
            profile = new SystemProfile
            {
                ClientId = clientId,
                CpuModel = payload.CpuModel,
                RamCapacity = payload.RamCapacity,
                StorageType = payload.StorageType,
                StorageCapacity = payload.StorageCapacity,
                StorageDetails = payload.StorageDetails != null 
                    ? JsonSerializer.Serialize(payload.StorageDetails) 
                    : null,
                OsVersion = payload.OsVersion,
                GpuDriver = payload.GpuDriver,
                ChipsetDriver = payload.ChipsetDriver,
                SystemAge = payload.SystemAge
            };
            _db.SystemProfiles.Add(profile);
            await _db.SaveChangesAsync();
        }
        else
        {
            profile.RamCapacity = payload.RamCapacity;
            profile.StorageType = payload.StorageType;
            profile.StorageCapacity = payload.StorageCapacity;
            if (payload.StorageDetails != null)
                profile.StorageDetails = JsonSerializer.Serialize(payload.StorageDetails);
            profile.GpuDriver = payload.GpuDriver;
            profile.ChipsetDriver = payload.ChipsetDriver;
            profile.SystemAge = payload.SystemAge;
            
            await _db.SaveChangesAsync();
        }

        return new
        {
            id = profile.Id.ToString(),
            cpu_model = profile.CpuModel,
            ram_capacity = profile.RamCapacity,
            storage_type = profile.StorageType,
            storage_capacity = profile.StorageCapacity,
            storage_details = string.IsNullOrWhiteSpace(profile.StorageDetails) 
                ? null : JsonSerializer.Deserialize<object>(profile.StorageDetails),
            os_version = profile.OsVersion,
            gpu_driver = profile.GpuDriver,
            chipset_driver = profile.ChipsetDriver,
            system_age = profile.SystemAge,
            created_at = profile.CreatedAt.ToString("o")
        };
    }

    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------

    private static DiagnosticSessionDto MapToDto(DiagnosticSession s)
    {
        string Answer(string key) =>
            s.Answers.FirstOrDefault(a => a.QuestionKey == key)?.AnswerValue ?? string.Empty;

        var action = s.Output?.ActionCategory ?? string.Empty;
        var nextStep = action.Contains("escalate", StringComparison.OrdinalIgnoreCase)
            ? "Back up critical files immediately and prepare the system for technician inspection."
            : action.Contains("troubleshoot", StringComparison.OrdinalIgnoreCase)
            ? "Run guided safe actions and verify whether the issue persists before changing hardware."
            : action.Contains("maintain", StringComparison.OrdinalIgnoreCase)
            ? "Perform safe maintenance steps and keep an eye on thermals and storage health."
            : "No urgent intervention required. Continue monitoring system telemetry under normal use.";

        var localTime = s.CreatedAt.ToLocalTime();
        var daysAgo = (int)(DateTime.UtcNow - s.CreatedAt).TotalDays;

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
            IsRecurring = s.Answers.Any(a => a.QuestionKey == "is_recurring" && a.AnswerValue == "true"),
            ResolutionStatus = Answer("resolution_status"),
            ResolutionSummary = Answer("resolution_summary"),
            ResolutionCheckedAt = Answer("resolution_checked_at"),
            LastActionStatus = Answer("last_action_status"),
            LastActionSummary = Answer("last_action_summary"),
            DiagnosisMode = Answer("diagnosis_mode"),
            ComponentIds = Answer("component_ids"),
            ScenarioId = Answer("scenario_id"),
            AgentId = Answer("agent_id"),
            AgentCommandId = Answer("agent_command_id"),
            ClientId = Answer("client_id"),
            CreatedAt = s.CreatedAt.ToString("o"),
            DaysAgo = daysAgo,
            DisplayDate = localTime.ToString("MMM dd, yyyy"),
            DisplayTime = localTime.ToString("hh:mm tt"),
            RecommendedNextStep = nextStep
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
            _db.SessionAnswers.Add(newAnswer);
        }
    }

    public async Task<IReadOnlyList<RecurringSessionDto>> GetRecurringSessionsAsync()
    {
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .AsQueryable();

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s => 
                s.Profile.ClientId == clientId || 
                s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        }

        var sessions = await query
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
        var clientId = GetCurrentClientId();
        var query = _db.SessionAnswers
            .Include(a => a.Session)
            .ThenInclude(s => s.Profile)
            .Where(a => a.QuestionKey == "warning_signs" && !string.IsNullOrWhiteSpace(a.AnswerValue));

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(a => 
                a.Session.Profile.ClientId == clientId || 
                a.Session.Answers.Any(ans => ans.QuestionKey == "client_id" && ans.AnswerValue == clientId));
        }

        return await query
            .Select(a => a.AnswerValue)
            .Distinct()
            .ToListAsync();
    }

   public async Task<IReadOnlyList<RemediationRunDto>> GetRemediationHistoryAsync(Guid sessionId)
    {
        var clientId = GetCurrentClientId();

        var query = _db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .Where(s => s.Id == sessionId);

        if (!string.IsNullOrEmpty(clientId))
        {
            query = query.Where(s =>
                s.Profile.ClientId == clientId ||
                s.Answers.Any(a =>
                    a.QuestionKey == "client_id" &&
                    a.AnswerValue == clientId));
        }

        var session = await query.FirstOrDefaultAsync();

        if (session?.Output == null)
        {
            return Array.Empty<RemediationRunDto>();
        }

        var runs = await _db.RemediationRuns
            .Include(r => r.ActionAttempts)
                .ThenInclude(a => a.Verification)
            .Where(r => r.DiagnosticOutputId == session.Output.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return runs.Select(r => new RemediationRunDto
        {
            RunId = r.Id.ToString(),
            Status = r.Status,
            CreatedAt = r.CreatedAt.ToString("o"),
            CompletedAt = r.CompletedAt?.ToString("o"),
            Attempts = r.ActionAttempts.Select(a => new ActionAttemptDto
            {
                ActionCode = a.ActionCode,
                VerificationStatus = a.Verification == null
                    ? null
                    : a.Verification.IsSuccessful
                        ? "Resolved"
                        : !string.IsNullOrEmpty(a.Verification.FailureReason)
                            ? a.Verification.FailureReason
                            : "Unresolved",
                CreatedAt = a.CreatedAt.ToString("o")
            }).ToList()
        }).ToList().AsReadOnly();
    }
}

