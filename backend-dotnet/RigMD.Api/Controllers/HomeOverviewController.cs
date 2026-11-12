using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/home")]
public sealed class HomeOverviewController(IHomeOverviewReader reader) : ControllerBase
{
    [HttpGet("overview")]
    public ActionResult<HomeOverviewSnapshot> GetOverview() => Ok(reader.Read());
}
