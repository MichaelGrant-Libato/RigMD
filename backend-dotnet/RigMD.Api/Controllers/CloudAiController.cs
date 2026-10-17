using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Autonomy;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/cloud-ai")]
public sealed class CloudAiController(ICloudAiSession session) : ControllerBase
{
    public sealed class SignInRequest
    {
        [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
        [Required, StringLength(1024, MinimumLength = 1)] public string Password { get; set; } = "";
    }

    [HttpGet("account")]
    public IActionResult Account() => Ok(session.Status);

    [HttpPost("sign-in"), RequestSizeLimit(8192)]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await session.SignInAsync(request.Email, request.Password, cancellationToken)); }
        catch (CloudAiSignInException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }

    [HttpPost("sign-out")]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        await session.SignOutAsync(cancellationToken);
        return Ok(session.Status);
    }
}
