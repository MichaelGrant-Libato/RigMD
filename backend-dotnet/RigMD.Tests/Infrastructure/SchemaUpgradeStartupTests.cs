using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public class SchemaUpgradeStartupTests
{
    [Fact]
    public async Task OldUniqueIndexIsUpgradedOnce_AndLaterStartsDoNotRebuildTheSchema()
    {
        await using var db = new RigMdDbContext(new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_RemediationRuns_DiagnosticOutputId; CREATE UNIQUE INDEX IX_RemediationRuns_DiagnosticOutputId ON RemediationRuns(DiagnosticOutputId);");
        var service = new LocalDatabaseSchemaUpgradeService(db, NullLogger<LocalDatabaseSchemaUpgradeService>.Instance);
        await service.ApplyAsync();
        var indexes = await db.Database.SqlQueryRaw<int>("SELECT \"unique\" AS Value FROM pragma_index_list('RemediationRuns') WHERE name = 'IX_RemediationRuns_DiagnosticOutputId'").ToArrayAsync();
        Assert.Equal(0, Assert.Single(indexes));
        using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA schema_version;";
        var before = await command.ExecuteScalarAsync();
        await service.ApplyAsync();
        Assert.Equal(before, await command.ExecuteScalarAsync());
    }
}
