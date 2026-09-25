using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Ai;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Services;
using RigMD.Domain.Rules;
using RigMD.Application.Models;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/diagnosis")]
public class DiagnosticController : ControllerBase
{
    private readonly IDiagnosticEngineService _diagnosticEngine;
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly IWindowsSystemProfileService _profileService;
    private readonly ResolutionService _resolutionService;
    private readonly IAutomaticDiagnosisService _automaticDiagnosisService;
    private readonly IAiExplainer? _aiExplainer;

    public DiagnosticController(
        IDiagnosticEngineService diagnosticEngine,
        IDiagnosticSessionRepository sessionRepository,
        IWindowsSystemProfileService profileService,
        ResolutionService resolutionService,
        IAutomaticDiagnosisService automaticDiagnosisService,
        IAiExplainer? aiExplainer = null)
    {
        _diagnosticEngine = diagnosticEngine;
        _sessionRepository = sessionRepository;
        _profileService = profileService;
        _resolutionService = resolutionService;
        _automaticDiagnosisService = automaticDiagnosisService;
        _aiExplainer = aiExplainer;
    }

    public class LocalDiagnosisRequest
    {
        public string Mode { get; set; } = "full";
        public string[] ComponentIds { get; set; } = Array.Empty<string>();
        public string? ScenarioId { get; set; }
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

    [HttpGet("preflight")]
    public IActionResult GetHardwarePreflight()
    {
        try
        {
            var probe = _profileService.GetHardwarePresenceProbe();
            return Ok(probe);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Hardware preflight check failed: {ex}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { detail = "Hardware preflight check could not be completed." });
        }
    }

    [HttpPost("local-scan")]
    public async Task<IActionResult> LocalDiagnosis(
        [FromBody] LocalDiagnosisRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
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

            var hardware = _profileService.GetLiveSystemProfile();
            var capturedAt = DateTimeOffset.UtcNow;

            var input =
                new AutomaticDiagnosisInput
                {
                    AgentId = "local",
                    CommandId = Guid.Empty,
                    Mode = request.Mode,
                    ComponentIds = request.ComponentIds,
                    ScenarioId = request.ScenarioId,
                    CapturedAt = capturedAt,
                    Hardware = hardware
                };

            var result =
                _automaticDiagnosisService
                    .Diagnose(input);

            var explanation = result.Explanation;
            if (_aiExplainer != null && result.ComponentStatus != ComponentStatus.NotPresent)
            {
                try
                {
                    var mappedResult = new DiagnosticResult
                    {
                        DiagnosedCategory = result.DiagnosedCategory,
                        ActionCategory = result.ActionCategory,
                        ConfidenceLabel = result.ConfidenceLabel,
                        RecommendedNextStep = result.RecommendedNextStep,
                        Proof = result.Proof.Select(p => new DiagnosticProofItem
                        {
                            Label = p.Label,
                            Value = p.Value,
                            Status = p.Status,
                            Meaning = p.Meaning
                        }).ToList(),
                        AllLiveProof = result.Proof.Select(p => new DiagnosticProofItem
                        {
                            Label = p.Label,
                            Value = p.Value,
                            Status = p.Status,
                            Meaning = p.Meaning
                        }).ToList()
                    };
                    var scopeSymptom = new DiagnosticSymptomPayload
                    {
                        SymptomType = request.Mode == "scenario" && !string.IsNullOrWhiteSpace(request.ScenarioId)
                            ? request.ScenarioId
                            : request.Mode == "component" && result.TargetScope.Count > 0
                                ? $"Component check: {string.Join(", ", result.TargetScope)}"
                                : "Full computer check",
                        AffectedActivity = $"Mode: {request.Mode}"
                    };
                    var generated = await _aiExplainer.GenerateExplanationAsync(mappedResult, scopeSymptom);
                    if (!string.IsNullOrWhiteSpace(generated))
                    {
                        explanation = string.IsNullOrWhiteSpace(result.IncidentalWarning)
                            ? generated
                            : $"{generated}\n\n{result.IncidentalWarning}";
                    }
                }
                catch
                {
                    // Keep baseline explanation if AI explainer fails
                }
            }

            var sessionId =
                await _sessionRepository
                    .SaveAutomaticDiagnosisAsync(
                        hardware,
                        request.Mode,
                        request.ComponentIds,
                        request.ScenarioId,
                        Guid.Empty,
                        "local",
                        result.DiagnosedCategory,
                        result.ActionCategory,
                        result.ConfidenceLabel,
                        explanation,
                        "local",
                        result.PrimaryResult,
                        result.IncidentalWarning,
                        result.ComponentStatus.ToString());

            return Ok(
                new
                {
                    session_id =
                        sessionId.ToString(),

                    component_status =
                        result.ComponentStatus.ToString(),

                    target_scope =
                        result.TargetScope,

                    primary_result =
                        result.PrimaryResult,

                    incidental_warning =
                        result.IncidentalWarning,

                    diagnosed_category =
                        result.DiagnosedCategory,

                    action_category =
                        result.ActionCategory,

                    confidence_label =
                        result.ConfidenceLabel,

                    ai_explanation =
                        explanation,

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
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Local diagnosis failed: {ex}");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    detail =
                        "Local diagnosis could not be completed."
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
    // DELETE SINGLE SESSION
    // =========================================================

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var id))
                return BadRequest(new { detail = "Invalid diagnosis session ID." });

            var deleted = await _sessionRepository.DeleteSessionAsync(id);

            if (!deleted)
                return NotFound(new { detail = "Diagnosis record not found." });

            return Ok(new
            {
                session_id = sessionId,
                deleted = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to delete diagnosis session: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Diagnosis session could not be deleted." });
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
