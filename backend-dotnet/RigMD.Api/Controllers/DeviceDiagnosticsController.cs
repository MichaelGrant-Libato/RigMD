using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/device-diagnostics")]
public sealed class DeviceDiagnosticsController(DeviceDiagnosticRunner runner,
    IDiagnosticSessionRepository sessions, ILogger<DeviceDiagnosticsController> logger,
    IStorageProvider? storage = null) : ControllerBase
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

    [HttpGet("boot-history")]
    public Task<IActionResult> BootHistory(CancellationToken token) =>
        RunAsync("boot history", ct => Task.Run(() => WindowsBootHistory.Read(ct), ct), token);

    [HttpGet("checks/{sessionId:guid}/drives/{number:int:min(0)}/health")]
    public async Task<IActionResult> SavedCheckDriveHealth(Guid sessionId, int number, CancellationToken token)
    {
        var session = await sessions.GetSessionAsync(sessionId);
        if (session is null) return NotFound(new { detail = "Saved check not found." });
        if (!RecentWindowsChanges.BelongsToThisDevice(session.DeviceId))
            return Conflict(new { detail = "This saved check could not be matched to this PC." });
        var target = session.DriveHealthTargets.FirstOrDefault(drive => drive.Number == number);
        if (target is null) return NotFound(new { detail = "The saved check has no matching drive." });
        return await RunAsync("drive health", async ct =>
        {
            var current = await Task.Run(() => (Drives: storage?.GetStorageDrives(), Volumes: storage?.GetAllDisks()), ct);
            var match = current.Drives?.FirstOrDefault(drive => drive.DiskIndex == number);
            if (match is null || !string.Equals(match.Model, target.Model, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(match.SizeGb - target.SizeGb) > 0.1 ||
                !target.Volumes.All(volume => current.Volumes?.Any(item => item.DiskIndex == number &&
                    item.Drive.Equals(volume, StringComparison.OrdinalIgnoreCase)) == true))
                return new DriveHealthReading("drive changed", DateTimeOffset.UtcNow);
            return await WindowsHardwareHealth.ReadDriveAsync(number, ct);
        }, token);
    }

    [HttpGet("checks/{sessionId:guid}/crash-events")]
    public async Task<IActionResult> CrashEvents(Guid sessionId, CancellationToken token)
    {
        var session = await sessions.GetSessionAsync(sessionId);
        if (session is null) return NotFound(new { detail = "Saved check not found." });
        if (!RecentWindowsChanges.BelongsToThisDevice(session.DeviceId))
            return Conflict(new { detail = "This saved check could not be matched to this PC." });
        var scenario = RigMD.Application.Services.Autonomy.DiagnosticScopeMapper.NormalizeScenarioId(session.ScenarioId);
        if (scenario is not ("app-crashes" or "blue-screen-crash"))
            return NotFound(new { detail = "This saved check has no crash event scope." });
        var application = scenario == "app-crashes";
        var window = RecentWindowsChanges.CrashWindow(session.Proof, application, DateTimeOffset.UtcNow);
        if (window is null) return Conflict(new { detail = "The saved crash event window is unavailable." });
        return await RunAsync("crash events", ct => Task.Run(() =>
            RecentWindowsChanges.ReadCrashRecords(window.Value.Start, window.Value.End, application, ct), ct), token);
    }

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
