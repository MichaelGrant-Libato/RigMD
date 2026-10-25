using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Application.Services;
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

        answers.Add(new() { DiagnosticSessionId = session.Id, Session = session,
            QuestionKey = "device_id", AnswerValue = LocalDiagnosticDeviceIdentity.Read() });
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
    string clientId = "",
    string? primaryResult = null,
    string? incidentalWarning = null,
    string? componentStatus = null,
    IReadOnlyList<AutomaticDiagnosisProof>? proof = null,
    string? recommendedNextStep = null)
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
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "primary_result",
                    AnswerValue = primaryResult ?? string.Empty
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "incidental_warning",
                    AnswerValue = incidentalWarning ?? string.Empty
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "proof",
                    AnswerValue = JsonSerializer.Serialize((proof ?? Array.Empty<AutomaticDiagnosisProof>())
                        .Select(p => new { metric_id = p.MetricId, captured_at = p.CapturedAt,
                            evidence_kind = p.EvidenceKind, rule_version = p.RuleVersion,
                            window_kind = p.WindowKind, window_start = p.WindowStart, window_end = p.WindowEnd,
                            label = p.Label, value = p.Value, status = p.Status, meaning = p.Meaning }))
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "recommended_next_step",
                    AnswerValue = recommendedNextStep ?? string.Empty
                },
                new()
                {
                    DiagnosticSessionId = session.Id,
                    Session = session,
                    QuestionKey = "component_status",
                    AnswerValue = componentStatus ?? nameof(ComponentStatus.Present)
                }
            };

        if (string.Equals(agentId, "local", StringComparison.Ordinal))
            answers.Add(new() { DiagnosticSessionId = session.Id, Session = session,
                QuestionKey = "device_id", AnswerValue = LocalDiagnosticDeviceIdentity.Read() });
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
        var repeatedIds = new RecurringPatternService().BuildPatterns(sessions.Select(MapRecurringSession).ToArray())
            .patterns.SelectMany(p => p.occurrences).Select(o => o.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sessions.Select(s =>
        {
            var dto = MapToDto(s);
            dto.IsRecurring = repeatedIds.Contains(dto.SessionId);
            return dto;
        }).ToList().AsReadOnly();
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
        if (session == null) return null;
        var dto = MapToDto(session);
        dto.IsRecurring = new RecurringPatternService().BuildPatterns(await GetRecurringSessionsAsync()).patterns
            .Any(p => p.occurrences.Any(o => o.id == dto.SessionId));
        return dto;
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId)
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
        if (session == null)
        {
            return false;
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        if (session.Output != null)
        {
            var outputId = session.Output.Id;

            var remediationRunIds = await _db.RemediationRuns
                .Where(r => r.DiagnosticOutputId == outputId)
                .Select(r => r.Id)
                .ToListAsync();

            if (remediationRunIds.Count > 0)
            {
                var actionAttemptIds = await _db.ActionAttempts
                    .Where(a => remediationRunIds.Contains(a.RemediationRunId))
                    .Select(a => a.Id)
                    .ToListAsync();

                if (actionAttemptIds.Count > 0)
                {
                    var verifications = await _db.VerificationResults
                        .Where(v => actionAttemptIds.Contains(v.ActionAttemptId))
                        .ToListAsync();

                    _db.VerificationResults.RemoveRange(verifications);

                    var rollbacks = await _db.RollbackEvents
                        .Where(r => actionAttemptIds.Contains(r.ActionAttemptId))
                        .ToListAsync();

                    _db.RollbackEvents.RemoveRange(rollbacks);
                }

                var pivots = await _db.PivotEvents
                    .Where(p => remediationRunIds.Contains(p.RemediationRunId))
                    .ToListAsync();

                _db.PivotEvents.RemoveRange(pivots);

                var attempts = await _db.ActionAttempts
                    .Where(a => remediationRunIds.Contains(a.RemediationRunId))
                    .ToListAsync();

                _db.ActionAttempts.RemoveRange(attempts);

                var runs = await _db.RemediationRuns
                    .Where(r => remediationRunIds.Contains(r.Id))
                    .ToListAsync();

                _db.RemediationRuns.RemoveRange(runs);
            }

            _db.DiagnosticOutputs.Remove(session.Output);
        }

        _db.SessionAnswers.RemoveRange(session.Answers);
        _db.DiagnosticSessions.Remove(session);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return true;
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
        SetOrAddAnswer(session, "resolution_device_id", LocalDiagnosticDeviceIdentity.Read());
        SetOrAddAnswer(session, "resolution_summary", resolutionSummary);
        SetOrAddAnswer(session, "resolution_proof", JsonSerializer.Serialize(resolutionProof));

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task SaveExplanationSourceAsync(Guid sessionId, string source, string? model)
    {
        var clientId = GetCurrentClientId();
        var query = _db.DiagnosticSessions.Include(s => s.Answers).Include(s => s.Profile).Where(s => s.Id == sessionId);
        if (!string.IsNullOrEmpty(clientId))
            query = query.Where(s => s.Profile.ClientId == clientId || s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId));
        var session = await query.FirstOrDefaultAsync();
        if (session == null) return;
        SetOrAddAnswer(session, "explanation_source", source is "gemini" or "local" ? source : "unknown");
        SetOrAddAnswer(session, "explanation_model", source == "gemini" ? model ?? "" : "");
        await _db.SaveChangesAsync();
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
        var previousProof = ReadSavedProof(session.Answers.FirstOrDefault(a => a.QuestionKey == "resolution_proof")?.AnswerValue ?? "");
        if (previousProof.Length > 0)
            SetOrAddAnswer(session, "pre_action_readings", JsonSerializer.Serialize(new
            {
                checked_at = session.Answers.FirstOrDefault(a => a.QuestionKey == "resolution_checked_at")?.AnswerValue,
                proof = previousProof
            }));
        SetOrAddAnswer(session, "resolution_proof", "[]");
        SetOrAddAnswer(session, "resolution_checked_at", "");
        SetOrAddAnswer(session, "resolution_summary", "The approved action finished. Its effect still needs checking.");
        SetOrAddAnswer(session, "last_action_status", "completed");
        SetOrAddAnswer(session, "last_action_summary",
            "The approved action finished. Its effect still needs checking.");

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

        var recurring = new RecurringPatternService().BuildPatterns(sessions.Select(MapRecurringSession).ToArray());
        var recurringIssuesCount = recurring.metrics.recurring_issues;
        if (lastSavedSessionDto != null) lastSavedSessionDto.IsRecurring = recurring.patterns
            .Any(p => p.occurrences.Any(o => o.id == lastSavedSessionDto.SessionId));

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

        var grouped = new RecurringPatternService().BuildPatterns(sessions.Select(MapRecurringSession).ToArray())
            .patterns.Select(pattern => new
            {
                category = pattern.probable_cause,
                count = pattern.occurrence_count,
                is_recurring = true,
                risk_level = pattern.risk_level,
                current_condition = pattern.current_condition,
                sessions = pattern.occurrences.Select(o => new
                {
                    session_id = o.id,
                    created_at = sessions.First(s => s.Id.ToString() == o.id).CreatedAt.ToString("o")
                }).ToList()
            })
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
            DeviceId = Answer("device_id"),
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
            ExplanationSource = Answer("explanation_source"),
            ExplanationModel = Answer("explanation_model"),
            IsRecurring = s.Answers.Any(a => a.QuestionKey == "is_recurring" && a.AnswerValue == "true"),
            ResolutionStatus = Answer("resolution_status"),
            ResolutionSummary = Answer("resolution_summary"),
            ResolutionProof = ReadSavedProof(Answer("resolution_proof")),
            Proof = ReadSavedProof(Answer("proof")),
            ResolutionCheckedAt = Answer("resolution_checked_at"),
            ResolutionDeviceId = Answer("resolution_device_id"),
            LastActionStatus = Answer("last_action_status"),
            LastActionSummary = Answer("last_action_summary"),
            DiagnosisMode = Answer("diagnosis_mode"),
            ComponentIds = Answer("component_ids"),
            ScenarioId = Answer("scenario_id"),
            AgentId = Answer("agent_id"),
            AgentCommandId = Answer("agent_command_id"),
            ClientId = Answer("client_id"),
            PrimaryResult = Answer("primary_result"),
            IncidentalWarning = Answer("incidental_warning"),
            ComponentStatus = string.IsNullOrWhiteSpace(Answer("component_status"))
                ? nameof(ComponentStatus.Present)
                : Answer("component_status"),
            CreatedAt = s.CreatedAt.ToString("o"),
            DaysAgo = daysAgo,
            DisplayDate = localTime.ToString("MMM dd, yyyy"),
            DisplayTime = localTime.ToString("hh:mm tt"),
            RecommendedNextStep = string.IsNullOrWhiteSpace(Answer("recommended_next_step"))
                ? nextStep : Answer("recommended_next_step")
        };
    }

    private static object[] ReadSavedProof(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<object>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<object>();
            return document.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String &&
                    item.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String &&
                    (!item.TryGetProperty("status", out var status) || status.ValueKind is JsonValueKind.String or JsonValueKind.Null) &&
                    (!item.TryGetProperty("meaning", out var meaning) || meaning.ValueKind is JsonValueKind.String or JsonValueKind.Null))
                .Select(item => (object)item.Clone()).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<object>();
        }
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

        return sessions.Select(MapRecurringSession).ToList().AsReadOnly();
    }

    private static RecurringSessionDto MapRecurringSession(DiagnosticSession s) => new()
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
            Proof = ReadSavedProof(s.Answers.FirstOrDefault(a => a.QuestionKey == "proof")?.AnswerValue ?? ""),
            ResolutionProof = ReadSavedProof(s.Answers.FirstOrDefault(a => a.QuestionKey == "resolution_proof")?.AnswerValue ?? ""),
            ResolutionCheckedAt = s.Answers.FirstOrDefault(a => a.QuestionKey == "resolution_checked_at")?.AnswerValue ?? "",
            ResolutionStatus = s.Answers.FirstOrDefault(a => a.QuestionKey == "resolution_status")?.AnswerValue ?? "",
            CreatedAt = s.CreatedAt.UtcDateTime
        };

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
            Attempts = r.ActionAttempts.Select(a =>
            {
                var evidence = ReadExecutionEvidence(a.Verification?.ObservedState);
                return new ActionAttemptDto
                {
                    ExecutionSummary = evidence.Summary,
                    Proof = evidence.Proof,
                    ActionCode = a.ActionCode,
                    VerificationStatus = a.Verification == null
                        ? null
                        : a.Verification.IsSuccessful
                            ? "Resolved"
                            : !string.IsNullOrEmpty(a.Verification.FailureReason)
                                ? a.Verification.FailureReason
                                : "Unresolved",
                    CreatedAt = a.CreatedAt.ToString("o")
                };
            }).ToList()
        }).ToList().AsReadOnly();
    }

    private static (string? Summary, List<ExecutionProof> Proof) ReadExecutionEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, new());
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, new());
            var summary = root.TryGetProperty("executionSummary", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            var proof = root.TryGetProperty("proof", out var rows) && rows.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<ExecutionProof>>(rows.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
            return (summary, (proof ?? new()).Where(item => item != null && !string.IsNullOrWhiteSpace(item.Label)).ToList());
        }
        catch (JsonException) { return (null, new()); }
    }
}

