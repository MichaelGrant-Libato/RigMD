using System;
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
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
