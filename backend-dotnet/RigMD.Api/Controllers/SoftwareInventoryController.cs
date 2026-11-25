using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/software-inventory")]
public sealed class SoftwareInventoryController(ISoftwareInventoryProvider inventory) : ControllerBase
{
    [HttpGet("apps")]
    public async Task<ActionResult<InstalledAppInventory>> Apps(CancellationToken cancellationToken) =>
        Ok(await inventory.ReadAppsAsync(cancellationToken));

    [HttpGet("drivers")]
    public async Task<ActionResult<SoftwareInventory<InstalledDriver>>> Drivers(CancellationToken cancellationToken) =>
        Ok(await inventory.ReadDriversAsync(cancellationToken));
}
