using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RigMD.Infrastructure.Persistence;

public class LocalDatabaseSchemaUpgradeService
{
    private readonly RigMdDbContext _db;
    private readonly ILogger<LocalDatabaseSchemaUpgradeService> _logger;

    public LocalDatabaseSchemaUpgradeService(
        RigMdDbContext db,
        ILogger<LocalDatabaseSchemaUpgradeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ApplyAsync()
    {
        if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(
            """
            DROP INDEX IF EXISTS "IX_RemediationRuns_DiagnosticOutputId";
            """);

        await _db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_RemediationRuns_DiagnosticOutputId"
            ON "RemediationRuns" ("DiagnosticOutputId");
            """);

        _logger.LogInformation(
            "Local SQLite remediation-history schema compatibility check completed.");
    }
}