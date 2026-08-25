using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutonomyController : ControllerBase
{
    private readonly IAutonomousOrchestrator _orchestrator;
    private readonly IWindowsSystemProfileService _profileService;
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly IRemediationRepository _remediationRepository;
    private readonly ILogger<AutonomyController> _logger;

    public AutonomyController(
        IAutonomousOrchestrator orchestrator,
        IWindowsSystemProfileService profileService,
        IDiagnosticSessionRepository sessionRepository,
        IRemediationRepository remediationRepository,
        ILogger<AutonomyController> logger)
    {
        _orchestrator = orchestrator;
        _profileService = profileService;
        _sessionRepository = sessionRepository;
        _remediationRepository = remediationRepository;
        _logger = logger;
    }

    public class DryRunRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string DiagnosedCategory { get; set; } = string.Empty;
    }

    [HttpPost("dry-run")]
    public async Task<IActionResult> DryRun([FromBody] DryRunRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
        {
            return BadRequest(new
            {
                message = "A valid diagnostic session ID is required."
            });
        }

        var diagnostic =
            await _sessionRepository.GetDiagnosticOutputAsync(sessionId);

        if (diagnostic == null)
        {
            return NotFound(new
            {
                message = "Diagnostic session was not found."
            });
        }

        var mockHardware = new HardwareProfileDto
        {
            OsVersion = "Windows 11"
        };

        var result =
            await _orchestrator.RunDryRunCycleAsync(
                diagnostic,
                mockHardware);

        if (result.Plan != null)
        {
            result.Plan.SessionId = sessionId.ToString();
        }

        // Dry-run is deliberately not persisted as remediation history.
        return Ok(result);
    }

    public class ExecuteRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string DiagnosedCategory { get; set; } = string.Empty;
        public bool UserConsentProvided { get; set; }
    }

    /// <summary>
    /// Runs a REAL remediation cycle:
    /// Plan -> Safety Check -> Execute -> Verify -> Rollback/Pivot/Escalate.
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] ExecuteRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
        {
            return BadRequest(new
            {
                message = "A valid diagnostic session ID is required."
            });
        }

        var diagnostic =
            await _sessionRepository.GetDiagnosticOutputAsync(sessionId);

        if (diagnostic == null)
        {
            return NotFound(new
            {
                message = "Diagnostic session was not found."
            });
        }

        // Real hardware is used for safety evaluation and as the
        // persisted pre-execution system snapshot.
        var hardware = _profileService.GetLiveSystemProfile();

        var result =
            await _orchestrator.RunExecutionCycleAsync(
                diagnostic,
                hardware,
                request.UserConsentProvided);

        if (result.Plan != null)
        {
            result.Plan.SessionId = sessionId.ToString();
        }

        /*
         * Persist only when at least one action actually reached the
         * execution layer.
         *
         * Safety-rejected / consent-required requests do not create
         * remediation history because no remediation was executed.
         */
        if (result.Attempts.Any(a =>
                a.Action != null &&
                a.Execution != null))
        {
            try
            {
                var run =
                    BuildRemediationRun(
                        diagnostic.Id,
                        result,
                        hardware);

                await _remediationRepository.SaveRunAsync(run);

                result.Trace +=
                    $"\n[PERSISTENCE] Remediation run saved: {run.Id}";
            }
            catch (Exception ex)
            {
                /*
                 * Do not convert this to a failed execution response.
                 *
                 * The system-changing action may already have happened.
                 * Returning a generic execution failure could encourage
                 * the client to repeat the real remediation.
                 */
                _logger.LogError(
                    ex,
                    "Remediation completed but its history could not be persisted.");

                result.Trace +=
                    "\n[PERSISTENCE] WARNING: The remediation executed, " +
                    "but its history could not be saved.";
            }
        }

        return Ok(result);
    }

    private static RemediationRun BuildRemediationRun(
        Guid diagnosticOutputId,
        OrchestrationResult result,
        HardwareProfileDto hardware)
    {
        var run = new RemediationRun
        {
            DiagnosticOutputId = diagnosticOutputId,
            Status = DetermineRunStatus(result),
            CompletedAt = DateTimeOffset.UtcNow
        };

        var persistedAttempts =
            new List<(RemediationAttempt Source, ActionAttempt Entity)>();

        foreach (var sourceAttempt in result.Attempts)
        {
            if (sourceAttempt.Action == null ||
                sourceAttempt.Execution == null)
            {
                continue;
            }

            var actionAttempt = new ActionAttempt
            {
                RemediationRunId = run.Id,
                ActionCode = sourceAttempt.Action.Id,

                // This is the real system state captured before the
                // remediation cycle began.
                PreconditionState = JsonSerializer.Serialize(hardware)
            };

            if (sourceAttempt.Verification.HasValue)
            {
                var verificationStatus =
                    sourceAttempt.Verification.Value;

                actionAttempt.Verification =
                    new VerificationResult
                    {
                        ActionAttemptId = actionAttempt.Id,
                        IsSuccessful =
                            verificationStatus ==
                            VerificationStatus.Resolved,

                        ObservedState =
                            JsonSerializer.Serialize(new
                            {
                                verification =
                                    verificationStatus.ToString(),

                                executionSummary =
                                    sourceAttempt.Execution.Summary,

                                proof =
                                    sourceAttempt.Execution.Proof
                            }),

                        FailureReason =
                            verificationStatus ==
                            VerificationStatus.Resolved
                                ? null
                                : verificationStatus.ToString()
                    };
            }

            run.ActionAttempts.Add(actionAttempt);

            persistedAttempts.Add(
                (sourceAttempt, actionAttempt));
        }

        foreach (var persistedAttempt in persistedAttempts)
        {
            var sourceAttempt = persistedAttempt.Source;
            var actionAttempt = persistedAttempt.Entity;

            if (sourceAttempt.RollbackResult != null)
            {
                run.RollbackEvents.Add(
                    new RollbackEvent
                    {
                        RemediationRunId = run.Id,
                        ActionAttemptId = actionAttempt.Id,

                        WasSuccessful =
                            sourceAttempt.RollbackResult.Success,

                        RestoredState =
                            JsonSerializer.Serialize(new
                            {
                                summary =
                                    sourceAttempt.RollbackResult.Summary,

                                proof =
                                    sourceAttempt.RollbackResult.Proof
                            })
                    });
            }
        }

        /*
         * A pivoted attempt is followed by the alternative action selected
         * by the orchestrator. Persist that transition when both executed
         * attempts are available.
         */
        for (var i = 0; i < persistedAttempts.Count - 1; i++)
        {
            var current = persistedAttempts[i];
            var next = persistedAttempts[i + 1];

            if (current.Source.State !=
                RemediationAttemptState.Pivoted)
            {
                continue;
            }

            run.PivotEvents.Add(
                new PivotEvent
                {
                    RemediationRunId = run.Id,

                    FromActionCode =
                        current.Source.Action?.Id ??
                        current.Entity.ActionCode,

                    ToActionCode =
                        next.Source.Action?.Id ??
                        next.Entity.ActionCode,

                    Reason =
                        string.IsNullOrWhiteSpace(
                            current.Source.Notes)
                            ? "Orchestrator pivoted to an alternative remediation action."
                            : current.Source.Notes
                });
        }

        return run;
    }

    private static string DetermineRunStatus(
        OrchestrationResult result)
    {
        if (result.Escalated)
        {
            return "Escalated";
        }

        var lastAttempt =
            result.Attempts.LastOrDefault();

        if (lastAttempt != null)
        {
            return lastAttempt.State.ToString();
        }

        if (result.Verification.HasValue)
        {
            return result.Verification.Value.ToString();
        }

        return result.Execution?.Success == true
            ? "Completed"
            : "Failed";
    }
}
