using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Services;
using RigMD.Infrastructure;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/warning-signs")]
public class WarningSignsController : ControllerBase
{
    private readonly DatabaseSessionService
        _databaseSessionService;

    private readonly WarningSignService
        _warningSignService;

    public WarningSignsController(
        DatabaseSessionService databaseSessionService,
        WarningSignService warningSignService)
    {
        _databaseSessionService =
            databaseSessionService;

        _warningSignService =
            warningSignService;
    }

    [HttpGet("reference")]
    public async Task<IActionResult>
        GetReference(
            [FromQuery] string category = "all",
            [FromQuery] string search = "",
            [FromQuery] bool observed_only = false)
    {
        try
        {
            var observedTexts =
                await _databaseSessionService
                    .GetObservedWarningTextsAsync();

            var result =
                _warningSignService
                    .BuildReference(
                        observedTexts,
                        category,
                        search,
                        observed_only
                    );

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to load warning signs: {ex}"
            );

            var fallback =
                _warningSignService
                    .EmptyResponse(
                        "Warning sign data could not be loaded."
                    );

            return Ok(fallback);
        }
    }
}