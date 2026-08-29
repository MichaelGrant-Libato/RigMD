using RigMD.Application.Models;
using RigMD.Domain.Rules;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Persistence;

/// <summary>
/// Repository abstraction for all diagnostic session persistence operations.
/// Implementations must not expose raw SQL or database provider details.
/// </summary>
public interface IDiagnosticSessionRepository
{
    /// <summary>Persists a new diagnostic session and returns its generated ID.</summary>
    Task<Guid> SaveDiagnosisAsync(
        DiagnosticSymptomPayload payload,
        HardwareProfileDto hardware,
        string diagnosedCategory,
        string actionCategory,
        string confidenceLabel,
        string aiExplanation,
        string clientId = "");

    Task<Guid> SaveAutomaticDiagnosisAsync(
        HardwareProfileDto hardware,
        string diagnosisMode,
        IReadOnlyList<string> componentIds,
        string? scenarioId,
        Guid commandId,
        string agentId,
        string diagnosedCategory,
        string actionCategory,
        string confidenceLabel,
        string explanation,
        string clientId = "");

    /// <summary>Returns all sessions for the current client, newest first.</summary>
    Task<IReadOnlyList<DiagnosticSessionDto>> GetSessionsAsync();

    /// <summary>Returns a single session by ID, or null if not found.</summary>
    Task<DiagnosticSessionDto?> GetSessionAsync(Guid sessionId);

    /// <summary>
    /// Returns the persisted diagnostic output for a session owned by the current client,
    /// or null when the session does not exist or is not accessible.
    /// </summary>
    Task<DiagnosticOutput?> GetDiagnosticOutputAsync(Guid sessionId);

    /// <summary>Updates the resolution outcome fields on an existing session.</summary>
    Task<bool> UpdateResolutionAsync(
        Guid sessionId,
        string resolutionStatus,
        string resolutionCheckedAt,
        string resolutionSummary,
        object[] resolutionProof);

    /// <summary>Marks a session as needing a follow-up resolution check.</summary>
    Task<bool> MarkNeedsRecheckAsync(Guid sessionId);

    /// <summary>Returns aggregate counts for the dashboard summary card.</summary>
    Task<object> GetDashboardSummaryAsync();

    /// <summary>Returns recurring pattern data grouped by diagnosed category.</summary>
    Task<object> GetRecurringPatternsAsync();

    /// <summary>Returns warning sign data across all sessions.</summary>
    Task<object> GetWarningSignsAsync();

    /// <summary>Returns saved system profiles.</summary>
    Task<object> GetProfilesAsync();

    /// <summary>Saves a system hardware profile manually.</summary>
    Task<object> SaveProfileAsync(RigMD.Application.Models.SaveProfilePayload payload);

    /// <summary>Returns all sessions mapped to RecurringSessionDto for pattern analysis.</summary>
    Task<IReadOnlyList<RecurringSessionDto>> GetRecurringSessionsAsync();

    /// <summary>Returns all observed warning sign text values across sessions.</summary>
    Task<IEnumerable<string>> GetObservedWarningTextsAsync();

    /// <summary>Returns remediation run history for a given session ID.</summary>
    Task<IReadOnlyList<RemediationRunDto>> GetRemediationHistoryAsync(Guid sessionId);
}
