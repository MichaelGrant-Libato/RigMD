using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutonomyController : ControllerBase
{
    private readonly IAutonomousOrchestrator _orchestrator;

    public AutonomyController(IAutonomousOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public class DryRunRequest
    {
        public string DiagnosedCategory { get; set; } = string.Empty;
    }

    [HttpPost("dry-run")]
    public async Task<IActionResult> DryRun([FromBody] DryRunRequest request)
    {
        // For dry run we just mock the diagnostic output and hardware
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
}
