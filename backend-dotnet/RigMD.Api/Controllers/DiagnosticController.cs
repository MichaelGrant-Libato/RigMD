using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigMD.Api.Services;
using RigMD.Api.Middleware;
using RigMD.Application.Contracts.Ai;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Services;
using RigMD.Application.Services.Autonomy;
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
    private readonly DeviceDiagnosticRunner _diagnosticRunner;

    public DiagnosticController(
        IDiagnosticEngineService diagnosticEngine,
        IDiagnosticSessionRepository sessionRepository,
        IWindowsSystemProfileService profileService,
        ResolutionService resolutionService,
        IAutomaticDiagnosisService automaticDiagnosisService,
        IAiExplainer? aiExplainer = null,
        DeviceDiagnosticRunner? diagnosticRunner = null)
    {
        _diagnosticEngine = diagnosticEngine;
        _sessionRepository = sessionRepository;
        _profileService = profileService;
        _resolutionService = resolutionService;
        _automaticDiagnosisService = automaticDiagnosisService;
        _aiExplainer = aiExplainer;
        _diagnosticRunner = diagnosticRunner ?? new DeviceDiagnosticRunner();
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

            var explanationStatus = RigMdAgentRuntimeSettingsStore.ResolveEffectiveProxyUrl() == null ? "local-only"
                : result.ComponentStatus == ComponentStatus.NotPresent || result.ActionCategory is not ("Troubleshoot" or "Escalate") ? "not-needed"
                : _aiExplainer == null ? "service-unavailable" : "pending";

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
                        result.Explanation,
                        "local",
                        result.PrimaryResult,
                        result.IncidentalWarning,
                        result.ComponentStatus.ToString(),
                        result.Proof,
                        result.RecommendedNextStep);

            await _sessionRepository.SaveExplanationAsync(sessionId, "local", null, explanationStatus);
            if (HttpContext != null) HttpContext.Items[PerformanceRecordingMiddleware.ContextKey] = new PerformanceContext(
                result.Proof.Count, result.Proof.Count(p => p.EvidenceKind is "unavailable" or "not_assessed" ||
                    p.Status is "unknown" or "unavailable" or "not_assessed" or "not_verified" or "unverified"), request.Mode,
                "local");

            return Ok(
                new
                {
                    session_id =
                        sessionId.ToString(),

                    diagnosis_mode = request.Mode,
                    component_ids = request.Mode == "component" ? request.ComponentIds : Array.Empty<string>(),
                    scenario_id = request.Mode == "scenario" ? request.ScenarioId : null,

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
                        result.Explanation,
                    explanation_source = "local",
                    explanation_model = (string?)null,
                    explanation_status = explanationStatus,

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

    [HttpPost("sessions/{sessionId:guid}/explanation")]
    public async Task<IActionResult> ExplainSavedCheck(Guid sessionId, CancellationToken token)
    {
        var response = await _diagnosticRunner.TryRunAsync<IActionResult>("explanation:" + sessionId, async ct =>
        {
            var session = await _sessionRepository.GetSessionAsync(sessionId);
            if (session == null) return NotFound(new { detail = "Saved check not found." });
            if (session.ExplanationStatus != "pending") return ExplanationResponse(session);

            var generated = new DiagnosticExplanation(session.AiExplanation, "local", Status: "interrupted");
            if (_aiExplainer != null && DateTimeOffset.TryParse(session.CreatedAt, out var savedAt) &&
                savedAt <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - savedAt < TimeSpan.FromMinutes(2))
            {
                try
                {
                    var result = new DiagnosticResult
                    {
                        DiagnosedCategory = session.DiagnosedCategory,
                        ActionCategory = session.ActionCategory,
                        ConfidenceLabel = session.ConfidenceLabel,
                        RecommendedNextStep = session.RecommendedNextStep ?? "",
                        Proof = CloudEvidence(session.Proof)
                    };
                    var symptom = new DiagnosticSymptomPayload
                    {
                        SymptomType = session.DiagnosisMode switch
                        {
                            "component" => CloudComponentScope(session.ComponentIds),
                            "scenario" => "Selected problem check",
                            _ => "Full computer check"
                        },
                        AffectedActivity = "Saved Windows check"
                    };
                    generated = await _aiExplainer.GenerateExplanationWithSourceAsync(result, symptom, ct);
                }
                catch (OperationCanceledException) { generated = generated with { Status = "interrupted" }; }
                catch (Exception) { generated = generated with { Status = "service-unavailable" }; }
            }

            if (generated.Source == "gemini" && !string.IsNullOrWhiteSpace(generated.Text))
            {
                session.ExplanationSource = "gemini";
                session.ExplanationModel = generated.Model ?? "";
                session.ExplanationStatus = "completed";
                session.AiExplanation = string.IsNullOrWhiteSpace(session.IncidentalWarning) ? generated.Text
                    : $"{generated.Text}\n\n{session.IncidentalWarning}";
            }
            else
            {
                session.ExplanationSource = "local";
                session.ExplanationStatus = generated.Status ?? "service-unavailable";
            }
            await _sessionRepository.SaveExplanationAsync(sessionId, session.ExplanationSource, session.ExplanationModel,
                session.ExplanationStatus, session.AiExplanation, onlyPending: true);
            return ExplanationResponse(session);
        }, token);
        return response ?? Conflict(new { detail = "The explanation is already being requested." });
    }

    private IActionResult ExplanationResponse(DiagnosticSessionDto session) => Ok(new
    {
        ai_explanation = session.AiExplanation,
        explanation_source = session.ExplanationSource,
        explanation_model = session.ExplanationModel,
        explanation_status = session.ExplanationStatus
    });

    private static readonly Dictionary<string, string> CloudMetricLabels = new()
    {
        ["cpu_usage"] = "Processor activity", ["cpu_temperature"] = "Processor temperature",
        ["gpu_temperature"] = "Graphics temperature", ["memory_usage"] = "Memory use",
        ["storage_usage"] = "Storage space used", ["battery_charge"] = "Battery charge",
        ["network_latency"] = "Network latency", ["network_packet_loss"] = "Network packet loss",
        ["application_crash_history"] = "Application crash records (last 7 days)",
        ["system_crash_history"] = "System crash and unexpected restart records (last 7 days)"
    };

    private static string CloudComponentScope(string componentIds)
    {
        try
        {
            var ids = JsonSerializer.Deserialize<string[]>(componentIds) ?? [];
            var labels = ids.Select(DiagnosticScopeMapper.NormalizeComponentId)
                .Distinct(StringComparer.Ordinal).Select(id => (Id: id, Label: DiagnosticScopeMapper.GetComponentDisplayName(id)))
                .Where(item => item.Id != item.Label).Select(item => item.Label).ToArray();
            if (labels.Length > 0) return "Component check: " + string.Join(", ", labels);
        }
        catch (JsonException) { }
        return "Selected component check";
    }

    internal static List<DiagnosticProofItem> CloudEvidence(object[] proof)
    {
        var readings = new List<DiagnosticProofItem>();
        foreach (var item in proof.OfType<JsonElement>())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("metric_id", out var id) || id.ValueKind != JsonValueKind.String ||
                !CloudMetricLabels.TryGetValue(id.GetString()!, out var label) ||
                !item.TryGetProperty("value", out var reading) || reading.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String) continue;
            var value = reading.GetString()!;
            var state = status.GetString()!;
            if (state is not ("high" or "elevated" or "normal" or "info" or "still needs attention") || value.Length > 80 ||
                !Regex.IsMatch(value, @"^\d+(?:(?:[.,]\d+)?\s?(?:%|°C|ms)(?: (?:used|\(\d+(?:[.,]\d+)? / \d+(?:[.,]\d+)? GB\)))?| found)$",
                    RegexOptions.CultureInvariant)) continue;
            readings.Add(new DiagnosticProofItem { Label = label, Value = value, Status = state });
        }
        return readings;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            var sessions = (await _sessionRepository.GetSessionsAsync()).ToList();
            var count = sessions.Count;
            if (HttpContext != null) HttpContext.Items[PerformanceRecordingMiddleware.ContextKey] = new PerformanceContext(count);
            for (var i = 0; i < count; i++)
            {
                sessions[i].SessionCode = $"S-{count - i:D3}";
            }

            var recurringCount = new RecurringPatternService().BuildPatterns(await _sessionRepository.GetRecurringSessionsAsync())
                .metrics.recurring_issues;
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
            DateTimeOffset? sessionCreatedAt = DateTimeOffset.TryParse(session.CreatedAt, out var parsedCreatedAt)
                ? parsedCreatedAt
                : null;

            var result = _resolutionService.CheckResolution(
                session.DiagnosedCategory,
                hardware,
                session.ScenarioId,
                session.ComponentIds,
                sessionCreatedAt,
                session.DiagnosisMode,
                session.Proof);

            var updated = await _sessionRepository.UpdateResolutionAsync(
                id,
                result.resolution_status,
                result.resolution_checked_at,
                result.resolution_summary,
                result.resolution_proof);

            if (!updated)
                return NotFound(new { detail = "Diagnosis record not found." });

            var savedFollowUp = await _sessionRepository.GetSessionAsync(id);
            if (savedFollowUp == null)
                return NotFound(new { detail = "Diagnosis record not found." });
            return Ok(new
            {
                session_id = sessionId,
                resolution_status = savedFollowUp.ResolutionStatus,
                resolution_checked_at = savedFollowUp.ResolutionCheckedAt,
                resolution_summary = savedFollowUp.ResolutionSummary,
                resolution_proof = savedFollowUp.ResolutionProof,
                follow_up_history = savedFollowUp.FollowUpHistory,
                follow_up_history_status = savedFollowUp.FollowUpHistoryStatus
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
                last_action_summary = "The approved action finished. Its effect still needs checking."
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
