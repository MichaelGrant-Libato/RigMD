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