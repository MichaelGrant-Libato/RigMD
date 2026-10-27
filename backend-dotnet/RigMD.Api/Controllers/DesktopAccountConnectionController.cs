using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Autonomy;
namespace RigMD.Api.Controllers;

[ApiController]
public sealed class DesktopAccountConnectionController(DesktopAccountConnection connection) : ControllerBase
{
    [HttpPost("api/cloud-ai/connect")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        try { return Ok(await connection.StartAsync(cancellationToken)); }
        catch (CloudAiSignInException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }
    [HttpGet("api/cloud-ai/connect")]
    public IActionResult Status() => Ok(connection.Status);
    [HttpPost("api/cloud-ai/connect/cancel")]
    public async Task<IActionResult> Cancel() { await connection.CancelAsync(); return Ok(connection.Status); }

    // This callback accepts ONLY an encrypted, short-lived envelope for a pending local key.
    // Ordinary /api routes retain their per-launch authentication and origin checks.
    [HttpPost("desktop-account-connect"), RequestSizeLimit(30000), RequestFormLimits(ValueLengthLimit = 22000, ValueCountLimit = 2)]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = DesktopAccountConnectionPage.ContentSecurityPolicy;
        if (!DesktopAccountConnection.IsTrustedCallback(HttpContext) || !Request.HasFormContentType) return StatusCode(403);
        var form = await Request.ReadFormAsync(cancellationToken);
        if (form.Count != 2 || form["state"].Count != 1 || form["payload"].Count != 1) return BadRequest();
        var connected = await connection.CompleteAsync(form["state"].ToString(), form["payload"].ToString(), cancellationToken);
        Response.StatusCode = connected ? 200 : 400;
        return Content(DesktopAccountConnectionPage.Render(connected), "text/html");
    }
}
