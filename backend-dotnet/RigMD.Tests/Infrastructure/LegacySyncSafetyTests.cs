using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public sealed class LegacySyncSafetyTests
{
    [Fact]
    public async Task ConfiguredLegacyConnectionCannotImportRemoteUsersOrDelayLocalStartup()
    {
        using var db = new RigMdDbContext(new DbContextOptionsBuilder<RigMdDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DATABASE_URL"] = "postgresql://fixture:unused@127.0.0.1:1/fixture" }).Build();
        var task = new DatabaseSyncService(db, config, NullLogger<DatabaseSyncService>.Instance).SyncFromPostgresAsync();
        Assert.True(task.IsCompletedSuccessfully); await task;
        Assert.Empty(db.SystemProfiles); Assert.Empty(db.DiagnosticSessions);
    }
}
