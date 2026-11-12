using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;

namespace RigMD.Api.Controllers;

// Protected by the same per-launch local API authentication as every /api endpoint.
[ApiController]
[Route("api/desktop")]
public sealed class DesktopLifetimeController(LocalRemediationExecutionGate gate) : ControllerBase
{
    [HttpGet("recovery")]
    public IActionResult Recovery() => Ok(new { blocked = gate.RecoveryBlockReason != null, message = gate.RecoveryBlockReason ?? "" });
    [HttpPost("prepare-exit")]
    public IActionResult PrepareExit() => gate.TryBeginShutdown()
        ? Ok(new { canExit = true })
        : Conflict(new { canExit = false, message = "A system action is running or its completion cannot be confirmed. Keep RigMD open until the action finishes." });
}
