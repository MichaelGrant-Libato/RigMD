using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Models;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public class FollowUpHistoryTests
{
    private sealed class Client(string id) : ICurrentClientProvider
    { public string GetCurrentClientId() => id; }
    private static DiagnosticSessionRepository Repository(RigMdDbContext db, string client = "owner") => new(db, new Client(client));
    private static RigMdDbContext MemoryDb()
    {
        var db = new RigMdDbContext(new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite("Data Source=:memory:").Options);
        db.Database.OpenConnection(); db.Database.EnsureCreated(); return db;
    }
    private static Task<Guid> Seed(DiagnosticSessionRepository repo) => repo.SaveAutomaticDiagnosisAsync(
        new HardwareProfileDto(), "component", ["memory"], null, Guid.Empty, "local",
        "High Memory Pressure", "Maintain", "Available", "Original explanation", "owner",
        proof: [new AutomaticDiagnosisProof { MetricId = "memory_usage", Label = "Memory use", Value = "90%", Status = "high" }]);
    private static object[] Proof(string value, string status = "high") =>
        [new { metric_id = "memory_usage", label = "Memory use", value, status, evidence_kind = "measurement" }];

    [Fact]
    public async Task EveryRecheckSurvivesReload_IncludingRepeatedTimestampsAndUnknownReadings()
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        const string at = "2026-11-11T00:00:00.0000000+00:00";
        await repo.UpdateResolutionAsync(id, "still_active", at, "First", Proof("91%"));
        await repo.UpdateResolutionAsync(id, "resolved", at, "Second", Proof("40%", "normal"));
        await repo.UpdateResolutionAsync(id, "needs_recheck", at, "Third", Proof("Unavailable", "unknown"));
        db.ChangeTracker.Clear(); var loaded = (await repo.GetSessionAsync(id))!;
        Assert.Equal("complete", loaded.FollowUpHistoryStatus);
        Assert.Equal(3, loaded.FollowUpHistory.Count);
        Assert.Equal(3, loaded.FollowUpHistory.Select(row => row.ObservationId).Distinct().Count());
        Assert.Equal(new[] { "First", "Second", "Third" }, loaded.FollowUpHistory.Select(row => row.Summary).Order().ToArray());
        Assert.Equal("90%", Assert.IsType<JsonElement>(Assert.Single(loaded.Proof)).GetProperty("value").GetString());
        Assert.Equal("Original explanation", loaded.AiExplanation);
        Assert.Contains(loaded.FollowUpHistory, row => row.Status == "needs_recheck" &&
            Assert.IsType<JsonElement>(Assert.Single(row.Proof)).GetProperty("value").GetString() == "Unavailable");
        var listed = Assert.Single(await repo.GetSessionsAsync());
        Assert.Empty(listed.FollowUpHistory); Assert.Equal("not_loaded", listed.FollowUpHistoryStatus);
    }

    [Fact]
    public async Task ExistingLatestFollowUpIsPreservedOnce_BeforeAnActionClearsItsLatestFields()
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        await repo.UpdateResolutionAsync(id, "still_active", "2026-11-10T12:00:00Z", "Old saved follow-up", Proof("91%"));
        db.SessionAnswers.RemoveRange(db.SessionAnswers.Where(row => row.QuestionKey == "resolution_observation_v1"));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await db.SessionAnswers.CountAsync();
        Assert.Equal("legacy_latest_only", (await repo.GetSessionAsync(id))!.FollowUpHistoryStatus);
        Assert.Equal(before, await db.SessionAnswers.CountAsync());
        await repo.MarkNeedsRecheckAsync(id); db.ChangeTracker.Clear();
        var afterAction = (await repo.GetSessionAsync(id))!;
        Assert.Empty(afterAction.ResolutionProof);
        Assert.Equal("Old saved follow-up", Assert.Single(afterAction.FollowUpHistory).Summary);
        Assert.Equal("legacy_latest", afterAction.FollowUpHistory[0].Source);
        await repo.UpdateResolutionAsync(id, "resolved", "2026-11-11T12:00:00Z", "New recheck", Proof("40%", "normal"));
        await repo.MarkNeedsRecheckAsync(id); db.ChangeTracker.Clear();
        var loaded = (await repo.GetSessionAsync(id))!;
        Assert.Equal(2, loaded.FollowUpHistory.Count);
        Assert.Single(loaded.FollowUpHistory, row => row.Source == "legacy_latest");
        Assert.Equal("legacy_latest_only", loaded.FollowUpHistoryStatus);
    }

    [Fact]
    public async Task OlderFinishingRecheckIsRetained_WithoutReplacingTheNewerLatestReading()
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        await repo.UpdateResolutionAsync(id, "resolved", "2026-11-11T12:00:00Z", "Newer", Proof("40%", "normal"));
        await repo.UpdateResolutionAsync(id, "still_active", "2026-11-11T11:00:00Z", "Older", Proof("91%"));
        db.ChangeTracker.Clear(); var loaded = (await repo.GetSessionAsync(id))!;
        Assert.Equal("Newer", loaded.ResolutionSummary);
        Assert.Equal(new[] { "Newer", "Older" }, loaded.FollowUpHistory.Select(row => row.Summary).ToArray());
    }

    [Fact]
    public async Task OtherClientsCannotReadOrAppendHistory_AndDeletingAnOwnedCheckRemovesItsHistory()
    {
        await using var db = MemoryDb(); var owner = Repository(db); var id = await Seed(owner);
        await owner.UpdateResolutionAsync(id, "still_active", "2026-11-11T12:00:00Z", "Owner", Proof("91%"));
        var other = Repository(db, "other");
        Assert.Null(await other.GetSessionAsync(id));
        Assert.False(await other.UpdateResolutionAsync(id, "resolved", "2026-11-11T13:00:00Z", "Unauthorized", []));
        Assert.Single((await owner.GetSessionAsync(id))!.FollowUpHistory);
        Assert.True(await owner.DeleteSessionAsync(id));
        Assert.False(await db.SessionAnswers.AnyAsync(row => row.DiagnosticSessionId == id));
    }

    [Fact]
    public async Task CorruptHistoryEntryIsReported_WithoutHidingValidEntriesOrChangingStoredEvidence()
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        await repo.UpdateResolutionAsync(id, "resolved", "2026-11-11T12:00:00Z", "Valid", Proof("40%", "normal"));
        db.SessionAnswers.Add(new SessionAnswer { DiagnosticSessionId = id, QuestionKey = "resolution_observation_v1", AnswerValue = "{broken" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = (await repo.GetSessionAsync(id))!;
        Assert.Equal("partially_unavailable", loaded.FollowUpHistoryStatus);
        Assert.Equal("Valid", Assert.Single(loaded.FollowUpHistory).Summary);
        Assert.Equal(2, await db.SessionAnswers.CountAsync(row => row.QuestionKey == "resolution_observation_v1"));
    }

    [Fact]
    public async Task StructuredNullHistoryFieldsRemainReadableAndAreMarkedIncomplete()
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        db.SessionAnswers.Add(new SessionAnswer
        {
            DiagnosticSessionId = id, QuestionKey = "resolution_observation_v1",
            AnswerValue = JsonSerializer.Serialize(new { observation_id = Guid.NewGuid().ToString(), checked_at = "2026-11-11T12:00:00Z", source = (string?)null, status = (string?)null, summary = (string?)null, proof = (object[]?)null })
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = (await repo.GetSessionAsync(id))!;
        Assert.Equal("partially_unavailable", loaded.FollowUpHistoryStatus);
        var row = Assert.Single(loaded.FollowUpHistory);
        Assert.Equal("unknown", row.Source); Assert.Equal("not_recorded", row.Status); Assert.Empty(row.Proof);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public async Task InvalidLegacySnapshotShapeDoesNotBreakSavedCheckDetails(string snapshot)
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        db.SessionAnswers.Add(new SessionAnswer
        {
            DiagnosticSessionId = id, QuestionKey = "pre_action_readings", AnswerValue = snapshot
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = (await repo.GetSessionAsync(id))!;
        Assert.Equal("not_recorded", loaded.FollowUpHistoryStatus);
        Assert.Empty(loaded.FollowUpHistory);
    }

    [Fact]
    public async Task FailedHistoryWriteCannotCommitLatestOutcomeWithoutItsObservation()
    {
        await using var db = MemoryDb(); var repo = Repository(db); var id = await Seed(repo);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_history BEFORE INSERT ON SessionAnswers WHEN NEW.QuestionKey = 'resolution_observation_v1' BEGIN SELECT RAISE(ABORT, 'test write failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => repo.UpdateResolutionAsync(id, "resolved", "2026-11-11T12:00:00Z", "Must not commit", Proof("40%", "normal")));
        db.ChangeTracker.Clear(); var loaded = (await repo.GetSessionAsync(id))!;
        Assert.NotEqual("Must not commit", loaded.ResolutionSummary);
        Assert.Empty(loaded.FollowUpHistory);
    }

    [Fact]
    public async Task IndependentWritersAndFreshContextsRetainBothObservationsInAnExistingSqliteDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), "RigMD-history-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var options = new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=10").Options;
        try
        {
            Guid id;
            await using (var setup = new RigMdDbContext(options))
            { await setup.Database.EnsureCreatedAsync(); id = await Seed(Repository(setup)); }
            await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
            {
                await using var writer = new RigMdDbContext(options);
                await Repository(writer).UpdateResolutionAsync(id, "still_active", $"2026-11-11T12:0{index}:00Z", $"Writer {index}", Proof($"{90 + index}%"));
            })));
            await using var reopened = new RigMdDbContext(options);
            var loaded = (await Repository(reopened).GetSessionAsync(id))!;
            Assert.Equal(2, loaded.FollowUpHistory.Count);
            Assert.Equal("Writer 1", loaded.ResolutionSummary);
            Assert.Single(await reopened.SessionAnswers.Where(row => row.DiagnosticSessionId == id && row.QuestionKey == "resolution_status").ToListAsync());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
