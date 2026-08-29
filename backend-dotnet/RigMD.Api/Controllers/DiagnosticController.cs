using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Services;
using RigMD.Domain.Rules;
using RigMD.Application.Models;
using System.Text.Json;
using RigMD.Api.Models;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/diagnosis")]
public class DiagnosticController : ControllerBase
{
    private readonly IDiagnosticEngineService _diagnosticEngine;
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly IWindowsSystemProfileService _profileService;
    private readonly ResolutionService _resolutionService;
    private readonly IAgentRepository _agentRepository;
    private readonly IAutomaticDiagnosisService _automaticDiagnosisService;

    public DiagnosticController(
        IDiagnosticEngineService diagnosticEngine,
        IDiagnosticSessionRepository sessionRepository,
        IWindowsSystemProfileService profileService,
        ResolutionService resolutionService,
        IAgentRepository agentRepository,
        IAutomaticDiagnosisService automaticDiagnosisService)
    {
        _diagnosticEngine = diagnosticEngine;
        _sessionRepository = sessionRepository;
        _profileService = profileService;
        _resolutionService = resolutionService;
        _agentRepository = agentRepository;
        _automaticDiagnosisService = automaticDiagnosisService;
    }

    // =========================================================
    // SUBMIT DIAGNOSIS
    // =========================================================

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitDiagnosis(
        [FromBody] DiagnosticSymptomPayload payload)
    {
        try
        {
            var report = await _diagnosticEngine.SubmitDiagnosisAsync(payload);

            if (report.hardware_profile == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Hardware profile was not available for database persistence." });
            }

            var clientId = Request.Headers["X-Client-ID"].FirstOrDefault() ?? string.Empty;

            var sessionId = await _sessionRepository.SaveDiagnosisAsync(
                payload,
                report.hardware_profile,
                report.diagnosed_category,
                report.action_category,
                report.confidence_label,
                report.ai_explanation,
                clientId);

            report.session_id = sessionId.ToString();

            return Ok(new
            {
                session_id = report.session_id,
                symptom_type = payload.SymptomType,
                affected_activity = payload.AffectedActivity,
                frequency = payload.Frequency,
                severity = payload.Severity,
                duration = payload.Duration,
                recent_changes = payload.RecentChanges,
                system_state = payload.SystemState,
                warning_signs = payload.WarningSigns,
                diagnosed_category = report.diagnosed_category,
                action_category = report.action_category,
                confidence_label = report.confidence_label,
                ai_explanation = report.ai_explanation,
                proof = report.proof,
                verification_target = report.verification_target,
                recommended_next_step = report.recommended_next_step,
                resolution_status = "open",
                resolution_checked_at = (string?)null,
                resolution_summary = string.Empty,
                resolution_proof = Array.Empty<object>(),
                created_at = DateTime.UtcNow.ToString("o")
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Diagnosis submission failed: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Diagnosis could not be completed." });
        }
    }

        [HttpPost("automatic")]
    public async Task<IActionResult> AutomaticDiagnosis(
        [FromBody] AutomaticDiagnosisRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.AgentId))
            {
                return BadRequest(
                    new
                    {
                        detail =
                            "Agent ID is required."
                    });
            }

            if (request.CommandId == Guid.Empty)
            {
                return BadRequest(
                    new
                    {
                        detail =
                            "A completed Agent scan command is required."
                    });
            }

            if (
                request.Mode != "full" &&
                request.Mode != "component" &&
                request.Mode != "scenario")
            {
                return BadRequest(
                    new
                    {
                        detail =
                            "Diagnosis mode must be full, component, or scenario."
                    });
            }

            var agent =
                await _agentRepository
                    .GetAgentAsync(
                        request.AgentId,
                        cancellationToken);

            if (agent == null)
            {
                return NotFound(
                    new
                    {
                        detail =
                            "RigMD Agent was not found."
                    });
            }

            var command =
                await _agentRepository
                    .GetCommandAsync(
                        request.AgentId,
                        request.CommandId,
                        cancellationToken);

            if (command == null)
            {
                return NotFound(
                    new
                    {
                        detail =
                            "Agent scan command was not found."
                    });
            }

            if (
                !string.Equals(
                    command.CommandType,
                    "scan_system_profile",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    new
                    {
                        detail =
                            "The supplied command is not a system-profile scan."
                    });
            }

            if (
                !string.Equals(
                    command.Status,
                    "completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(
                    new
                    {
                        detail =
                            "The Agent scan has not completed yet."
                    });
            }

            var snapshot =
                await _agentRepository
                    .GetLatestSnapshotAsync(
                        request.AgentId,
                        cancellationToken);

            if (snapshot == null)
            {
                return NotFound(
                    new
                    {
                        detail =
                            "No Agent hardware snapshot is available."
                    });
            }

            /*
            * The schema currently stores latest snapshot separately
            * from the command rather than command_id on the snapshot.
            *
            * Require the snapshot to have been captured after this
            * command was requested so an older snapshot cannot be used.
            */
            if (
                snapshot.CapturedAt <
                command.RequestedAt)
            {
                return Conflict(
                    new
                    {
                        detail =
                            "The available hardware snapshot is older than the requested scan."
                    });
            }

            var snapshotAge =
                DateTimeOffset.UtcNow -
                snapshot.CapturedAt;

            if (
                snapshotAge >
                TimeSpan.FromMinutes(2))
            {
                return Conflict(
                    new
                    {
                        detail =
                            "The Agent hardware snapshot is stale. Run a fresh diagnosis scan."
                    });
            }

            var jsonOptions =
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive =
                        true
                };

            var hardware =
                JsonSerializer.Deserialize<HardwareProfileDto>(
                    snapshot.HardwareJson,
                    jsonOptions);

            if (hardware == null)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        detail =
                            "RigMD could not parse the Agent hardware evidence."
                    });
            }

            var input =
                new AutomaticDiagnosisInput
                {
                    AgentId =
                        request.AgentId,

                    CommandId =
                        request.CommandId,

                    Mode =
                        request.Mode,

                    ComponentIds =
                        request.ComponentIds,

                    ScenarioId =
                        request.ScenarioId,

                    CapturedAt =
                        snapshot.CapturedAt,

                    Hardware =
                        hardware
                };

            var result =
                _automaticDiagnosisService
                    .Diagnose(input);

            var sessionId =
                await _sessionRepository
                    .SaveAutomaticDiagnosisAsync(
                        hardware,
                        request.Mode,
                        request.ComponentIds,
                        request.ScenarioId,
                        request.CommandId,
                        request.AgentId,
                        result.DiagnosedCategory,
                        result.ActionCategory,
                        result.ConfidenceLabel,
                        result.Explanation,
                        agent.ClientId);

            return Ok(
                new
                {
                    session_id =
                        sessionId.ToString(),

                    diagnosed_category =
                        result.DiagnosedCategory,

                    action_category =
                        result.ActionCategory,

                    confidence_label =
                        result.ConfidenceLabel,

                    ai_explanation =
                        result.Explanation,

                    proof =
                        result.Proof,

                    verification_target =
                        result.VerificationTarget,

                    recommended_next_step =
                        result.RecommendedNextStep,

                    resolution_status =
                        "open",

                    resolution_checked_at =
                        (string?)null,

                    resolution_summary =
                        string.Empty,

                    resolution_proof =
                        Array.Empty<object>(),

                    created_at =
                        DateTime.UtcNow
                            .ToString("o")
                });
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"Automatic diagnosis JSON parsing failed: {ex}");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    detail =
                        "RigMD could not read the Agent hardware evidence."
                });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Automatic diagnosis failed: {ex}");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    detail =
                        "Automatic diagnosis could not be completed."
                });
        }
    }

    // =========================================================
    // GET ALL SESSIONS
    // =========================================================

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            var sessions = (await _sessionRepository.GetSessionsAsync()).ToList();
            var count = sessions.Count;
            for (var i = 0; i < count; i++)
            {
                sessions[i].SessionCode = $"S-{count - i:D3}";
            }

            var recurringCount = sessions.Count(s => s.IsRecurring);
            var escalatedCount = sessions.Count(s => s.ActionCategory.Contains("escalate", StringComparison.OrdinalIgnoreCase));
            var now = DateTime.UtcNow;
            var thisMonthCount = sessions.Count(s => 
                DateTime.TryParse(s.CreatedAt, out var dt) && dt.Month == now.Month && dt.Year == now.Year);

            return Ok(new
            {
                metrics = new
                {
                    total_sessions = sessions.Count,
                    recurring_issues = recurringCount,
                    escalated = escalatedCount,
                    this_month = thisMonthCount
                },
                sessions
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load diagnosis sessions: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Diagnosis sessions could not be loaded." });
        }
    }

    // =========================================================
    // GET SINGLE SESSION
    // =========================================================

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var id))
                return BadRequest(new { detail = "Invalid diagnosis session ID." });

            var session = await _sessionRepository.GetSessionAsync(id);

            if (session == null)
                return NotFound(new { detail = "Diagnosis record not found." });

            // Attach remediation history
            session.RemediationHistory = (await _sessionRepository.GetRemediationHistoryAsync(id)).ToList();

            return Ok(session);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load diagnosis session: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Diagnosis session could not be loaded." });
        }
    }

    // =========================================================
    // CHECK RESOLUTION
    // =========================================================

    [HttpPost("{sessionId}/check-resolution")]
    public async Task<IActionResult> CheckResolution(string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var id))
                return BadRequest(new { detail = "Invalid diagnosis session ID." });

            var session = await _sessionRepository.GetSessionAsync(id);
            if (session == null)
                return NotFound(new { detail = "Diagnosis record not found." });

            var hardware = _profileService.GetLiveSystemProfile();
            var result = _resolutionService.CheckResolution(session.DiagnosedCategory, hardware);

            var updated = await _sessionRepository.UpdateResolutionAsync(
                id,
                result.resolution_status,
                result.resolution_checked_at,
                result.resolution_summary,
                result.resolution_proof);

            if (!updated)
                return NotFound(new { detail = "Diagnosis record not found." });

            return Ok(new
            {
                session_id = sessionId,
                resolution_status = result.resolution_status,
                resolution_checked_at = result.resolution_checked_at,
                resolution_summary = result.resolution_summary,
                resolution_proof = result.resolution_proof
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Resolution check failed: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Resolution check could not be completed." });
        }
    }

    // =========================================================
    // MARK NEEDS RECHECK
    // =========================================================

    [HttpPost("{sessionId}/needs-recheck")]
    public async Task<IActionResult> MarkNeedsRecheck(string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var id))
                return BadRequest(new { detail = "Invalid diagnosis session ID." });

            var updated = await _sessionRepository.MarkNeedsRecheckAsync(id);

            if (!updated)
                return NotFound(new { detail = "Diagnosis record not found." });

            return Ok(new
            {
                session_id = sessionId,
                resolution_status = "needs_recheck",
                last_action_status = "completed",
                last_action_summary = "A safe action was performed. Run a follow-up check to see if the issue improved."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to update diagnosis status: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Diagnosis status could not be updated." });
        }
    }
}