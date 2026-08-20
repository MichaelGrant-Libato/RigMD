using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDiagnosticSessionRepository _sessionRepository;

    public DashboardController(IDiagnosticSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        try
        {
            var dashboard = await _sessionRepository.GetDashboardSummaryAsync();
            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load dashboard summary: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Dashboard data could not be loaded." });
        }
    }
}