using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutonomyController : ControllerBase
{
    private readonly IAutonomousOrchestrator _orchestrator;
    private readonly IVerificationService _verificationService;
    private readonly IWindowsSystemProfileService _profileService;

    public AutonomyController(
        IAutonomousOrchestrator orchestrator,
        IVerificationService verificationService,
        IWindowsSystemProfileService profileService)
    {
        _orchestrator = orchestrator;
        _verificationService = verificationService;
        _profileService = profileService;
    }

    public class DryRunRequest
    {
        public string DiagnosedCategory { get; set; } = string.Empty;
    }

    [HttpPost("dry-run")]
    public async Task<IActionResult> DryRun([FromBody] DryRunRequest request)
    {
        var mockDiagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = request.DiagnosedCategory,
            ActionCategory = "Test Action",
            ConfidenceLabel = "High"
        };

        var mockHardware = new HardwareProfileDto
        {
            OsVersion = "Windows 11"
        };

        var result = await _orchestrator.RunDryRunCycleAsync(mockDiagnostic, mockHardware);

        return Ok(result);
    }

    public class ExecuteRequest
    {
        public string DiagnosedCategory { get; set; } = string.Empty;
    }

    /// <summary>
    /// Runs a REAL remediation cycle: Plan → Safety Check → Execute → Verify.
    /// This endpoint will make actual changes to the system (e.g., delete temp files).
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] ExecuteRequest request)
    {
        // Use real system hardware profile for safety evaluation
        var hardware = _profileService.GetLiveSystemProfile();

        var diagnostic = new DiagnosticOutput
        {
            DiagnosedCategory = request.DiagnosedCategory,
            ActionCategory = request.DiagnosedCategory,
            ConfidenceLabel = "High"
        };

        // Run the orchestrator (which now uses the real WindowsRemediationExecutor)
        var result = await _orchestrator.RunDryRunCycleAsync(diagnostic, hardware);

        // If execution succeeded, run verification on the first action
        if (result.Execution?.Success == true && result.Plan?.PlannedActions.Count > 0)
        {
            var verificationStatus = await _verificationService.VerifyAsync(
                result.Plan.PlannedActions[0], hardware);

            result.Trace += $"\n[VERIFICATION] Post-execution status: {verificationStatus}";
        }

        return Ok(result);
    }
}

