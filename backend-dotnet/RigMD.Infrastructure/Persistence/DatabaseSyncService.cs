using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RigMD.Infrastructure.Persistence;

/// <summary>
/// Compatibility entry point for the retired raw PostgreSQL import. Cloud AI/account
/// access uses its authenticated proxy; diagnostic history stays in local SQLite.
/// An unscoped connection string must never import other users' profiles or sessions.
/// </summary>
public sealed class DatabaseSyncService
{
    private readonly ILogger<DatabaseSyncService> _logger;
    public DatabaseSyncService(RigMdDbContext db, IConfiguration config, ILogger<DatabaseSyncService> logger)
    { ArgumentNullException.ThrowIfNull(db); ArgumentNullException.ThrowIfNull(config); _logger = logger; }
    public Task SyncFromPostgresAsync()
    {
        _logger.LogInformation("Legacy direct database import is disabled. Diagnostic history remains local; cloud AI uses its authenticated proxy.");
        return Task.CompletedTask;
    }
}
