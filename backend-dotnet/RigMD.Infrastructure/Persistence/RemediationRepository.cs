using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Domain.Entities;

namespace RigMD.Infrastructure.Persistence;

/// <summary>
/// EF Core + SQLite implementation of IRemediationRepository.
/// </summary>
public class RemediationRepository : IRemediationRepository
{
    private readonly RigMdDbContext _db;

    public RemediationRepository(RigMdDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> SaveRunAsync(RemediationRun run)
    {
        _db.RemediationRuns.Add(run);
        await _db.SaveChangesAsync();
        return run.Id;
    }

    public async Task<IReadOnlyList<RemediationRun>> GetRunsBySessionAsync(Guid diagnosticOutputId)
    {
        return await _db.RemediationRuns
            .Include(r => r.ActionAttempts)
                .ThenInclude(a => a.Verification)
            .Include(r => r.RollbackEvents)
            .Include(r => r.PivotEvents)
            .Where(r => r.DiagnosticOutputId == diagnosticOutputId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }
}
