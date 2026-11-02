using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Models;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public sealed class PerformanceRecordRepositoryTests
{
    [Fact]
    public async Task UpgradeIsIdempotentOnExistingDatabaseAndDoesNotChangeDiagnosticHistory()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite(connection).Options;
        await using (var db = new RigMdDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE PerformanceRecords;");
            var upgrade = new LocalDatabaseSchemaUpgradeService(db, NullLogger<LocalDatabaseSchemaUpgradeService>.Instance);
            await upgrade.ApplyAsync(); await upgrade.ApplyAsync();
            var repository = new PerformanceRecordRepository(db);
            var row = new PerformanceRecord { ClientId = "one", Operation = "history.ui", Outcome = "completed", DurationMs = 3000, RecordCount = 50 };
            await repository.AddAsync(row); await repository.AddAsync(row);
            Assert.Single(await repository.GetAsync("one")); Assert.Empty(await repository.GetAsync("other"));
            Assert.Empty(await db.DiagnosticSessions.ToListAsync()); Assert.Empty(await db.SessionFeedback.ToListAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetAsync(""));
        }
        await using (var reopened = new RigMdDbContext(options))
        { var row = Assert.Single(await new PerformanceRecordRepository(reopened).GetAsync("one")); Assert.Equal(50, row.RecordCount); }
    }

    [Fact]
    public async Task RetentionKeepsNewestRecordsAndDoesNotDeleteAnotherClientsEvidence()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        await using var db = new RigMdDbContext(new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var now = DateTimeOffset.UtcNow;
        db.PerformanceRecords.AddRange(Enumerable.Range(0, PerformanceRules.RetainedRecords).Select(i => new PerformanceRecord { ClientId = "one", CreatedAt = now.AddSeconds(-i - 1) }));
        db.PerformanceRecords.Add(new PerformanceRecord { ClientId = "other", CreatedAt = now.AddYears(-1) });
        await db.SaveChangesAsync(); var repository = new PerformanceRecordRepository(db);
        var newest = new PerformanceRecord { ClientId = "one", CreatedAt = now }; await repository.AddAsync(newest);
        var rows = await repository.GetAsync("one"); Assert.Equal(PerformanceRules.RetainedRecords, rows.Count);
        Assert.Equal(newest.Id, rows[0].Id); Assert.Single(await repository.GetAsync("other"));
        Assert.DoesNotContain(rows, r => r.CreatedAt == now.AddSeconds(-PerformanceRules.RetainedRecords));
    }
}
