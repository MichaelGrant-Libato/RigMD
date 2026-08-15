using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Models;
using RigMD.Infrastructure;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private readonly DatabaseSessionService
        _databaseSessionService;

    public ProfilesController(
        DatabaseSessionService databaseSessionService)
    {
        _databaseSessionService =
            databaseSessionService;
    }

    [HttpPost("save")]
    public async Task<IActionResult> SaveProfile(
        [FromBody] SaveProfileRequestDto request)
    {
        try
        {
            var profile =
                await _databaseSessionService
                    .SaveProfileAsync(request);

            return Ok(profile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to save profile: {ex}"
            );

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    detail =
                        "Failed to save hardware profile."
                }
            );
        }
    }
}