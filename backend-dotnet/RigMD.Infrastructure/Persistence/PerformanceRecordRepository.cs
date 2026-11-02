using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Infrastructure.Persistence;

public sealed class PerformanceRecordRepository(RigMdDbContext db) : IPerformanceRecordRepository
{
    public async Task AddAsync(PerformanceRecord record, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record.ClientId)) throw new ArgumentException("A local identity is required.");
        // The single background writer serializes inserts; retries cannot count the same event twice.
        if (await db.PerformanceRecords.AnyAsync(r => r.Id == record.Id, cancellationToken)) return;
        db.PerformanceRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        var overflow = await db.PerformanceRecords.Where(r => r.ClientId == record.ClientId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip(PerformanceRules.RetainedRecords).ToListAsync(cancellationToken);
        if (overflow.Count == 0) return;
        db.PerformanceRecords.RemoveRange(overflow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PerformanceRecord>> GetAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new UnauthorizedAccessException();
        return await db.PerformanceRecords.AsNoTracking().Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(PerformanceRules.RetainedRecords).ToListAsync(cancellationToken);
    }
}
