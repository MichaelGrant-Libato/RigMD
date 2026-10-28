using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Models;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public sealed class SessionFeedbackRepositoryTests
{
    private sealed class Client(string id) : ICurrentClientProvider { public string GetCurrentClientId() => id; }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 28, 4, 0, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static RigMdDbContext Context(SqliteConnection connection) => new(new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite(connection).Options);
    private static DiagnosticSessionRepository Sessions(RigMdDbContext db) => new(db, new Client("owner"));
    private static Task<Guid> Seed(RigMdDbContext db) => Sessions(db).SaveAutomaticDiagnosisAsync(
        new HardwareProfileDto(), "component", ["memory"], null, Guid.Empty, "local", "High Memory Pressure", "Troubleshoot", "Partial", "Recorded warning.",
        "owner", proof: [new() { MetricId = "memory_usage", Label = "Memory", Value = "90%", Status = "high", EvidenceKind = "measurement" }]);

    [Theory]
    [InlineData("still_happening")]
    [InlineData("not_happening")]
    [InlineData("not_sure")]
    public async Task FeedbackSurvivesNewContextWithoutChangingAnyDiagnosticOrResolutionFields(string outcome)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        var clock = new Clock(); Guid id; string original;
        await using (var db = Context(connection))
        {
            await db.Database.EnsureCreatedAsync(); id = await Seed(db);
            await Sessions(db).UpdateResolutionAsync(id, "still_active", clock.Now.ToString("O"), "Measured warning remains.",
                [new { metric_id = "memory_usage", label = "Memory", value = "91%", status = "high", evidence_kind = "measurement" }]);
            original = JsonSerializer.Serialize(await Sessions(db).GetSessionAsync(id));
            var repository = new SessionFeedbackRepository(db, new Client("owner"), clock);
            Assert.Empty((await repository.GetAsync(id))!);
            var saved = await repository.SaveAsync(id, new() { Outcome = outcome, Note = "  My observation.  " });
            Assert.Equal(outcome, saved!.Outcome); Assert.Equal("My observation.", saved.Note);
            Assert.Equal(TimeSpan.Zero, saved.CreatedAt.Offset); Assert.Equal(clock.Now, saved.CreatedAt);
        }
        await using (var reloaded = Context(connection))
        {
            var repository = new SessionFeedbackRepository(reloaded, new Client("owner"), clock);
            Assert.Equal(outcome, Assert.Single((await repository.GetAsync(id))!).Outcome);
            Assert.Equal(original, JsonSerializer.Serialize(await Sessions(reloaded).GetSessionAsync(id)));
            Assert.Single(await reloaded.SessionFeedback.ToListAsync());
        }
    }

    [Theory]
    [InlineData("other-client")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrOtherClientCannotReadOrWriteFeedback(string client)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        await using var db = Context(connection); await db.Database.EnsureCreatedAsync(); var id = await Seed(db);
        var repository = new SessionFeedbackRepository(db, new Client(client), new Clock());
        Assert.Null(await repository.GetAsync(id));
        Assert.Null(await repository.SaveAsync(id, new() { Outcome = "not_happening" }));
        Assert.Empty(await db.SessionFeedback.ToListAsync());
        Assert.Null(await new SessionFeedbackRepository(db, new Client("owner"), new Clock()).GetAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fixed")]
    [InlineData("STILL_HAPPENING")]
    public async Task InvalidAnswersAreRejectedBeforeSaving(string outcome)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        await using var db = Context(connection); await db.Database.EnsureCreatedAsync(); var id = await Seed(db);
        var repository = new SessionFeedbackRepository(db, new Client("owner"), new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(id, new() { Outcome = outcome }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(id, new() { Outcome = "not_sure", Note = new string('a', 501) }));
        Assert.Empty(await db.SessionFeedback.ToListAsync());
    }

    [Fact]
    public async Task UpdatesAppendHistoryAndStayWithTheirOwnCheck()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        await using var db = Context(connection); await db.Database.EnsureCreatedAsync(); var first = await Seed(db); var second = await Seed(db);
        var clock = new Clock(); var repository = new SessionFeedbackRepository(db, new Client("owner"), clock);
        for (var index = 0; index < 22; index++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            await repository.SaveAsync(first, new() { Outcome = index == 21 ? "not_happening" : "still_happening", Note = $"Observation {index}" });
        }
        var history = (await repository.GetAsync(first))!;
        Assert.Equal(20, history.Count); Assert.Equal("Observation 21", history[0].Note); Assert.Equal("Observation 2", history[^1].Note);
        Assert.Equal(22, await db.SessionFeedback.CountAsync()); Assert.Empty((await repository.GetAsync(second))!);
        db.ChangeTracker.Clear();
        Assert.True(await Sessions(db).DeleteSessionAsync(first));
        Assert.Empty(await db.SessionFeedback.ToListAsync()); Assert.NotNull(await Sessions(db).GetSessionAsync(second));
        Assert.Null(await repository.SaveAsync(first, new() { Outcome = "not_sure" }));
    }

    [Fact]
    public async Task ExistingDatabaseUpgradeIsIdempotentAndPreservesSavedEvidence()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        await using var db = Context(connection); await db.Database.EnsureCreatedAsync(); var id = await Seed(db);
        var original = JsonSerializer.Serialize(await Sessions(db).GetSessionAsync(id));
        await db.Database.ExecuteSqlRawAsync("DROP TABLE SessionFeedback;");
        var upgrade = new LocalDatabaseSchemaUpgradeService(db, NullLogger<LocalDatabaseSchemaUpgradeService>.Instance);
        await upgrade.ApplyAsync();
        var repository = new SessionFeedbackRepository(db, new Client("owner"), new Clock());
        await repository.SaveAsync(id, new() { Outcome = "not_sure", Note = new string('x', 500) });
        await upgrade.ApplyAsync(); db.ChangeTracker.Clear();
        Assert.Single((await repository.GetAsync(id))!);
        Assert.Equal(original, JsonSerializer.Serialize(await Sessions(db).GetSessionAsync(id)));
    }
}
