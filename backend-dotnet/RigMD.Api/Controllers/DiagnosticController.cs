using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Services;
using RigMD.Domain.Rules;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/diagnosis")]
public class DiagnosticController : ControllerBase
{
    private readonly IDiagnosticEngineService _diagnosticEngine;
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly IWindowsSystemProfileService _profileService;
    private readonly ResolutionService _resolutionService;

    public DiagnosticController(
        IDiagnosticEngineService diagnosticEngine,
        IDiagnosticSessionRepository sessionRepository,
        IWindowsSystemProfileService profileService,
        ResolutionService resolutionService)
    {
        _diagnosticEngine = diagnosticEngine;
        _sessionRepository = sessionRepository;
        _profileService = profileService;
        _resolutionService = resolutionService;
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

    // =========================================================
    // GET ALL SESSIONS
    // =========================================================

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            var sessions = await _sessionRepository.GetSessionsAsync();
            return Ok(sessions);
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