using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Persistence;

public interface ISessionFeedbackRepository
{
    /// <summary>Latest twenty observations, newest first; null for inaccessible or missing checks.</summary>
    Task<IReadOnlyList<SessionFeedbackDto>?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
    /// <summary>Appends a user observation without changing evidence or verification fields.</summary>
    Task<SessionFeedbackDto?> SaveAsync(Guid sessionId, SaveSessionFeedbackRequest request, CancellationToken cancellationToken = default);
}
