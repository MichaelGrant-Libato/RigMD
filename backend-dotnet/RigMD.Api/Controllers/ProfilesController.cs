using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private readonly IDiagnosticSessionRepository _sessionRepository;

    public ProfilesController(IDiagnosticSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfiles()
    {
        try
        {
            var profiles = await _sessionRepository.GetProfilesAsync();
            return Ok(profiles);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load profiles: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { detail = "Failed to load hardware profiles." });
        }
    }
}