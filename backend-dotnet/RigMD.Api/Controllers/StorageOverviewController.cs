using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/autonomy/storage-overview")]
public sealed class StorageOverviewController(IAppUninstallPlatform platform) : ControllerBase
{
    // A scan is bounded and only one scan runs at a time. No scan continues after its caller cancels.
    private static readonly SemaphoreSlim ScanGate = new(1, 1);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string metricId, CancellationToken cancellationToken)
    {
        var drive = StorageOverviewReader.DriveForMetric(metricId ?? "", Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        if (drive == null) return BadRequest(new { message = "Select a storage-space finding to review." });
        if (!await ScanGate.WaitAsync(0, cancellationToken))
            return StatusCode(429, new { message = "A storage review is already loading. Try again shortly." });
        try
        {
            var volume = platform.ReadVolumes().SingleOrDefault(volume =>
                string.Equals(volume.Drive.TrimEnd('\\'), drive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (volume == null || volume.TotalBytes <= 0 || volume.FreeBytes < 0 || volume.FreeBytes > volume.TotalBytes)
                return NotFound(new { message = "This drive’s current storage reading is unavailable. Check that it is connected and try again." });
            Response.Headers.CacheControl = "no-store";
            return Ok(StorageOverviewReader.Capture(volume, cancellationToken));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return StatusCode(503, new { message = "Storage could not be read. Nothing was changed. Try again shortly." }); }
        finally { ScanGate.Release(); }
    }
}
