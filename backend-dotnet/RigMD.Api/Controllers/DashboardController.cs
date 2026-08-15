using Microsoft.AspNetCore.Mvc;
using RigMD.Infrastructure;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly DatabaseSessionService _databaseSessionService;

    public DashboardController(
        DatabaseSessionService databaseSessionService)
    {
        _databaseSessionService = databaseSessionService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        try
        {
            var dashboard =
                await _databaseSessionService.GetDashboardSummaryAsync();

            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to load dashboard summary: {ex}"
            );

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = "Dashboard data could not be loaded."
                }
            );
        }
    }
}