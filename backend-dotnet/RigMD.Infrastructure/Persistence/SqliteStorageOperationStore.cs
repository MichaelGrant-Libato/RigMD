using System.Globalization;
using Microsoft.Data.Sqlite;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Infrastructure.Persistence;

public sealed class SqliteStorageOperationStore(string connectionString) : IStorageOperationStore
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // Mutation intent must be committed before launching Windows, including on WAL databases.
            command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public void Initialize()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS StorageOperationJournal (
                Id TEXT PRIMARY KEY NOT NULL, Kind TEXT NOT NULL CHECK(Kind IN ('uninstall','cleanup')),
                SessionId TEXT NOT NULL, MetricId TEXT NOT NULL, Payload TEXT NOT NULL,
                BootId TEXT NULL, MayBeRunning INTEGER NOT NULL CHECK(MayBeRunning IN (0,1)),
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_StorageOperationJournal_Session
                ON StorageOperationJournal(SessionId, CreatedAt);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_StorageOperationJournal_Active
                ON StorageOperationJournal(MayBeRunning) WHERE MayBeRunning=1;
            """;
        command.ExecuteNonQuery();
    }

    public StorageOperationEntry[] ReadAll()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Kind,SessionId,MetricId,Payload,BootId,MayBeRunning,CreatedAt,UpdatedAt FROM StorageOperationJournal ORDER BY CreatedAt DESC;";
        using var reader = command.ExecuteReader(); var records = new List<StorageOperationEntry>();
        while (reader.Read()) records.Add(new(reader.GetString(0), reader.GetString(1), Guid.Parse(reader.GetString(2)),
            reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture)));
        return records.ToArray();
    }

    public void Save(StorageOperationEntry entry)
    {
        if (!Guid.TryParseExact(entry.Id, "N", out _) || entry.Kind is not ("uninstall" or "cleanup") ||
            entry.SessionId == Guid.Empty || entry.Payload.Length > 131072 || entry.MetricId.Length > 96)
            throw new InvalidOperationException("Invalid operation receipt.");
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO StorageOperationJournal(Id,Kind,SessionId,MetricId,Payload,BootId,MayBeRunning,CreatedAt,UpdatedAt)
            VALUES($id,$kind,$session,$metric,$payload,$boot,$running,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET Payload=$payload,MayBeRunning=$running,UpdatedAt=$updated
            WHERE Kind=$kind AND SessionId=$session AND MetricId=$metric AND CreatedAt=$created AND BootId IS $boot;
            """;
        command.Parameters.AddWithValue("$id", entry.Id); command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$session", entry.SessionId.ToString()); command.Parameters.AddWithValue("$metric", entry.MetricId);
        command.Parameters.AddWithValue("$payload", entry.Payload); command.Parameters.AddWithValue("$boot", (object?)entry.BootId ?? DBNull.Value);
        command.Parameters.AddWithValue("$running", entry.MayBeRunning); command.Parameters.AddWithValue("$created", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", entry.UpdatedAt.ToString("O"));
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Operation receipt identity changed.");
        // Interrupted receipts are never evicted. Retain 200 completed receipts across restarts.
        command.CommandText = "DELETE FROM StorageOperationJournal WHERE MayBeRunning=0 AND Id IN (SELECT Id FROM StorageOperationJournal WHERE MayBeRunning=0 ORDER BY CreatedAt DESC,Id LIMIT -1 OFFSET 200);";
        command.Parameters.Clear(); command.ExecuteNonQuery(); transaction.Commit();
    }
}
