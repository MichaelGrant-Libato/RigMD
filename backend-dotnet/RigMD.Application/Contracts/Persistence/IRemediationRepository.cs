using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Persistence;

/// <summary>
/// Repository abstraction for remediation run persistence.
/// </summary>
public interface IRemediationRepository
{
    /// <summary>Persists a new remediation run and returns its generated ID.</summary>
    Task<Guid> SaveRunAsync(RemediationRun run);

    /// <summary>Returns all remediation runs linked to a given diagnostic session ID.</summary>
    Task<IReadOnlyList<RemediationRun>> GetRunsBySessionAsync(Guid diagnosticOutputId);

    /// <summary>Returns distinct action codes that have historically failed verification.</summary>
    Task<IReadOnlyList<string>> GetFailedActionCodesAsync();
}
