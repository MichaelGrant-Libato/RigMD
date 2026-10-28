using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Infrastructure.Persistence;

public sealed class SessionFeedbackRepository(RigMdDbContext db, ICurrentClientProvider clientProvider, TimeProvider clock)
    : ISessionFeedbackRepository
{
    private IQueryable<DiagnosticSession> OwnedSession(Guid sessionId)
    {
        var clientId = clientProvider.GetCurrentClientId();
        return db.DiagnosticSessions.Where(s => s.Id == sessionId && !string.IsNullOrWhiteSpace(clientId) &&
            (s.Profile.ClientId == clientId || s.Answers.Any(a => a.QuestionKey == "client_id" && a.AnswerValue == clientId)));
    }

    public async Task<IReadOnlyList<SessionFeedbackDto>?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!await OwnedSession(sessionId).AnyAsync(cancellationToken)) return null;
        return await db.SessionFeedback.AsNoTracking().Where(f => f.DiagnosticSessionId == sessionId)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id).Take(20)
            .Select(f => new SessionFeedbackDto(f.Id, f.DiagnosticSessionId, f.Outcome, f.Note, f.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SessionFeedbackDto?> SaveAsync(Guid sessionId, SaveSessionFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        if (!SaveSessionFeedbackRequest.IsKnownOutcome(request.Outcome) || request.Note?.Length > SaveSessionFeedbackRequest.MaxNoteLength)
            throw new ArgumentException("Choose a valid answer and keep the note within 500 characters.", nameof(request));
        if (!await OwnedSession(sessionId).AnyAsync(cancellationToken)) return null;
        var feedback = new SessionFeedback
        {
            DiagnosticSessionId = sessionId, Outcome = request.Outcome,
            Note = request.Note?.Trim() ?? string.Empty, CreatedAt = clock.GetUtcNow().ToUniversalTime()
        };
        db.SessionFeedback.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);
        return new SessionFeedbackDto(feedback.Id, sessionId, feedback.Outcome, feedback.Note, feedback.CreatedAt);
    }
}
