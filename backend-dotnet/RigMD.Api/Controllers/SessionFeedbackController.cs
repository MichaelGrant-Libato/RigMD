using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/diagnosis/sessions/{sessionId:guid}/feedback")]
public sealed class SessionFeedbackController(ISessionFeedbackRepository repository, ILogger<SessionFeedbackController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var feedback = await repository.GetAsync(sessionId, cancellationToken);
            return feedback is null ? NotFound(new { detail = "Saved check not found." }) : Ok(feedback);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not load session feedback.");
            return StatusCode(500, new { detail = "Your feedback could not be loaded. Please try again." });
        }
    }

    [HttpPost, RequestSizeLimit(8192)]
    public async Task<IActionResult> Save(Guid sessionId, [FromBody] SaveSessionFeedbackRequest request, CancellationToken cancellationToken)
    {
        // Validate here as well as through ApiController, so all callers receive the same guard.
        if (!SaveSessionFeedbackRequest.IsKnownOutcome(request.Outcome) || request.Note?.Length > SaveSessionFeedbackRequest.MaxNoteLength)
            return BadRequest(new { detail = "Choose an answer and keep the note within 500 characters." });
        try
        {
            var feedback = await repository.SaveAsync(sessionId, request, cancellationToken);
            return feedback is null ? NotFound(new { detail = "Saved check not found." }) : Ok(feedback);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not save session feedback.");
            return StatusCode(500, new { detail = "Your feedback could not be saved. Please try again." });
        }
    }
}
