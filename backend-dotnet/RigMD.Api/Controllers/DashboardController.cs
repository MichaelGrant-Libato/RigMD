using System;
using Microsoft.AspNetCore.Mvc;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    [HttpGet("summary")]
    public IActionResult GetDashboardSummary()
    {
        // Database persistence is not yet wired in the C# backend.
        // Return the empty-but-valid dashboard shape so the frontend renders
        // without errors. Once EF Core session tracking is implemented in a
        // later phase, this endpoint will query real data.
        return Ok(new
        {
            server_time = DateTime.UtcNow.ToString("o"),
            totals = new
            {
                total_sessions = 0,
                this_month_count = 0,
                escalated_count = 0
            },
            last_diagnosis = (object?)null,
            current_action_status = (object?)null,
            recurring_issues_count = 0,
            warning_signs_active_count = 0,
            action_distribution = new[]
            {
                new { label = "Monitor", count = 0 },
                new { label = "Maintain", count = 0 },
                new { label = "Troubleshoot", count = 0 },
                new { label = "Escalate", count = 0 }
            },
            session_frequency = Array.Empty<object>(),
            recent_warning_signs = Array.Empty<object>(),
            last_saved_session = (object?)null
        });
    }
}
