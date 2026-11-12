using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Services;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/autonomy/storage-apps")]
public sealed class StorageAppsController(AppUninstallService uninstalls, IDiagnosticSessionRepository sessions) : ControllerBase
{
    [HttpGet]
    public IActionResult Get(CancellationToken cancellationToken, [FromQuery] bool includeStore = false)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(StorageAppInventory.Capture(cancellationToken, includeStore: includeStore));
    }

    public sealed class UninstallRequest
    {
        public string SessionId { get; set; } = "";
        public string MetricId { get; set; } = "";
        public string AppId { get; set; } = "";
        public string? ApprovalToken { get; set; }
        public bool Confirmed { get; set; }
    }

    private async Task<IActionResult?> Guard(UninstallRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId) || string.IsNullOrWhiteSpace(request.MetricId) || string.IsNullOrWhiteSpace(request.AppId))
            return BadRequest(new { message = "A saved storage check and selected app are required." });
        var session = await sessions.GetSessionAsync(sessionId);
        if (session == null) return NotFound(new { message = "The saved check was not found." });
        var eligibility = RemediationEligibilityPolicy.Evaluate(session, await sessions.GetSessionsAsync());
        if (eligibility.State != "allowed") return Conflict(new { message = eligibility.Message, remediation_guard = eligibility });
        var diagnostic = await sessions.GetDiagnosticOutputAsync(sessionId);
        if (diagnostic == null || RepairSupportPolicy.SelectStorageFinding(diagnostic, request.MetricId, "uninstall_installed_app") == null)
            return Conflict(new { message = "This check does not authorize an uninstall. Run a current storage check with a verified storage warning." });
        return null;
    }

    [HttpPost("uninstall/preview")]
    public async Task<IActionResult> Preview([FromBody] UninstallRequest request)
    {
        if (await Guard(request) is { } block) return block;
        try { return Ok(uninstalls.Preview(Guid.Parse(request.SessionId), request.MetricId, request.AppId)); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) { return StatusCode(503, new { message = "An uninstall preview could not be obtained. No uninstall was started by this preview." }); }
    }

    [HttpPost("uninstall/execute")]
    public async Task<IActionResult> Execute([FromBody] UninstallRequest request)
    {
        if (!request.Confirmed) return BadRequest(new { message = "Explicit uninstall approval is required." });
        if (await Guard(request) is { } block) return block;
        try { return Ok(uninstalls.Start(Guid.Parse(request.SessionId), request.MetricId, request.AppId, request.ApprovalToken, request.Confirmed)); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) { return StatusCode(503, new { message = "The uninstall request could not be completed. Refresh operation status before retrying; do not assume the operation failed." }); }
    }

    [HttpGet("uninstall/operations/{id}")]
    public IActionResult Operation(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return BadRequest(new { message = "Invalid uninstall operation." });
        var operation = uninstalls.Find(id);
        return operation == null ? NotFound(new { message = "This operation is not available in the current RigMD session. Do not assume it failed or repeat an uninstall without checking the app." }) : Ok(operation);
    }

    [HttpGet("uninstall/operations")]
    public IActionResult Recent([FromQuery] string sessionId) => Guid.TryParse(sessionId, out var id)
        ? Ok(uninstalls.Recent(id)) : BadRequest(new { message = "A valid saved check is required." });
}
