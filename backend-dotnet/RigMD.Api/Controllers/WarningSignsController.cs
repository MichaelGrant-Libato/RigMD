using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Services;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/warning-signs")]
public class WarningSignsController : ControllerBase
{
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly WarningSignService _warningSignService;

    public WarningSignsController(
        IDiagnosticSessionRepository sessionRepository,
        WarningSignService warningSignService)
    {
        _sessionRepository = sessionRepository;
        _warningSignService = warningSignService;
    }

    [HttpGet("{referenceId}/checks")]
    public async Task<IActionResult> GetChecks(string referenceId, [FromQuery] int offset = 0, [FromQuery] int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 50) return BadRequest("Invalid saved-check page.");
        var canonical = referenceId.ToUpperInvariant();
        if (!_warningSignService.BuildReference(Array.Empty<string>()).warning_signs.Any(w => w.id == canonical)) return NotFound();
        try { return Ok(await _sessionRepository.GetWarningChecksAsync(canonical, offset, limit)); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load warning checks: {ex}");
            return Problem(statusCode: 503, title: "Saved warning checks could not be loaded.");
        }
    }

    [HttpGet("reference")]
    public async Task<IActionResult> GetReference(
        [FromQuery] string category = "all",
        [FromQuery] string search = "",
        [FromQuery] bool observed_only = false)
    {
        try
        {
            var observedTexts = await _sessionRepository.GetObservedWarningTextsAsync();
            var result = _warningSignService.BuildReference(observedTexts, category, search, observed_only);
            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load warning signs: {ex}");
            var fallback = _warningSignService.EmptyResponse("Warning sign data could not be loaded.");
            return Ok(fallback);
        }
    }
}