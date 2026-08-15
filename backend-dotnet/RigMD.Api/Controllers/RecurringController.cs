using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Services;
using RigMD.Infrastructure;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/recurring")]
public class RecurringController : ControllerBase
{
    private readonly DatabaseSessionService
        _databaseSessionService;

    private readonly RecurringPatternService
        _recurringPatternService;

    public RecurringController(
        DatabaseSessionService databaseSessionService,
        RecurringPatternService recurringPatternService)
    {
        _databaseSessionService =
            databaseSessionService;

        _recurringPatternService =
            recurringPatternService;
    }

    // =========================================================
    // GET ALL RECURRING PATTERNS
    // =========================================================

    [HttpGet("patterns")]
    public async Task<IActionResult>
        GetRecurringPatterns()
    {
        try
        {
            var sessions =
                await _databaseSessionService
                    .GetRecurringSessionsAsync();

            var result =
                _recurringPatternService
                    .BuildPatterns(
                        sessions
                    );

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to load recurring patterns: {ex}"
            );

            return Ok(
                RecurringPatternService
                    .EmptyResponse(
                        "Recurring pattern data could not be loaded."
                    )
            );
        }
    }

    // =========================================================
    // GET SINGLE PATTERN
    // =========================================================

    [HttpGet("patterns/{patternId}")]
    public async Task<IActionResult>
        GetRecurringPattern(
            string patternId)
    {
        try
        {
            var sessions =
                await _databaseSessionService
                    .GetRecurringSessionsAsync();

            var result =
                _recurringPatternService
                    .BuildPatterns(
                        sessions
                    );

            var pattern =
                result.patterns
                    .FirstOrDefault(
                        item =>
                            string.Equals(
                                item.id,
                                patternId,
                                StringComparison
                                    .OrdinalIgnoreCase
                            )
                    );

            return Ok(new
            {
                pattern
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to load recurring pattern: {ex}"
            );

            return Ok(new
            {
                pattern =
                    (object?)null
            });
        }
    }
}