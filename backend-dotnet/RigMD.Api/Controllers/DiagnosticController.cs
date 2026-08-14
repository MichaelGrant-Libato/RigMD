using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Services;
using RigMD.Domain.Rules;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/diagnosis")]
public class DiagnosticController : ControllerBase
{
    private readonly IDiagnosticEngineService _diagnosticEngine;

    // In-memory session store — replaced by EF Core in a later phase
    private static readonly List<object> _sessions = new();
    private static readonly Dictionary<string, object> _sessionMap = new();

    public DiagnosticController(IDiagnosticEngineService diagnosticEngine)
    {
        _diagnosticEngine = diagnosticEngine;
    }

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitDiagnosis([FromBody] DiagnosticSymptomPayload payload)
    {
        try
        {
            var report = await _diagnosticEngine.SubmitDiagnosisAsync(payload);

            // Store session in-memory for this run
            var sessionId = report.session_id ?? Guid.NewGuid().ToString();
            var sessionRecord = new
            {
                session_id = sessionId,
                symptom_type = payload.SymptomType,
                diagnosed_category = report.diagnosed_category,
                action_category = report.action_category,
                confidence_label = report.confidence_label,
                ai_explanation = report.ai_explanation,
                proof = report.proof,
                verification_target = report.verification_target,
                recommended_next_step = report.recommended_next_step,
                resolution_status = "open",
                resolution_checked_at = (string?)null,
                resolution_summary = "",
                resolution_proof = Array.Empty<object>(),
                created_at = DateTime.UtcNow.ToString("o")
            };
            _sessionMap[sessionId] = sessionRecord;
            _sessions.Insert(0, sessionRecord);

            return Ok(sessionRecord);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("sessions")]
    public IActionResult GetSessions()
    {
        return Ok(_sessions);
    }

    [HttpGet("sessions/{sessionId}")]
    public IActionResult GetSession(string sessionId)
    {
        if (_sessionMap.TryGetValue(sessionId, out var session))
            return Ok(session);

        return NotFound(new { detail = "Diagnosis record not found" });
    }

    [HttpPost("{sessionId}/check-resolution")]
    public IActionResult CheckResolution(string sessionId)
    {
        // Resolution checking requires the full Python resolution_service logic.
        // For now we return a placeholder so the frontend does not error out.
        return Ok(new
        {
            session_id = sessionId,
            resolution_status = "open",
            resolution_checked_at = DateTime.UtcNow.ToString("o"),
            resolution_summary = "Resolution check is not yet fully implemented in the C# backend. Recheck after applying the recommended action.",
            resolution_proof = Array.Empty<object>()
        });
    }

    [HttpPost("{sessionId}/needs-recheck")]
    public IActionResult MarkNeedsRecheck(string sessionId)
    {
        return Ok(new
        {
            session_id = sessionId,
            resolution_status = "needs_recheck",
            last_action_status = "completed",
            last_action_summary = "A safe action was performed. Run a follow-up check to see if the issue improved."
        });
    }
}
