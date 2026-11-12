using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Services;

namespace RigMD.Api.Controllers;

[ApiController, Route("api/autonomy/storage-cleanup")]
public sealed class StorageCleanupController(StorageFileCleanupService cleanup, IDiagnosticSessionRepository sessions) : ControllerBase
{
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    public sealed class CleanupRequest
    {
        public string SessionId { get; set; } = "";
        public string MetricId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string[] FileIds { get; set; } = [];
        public string? ApprovalToken { get; set; }
        public bool Confirmed { get; set; }
    }
    private async Task<IActionResult?> Guard(CleanupRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId) || string.IsNullOrWhiteSpace(request.MetricId)) return BadRequest(new { message = "Choose a current storage check." });
        var session = await sessions.GetSessionAsync(sessionId);
        if (session == null) return NotFound(new { message = "This saved check was not found." });
        var eligibility = RemediationEligibilityPolicy.Evaluate(session, await sessions.GetSessionsAsync());
        if (eligibility.State != "allowed") return Conflict(new { message = eligibility.Message, remediation_guard = eligibility });
        var diagnostic = await sessions.GetDiagnosticOutputAsync(sessionId);
        if (diagnostic == null || RepairSupportPolicy.SelectStorageFinding(diagnostic, request.MetricId, "recycle_selected_files") == null)
            return Conflict(new { message = "A current measured storage warning is required before cleanup." });
        return null;
    }
    [HttpGet("files")]
    public async Task<IActionResult> Files([FromQuery] string metricId, [FromQuery] string categoryId, CancellationToken cancellationToken)
    {
        if (!await ScanGate.WaitAsync(0, cancellationToken)) return StatusCode(429, new { message = "Another personal-file scan is running. Try again shortly." });
        try { return Ok(cleanup.Files(metricId, categoryId, cancellationToken)); }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) { return StatusCode(503, new { message = "Personal files could not be scanned. Nothing was changed." }); }
        finally { ScanGate.Release(); }
    }
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] CleanupRequest request)
    {
        if (await Guard(request) is {} block) return block;
        try { return Ok(cleanup.Preview(Guid.Parse(request.SessionId), request.MetricId, request.Kind, request.FileIds)); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) { return StatusCode(503, new { message = "A cleanup preview could not be read. Nothing was changed." }); }
    }
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] CleanupRequest request)
    {
        if (!request.Confirmed) return BadRequest(new { message = "Explicit cleanup approval is required." });
        if (await Guard(request) is {} block) return block;
        try { return Ok(cleanup.Start(Guid.Parse(request.SessionId), request.MetricId, request.ApprovalToken, request.Confirmed)); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (Exception) { return StatusCode(503, new { message = "Cleanup status is uncertain. Check operation status before retrying." }); }
    }
    [HttpGet("operations/{id}")]
    public IActionResult Operation(string id) => Guid.TryParseExact(id, "N", out _) && cleanup.Find(id) is {} operation
        ? Ok(operation) : NotFound(new { message = "This cleanup receipt is unavailable. Do not assume that cleanup failed or repeat it automatically." });
    [HttpGet("operations")]
    public IActionResult Recent([FromQuery] string sessionId, [FromQuery] string metricId) => Guid.TryParse(sessionId, out var id)
        ? Ok(cleanup.Recent(id, metricId)) : BadRequest(new { message = "Choose a valid saved storage check." });
}
