using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/device-diagnostics")]
public sealed class DeviceDiagnosticsController(DeviceDiagnosticRunner runner,
    IDiagnosticSessionRepository sessions, ILogger<DeviceDiagnosticsController> logger) : ControllerBase
{
    [HttpGet("connection")]
    public Task<IActionResult> Connection(CancellationToken token) =>
        RunAsync("connection", WindowsConnectionTest.RunAsync, token);

    [HttpGet("app-activity")]
    public Task<IActionResult> AppActivity(CancellationToken token) =>
        RunAsync("process activity", ProcessActivityReader.CaptureAsync, token);

    [HttpGet("drives/{number:int:min(0)}/health")]
    public Task<IActionResult> DriveHealth(int number, CancellationToken token) =>
        RunAsync("drive health", ct => WindowsHardwareHealth.ReadDriveAsync(number, ct), token);

    [HttpGet("battery-health")]
    public Task<IActionResult> BatteryHealth(CancellationToken token) =>
        RunAsync("battery health", WindowsHardwareHealth.ReadBatteryAsync, token);

    [HttpGet("changes/{sessionId:guid}")]
    public async Task<IActionResult> Changes(Guid sessionId, CancellationToken token)
    {
        var session = await sessions.GetSessionAsync(sessionId);
        if (session is null) return NotFound(new { detail = "Saved check not found." });
        if (!RecentWindowsChanges.BelongsToThisDevice(session.DeviceId))
            return Conflict(new { detail = "This saved check could not be matched to this PC." });
        if (!DateTimeOffset.TryParse(session.CreatedAt, out var baseline) || baseline > DateTimeOffset.UtcNow)
            return Conflict(new { detail = "The collection time for this saved check is unavailable." });
        return await RunAsync("recent changes", ct => RecentWindowsChanges.ReadAsync(baseline, ct), token);
    }

    private async Task<IActionResult> RunAsync<T>(string name, Func<CancellationToken, Task<T>> read, CancellationToken token)
        where T : class
    {
        try
        {
            var reading = await runner.TryRunAsync(name, read, token);
            return reading is null
                ? Conflict(new { detail = "This reading is already being collected. Try again shortly." })
                : Ok(reading);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return StatusCode(504, new { detail = "Windows did not finish this reading in time. Try again shortly." }); }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("Windows could not collect {Reading} ({ErrorType}).", name, ex.GetType().Name);
            return StatusCode(503, new { detail = "Windows could not provide this reading." });
        }
    }
}
