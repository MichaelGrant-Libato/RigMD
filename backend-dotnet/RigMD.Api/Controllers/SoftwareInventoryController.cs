using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/software-inventory")]
public sealed class SoftwareInventoryController(
    ISoftwareInventoryProvider inventory, IAppUninstallService uninstall) : ControllerBase
{
    [HttpGet("apps")]
    public async Task<ActionResult<InstalledAppInventory>> Apps(CancellationToken cancellationToken) =>
        Ok(await inventory.ReadAppsAsync(cancellationToken));

    [HttpGet("drivers")]
    public async Task<ActionResult<SoftwareInventory<InstalledDriver>>> Drivers(CancellationToken cancellationToken) =>
        Ok(await inventory.ReadDriversAsync(cancellationToken));

    [HttpPost("apps/uninstall-review")]
    public async Task<ActionResult<AppUninstallReview>> ReviewUninstall(
        AppUninstallReviewRequest request, CancellationToken cancellationToken) =>
        Ok(await uninstall.ReviewAsync(request.AppId, cancellationToken));

    [HttpPost("apps/uninstall")]
    public async Task<ActionResult<AppUninstallResult>> Uninstall(
        AppUninstallRequest request, CancellationToken cancellationToken) =>
        Ok(await uninstall.UninstallAsync(request.ApprovalId, cancellationToken));

    public sealed record AppUninstallReviewRequest([Required, StringLength(512)] string AppId);
    public sealed record AppUninstallRequest(Guid ApprovalId);
}
