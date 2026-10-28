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

        // EnsureCreated does not add tables to an existing installation.
        // Feedback is local user input, kept apart from all diagnostic and resolution records.
        await _db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SessionFeedback" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SessionFeedback" PRIMARY KEY,
                "DiagnosticSessionId" TEXT NOT NULL,
                "Outcome" TEXT NOT NULL CHECK ("Outcome" IN ('still_happening', 'not_happening', 'not_sure')),
                "Note" TEXT NOT NULL CHECK (length("Note") <= 500),
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_SessionFeedback_DiagnosticSessions_DiagnosticSessionId"
                    FOREIGN KEY ("DiagnosticSessionId") REFERENCES "DiagnosticSessions" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_SessionFeedback_DiagnosticSessionId_CreatedAt"
                ON "SessionFeedback" ("DiagnosticSessionId", "CreatedAt");
            """);

        _logger.LogInformation(
            "Local SQLite schema compatibility check completed.");
    }
}