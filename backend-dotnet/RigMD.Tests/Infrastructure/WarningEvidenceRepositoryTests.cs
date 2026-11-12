using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public class WarningEvidenceRepositoryTests
{
    private const string ClientId = "alerts-client";
    private static readonly DateTimeOffset OldDate = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExistingAutomaticProofFeedsAlertsAndDashboardWithoutBackfillOrResolutionFiltering()
    {
        await using var db = CreateDb();
        const string original = """
            [{"metric_id":"memory_usage","label":"Memory","value":"91%","status":"high"},
             {"metric_id":"memory_usage","label":"Memory duplicate","value":"91%","status":"high"},
             {"metric_id":"cpu_usage","label":"SMART caution","value":"10%","status":"normal"},
             {"metric_id":"storage_health","label":"Drive health","value":"Unavailable","status":"unknown"}]
            """;
        var session = SavedSession(db, 1, proof: original);
        session.Answers.Add(Answer(session, "resolution_status", "resolved"));
        session.Answers.Add(Answer(session, "resolution_proof", "[{\"metric_id\":\"memory_usage\",\"status\":\"normal\"}]"));
        await db.SaveChangesAsync();
        var before = await AnswerSnapshot(db);
        db.ChangeTracker.Clear();

        var repo = Repository(db);
        var result = new WarningSignService().BuildReference(await repo.GetObservedWarningTextsAsync(), observedOnly: true);
        var warning = Assert.Single(result.warning_signs);
        Assert.Equal("WS-011", warning.id);
        Assert.Equal(1, warning.observed_count);
        var dashboard = JsonSerializer.SerializeToElement(await repo.GetDashboardSummaryAsync());
        Assert.Equal(1, dashboard.GetProperty("warning_signs_active_count").GetInt32());
        var recent = Assert.Single(dashboard.GetProperty("recent_warning_signs").EnumerateArray());
        Assert.Equal(warning.warning_sign, recent.GetProperty("warning_sign").GetString());
        Assert.Equal(warning.action, recent.GetProperty("recommended_action").GetString());
        var list = JsonSerializer.SerializeToElement(await repo.GetWarningSignsAsync());
        Assert.Equal(warning.warning_sign, Assert.Single(list.GetProperty("warning_signs").EnumerateArray()).GetProperty("warning_signs").GetString());
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(before, await AnswerSnapshot(db));
        Assert.False(await db.SessionAnswers.AnyAsync(a => a.QuestionKey == "warning_signs"));
    }

    [Fact]
    public async Task CurrentAutomaticSaveUsesOriginalStructuredProofForHistoricalAlerts()
    {
        await using var db = CreateDb();
        var repo = Repository(db);
        await repo.SaveAutomaticDiagnosisAsync(new HardwareProfileDto(), "component", ["storage"], null,
            Guid.Empty, "local", "Storage health behavior", "Escalate", "Available", "Drive warning", ClientId,
            proof: [new() { MetricId = "storage_health", Status = "high", Value = "Warning reported",
                Label = "Drive-health warnings", EvidenceKind = "reported_status" }]);
        db.ChangeTracker.Clear();

        var result = new WarningSignService().BuildReference(await repo.GetObservedWarningTextsAsync(), observedOnly: true);
        Assert.Equal("WS-006", Assert.Single(result.warning_signs).id);
        var summary = JsonSerializer.SerializeToElement(await repo.GetDashboardSummaryAsync());
        Assert.Equal(1, summary.GetProperty("warning_signs_active_count").GetInt32());
        Assert.False(await db.SessionAnswers.AnyAsync(a => a.QuestionKey == "warning_signs"));
    }

    [Theory]
    [InlineData("memory", "memory_usage", "high", "WS-011")]
    [InlineData("cpu", "cpu_usage", "high", "WS-012")]
    [InlineData("storage", "storage_usage", "high", "WS-013")]
    [InlineData("network", "network_loss", "high", "WS-014")]
    [InlineData("battery", "battery_charge", "elevated", "WS-017")]
    public async Task ActualCurrentProducerWarningStatusesSurvivePersistenceAndFeedAlerts(
        string component, string metric, string expectedStatus, string expectedId)
    {
        await using var db = CreateDb();
        var hardware = RigMD.Tests.Application.ScannerEvidenceAuditTests.KnownReadings();
        switch (component)
        {
            case "memory": hardware.Ram.UsagePercent = 91; break;
            case "cpu": hardware.Cpu.UsagePercent = 95; break;
            case "storage": hardware.AllDisks[0].UsagePercent = 95; hardware.StorageDrives[0].UsagePercent = 95; break;
            case "network": hardware.Network.PacketLossPercent = 25; break;
            case "battery": hardware.Battery!.EstimatedChargeRemaining = 10; hardware.Battery.IsCharging = false; break;
        }
        var result = new AutomaticDiagnosisService().Diagnose(new()
        {
            Mode = "component", ComponentIds = [component], Hardware = hardware, CapturedAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(expectedStatus, result.Proof.Single(p => p.MetricId == metric).Status);
        var repo = Repository(db);
        await repo.SaveAutomaticDiagnosisAsync(hardware, "component", [component], null, Guid.Empty, "local",
            result.DiagnosedCategory, result.ActionCategory, result.ConfidenceLabel, result.Explanation, ClientId,
            result.PrimaryResult, result.IncidentalWarning, result.ComponentStatus.ToString(), result.Proof, result.RecommendedNextStep);
        db.ChangeTracker.Clear();

        var references = new WarningSignService().BuildReference(await repo.GetObservedWarningTextsAsync(), observedOnly: true);
        Assert.Equal(expectedId, Assert.Single(references.warning_signs).id);
        var dashboard = JsonSerializer.SerializeToElement(await repo.GetDashboardSummaryAsync());
        Assert.Equal(1, dashboard.GetProperty("warning_signs_active_count").GetInt32());
        Assert.Equal(Assert.Single(references.warning_signs).warning_sign,
            Assert.Single(dashboard.GetProperty("recent_warning_signs").EnumerateArray()).GetProperty("warning_sign").GetString());
        if (component == "storage") Assert.DoesNotContain(references.warning_signs, w => w.id == "WS-006");
    }

    [Fact]
    public async Task RepeatedSessionsAndMixedEvidenceCountOncePerReferenceAndAlignDashboardRows()
    {
        await using var db = CreateDb();
        const string legacy = "SMART caution; smart warning; WS-006; BSOD; blue screen";
        SavedSession(db, 1, legacy: legacy);
        SavedSession(db, 2, legacy: legacy);
        var mixed = SavedSession(db, 3, legacy: "WS-006", proof: """
            [{"metric_id":"storage_health","status":"high"},
             {"metric_id":"storage_health","status":"critical"}]
            """);
        var resource = SavedSession(db, 4, proof: """
            [{"metric_id":"memory_usage","status":"high"},
             {"metric_id":"cpu_usage","status":"warning"},
             {"metric_id":"storage_usage_C:","status":"high"},
             {"metric_id":"storage_usage_D:","status":"high"},
             {"metric_id":"network_dns","status":"high"},
             {"metric_id":"network_loss","status":"critical"}]
            """);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = Repository(db);
        var texts = (await repo.GetObservedWarningTextsAsync()).ToArray();
        Assert.Equal(4, texts.Length);
        var reference = new WarningSignService().BuildReference(texts, observedOnly: true);
        Assert.Equal(3, reference.warning_signs.Single(w => w.id == "WS-006").observed_count);
        Assert.Equal(2, reference.warning_signs.Single(w => w.id == "WS-002").observed_count);
        Assert.Equal(6, reference.summary.observed_warning_signs);
        Assert.Equal(9, reference.summary.total_observed_occurrences);
        var dashboard = JsonSerializer.SerializeToElement(await repo.GetDashboardSummaryAsync());
        Assert.Equal(reference.summary.total_observed_occurrences, dashboard.GetProperty("warning_signs_active_count").GetInt32());
        var recent = dashboard.GetProperty("recent_warning_signs").EnumerateArray().ToArray();
        Assert.Equal(5, recent.Length);
        Assert.Equal(5, recent.Select(w => w.GetProperty("id").GetString()).Distinct().Count());
        Assert.All(recent.Take(4), w => Assert.StartsWith(resource.Id.ToString(), w.GetProperty("id").GetString()));
        Assert.StartsWith(mixed.Id.ToString(), recent[4].GetProperty("id").GetString());
        foreach (var row in recent)
        {
            var expected = reference.warning_signs.Single(w => w.warning_sign == row.GetProperty("warning_sign").GetString());
            Assert.Equal(expected.action, row.GetProperty("recommended_action").GetString());
            Assert.Equal(expected.threshold, row.GetProperty("threshold").GetString());
        }
        Assert.Equal("Maintain", recent.Single(w => w.GetProperty("id").GetString()!.EndsWith(":WS-013")).GetProperty("recommended_action").GetString());
        Assert.DoesNotContain(recent.Take(4), w => w.GetProperty("id").GetString()!.EndsWith(":WS-006"));
    }

    [Fact]
    public async Task NormalUnknownNegatedMalformedAndUnmappedEvidenceProducesNoDashboardAlerts()
    {
        await using var db = CreateDb();
        SavedSession(db, 1, legacy: "No blue screen; SMART OK; None; No SMART caution");
        SavedSession(db, 2, proof: """
            [{"metric_id":"storage_health","status":"normal","label":"SMART caution"},
             {"metric_id":"cpu_usage","status":"unknown","meaning":"blue screen"},
             {"metric_id":"memory_usage","status":"info","value":"Warning"},
             {"metric_id":"network_loss","status":"unavailable"},
             {"metric_id":"unknown_metric","status":"critical","label":"SMART caution"},
             {"metric_id":"storage_usage","status":"high","evidence_kind":"not_assessed"}]
            """);
        SavedSession(db, 3, proof: "invalid proof");
        var context = SavedSession(db, 4);
        context.Answers.Add(Answer(context, "primary_result", "SMART caution and BSOD"));
        context.Answers.Add(Answer(context, "incidental_warning", "Storage warning"));
        context.Answers.Add(Answer(context, "resolution_proof", "[{\"metric_id\":\"storage_health\",\"status\":\"high\"}]"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = Repository(db);
        Assert.Empty(await repo.GetObservedWarningTextsAsync());
        var result = new WarningSignService().BuildReference(await repo.GetObservedWarningTextsAsync());
        Assert.All(result.warning_signs, w => Assert.False(w.observed));
        var dashboard = JsonSerializer.SerializeToElement(await repo.GetDashboardSummaryAsync());
        Assert.Equal(0, dashboard.GetProperty("warning_signs_active_count").GetInt32());
        Assert.Empty(dashboard.GetProperty("recent_warning_signs").EnumerateArray());
        Assert.Empty(JsonSerializer.SerializeToElement(await repo.GetWarningSignsAsync()).GetProperty("warning_signs").EnumerateArray());
    }

    [Fact]
    public async Task WarningEvidenceRetainsClientIsolationForBothProfileAndLegacyAnswerIdentity()
    {
        await using var db = CreateDb();
        SavedSession(db, 1, legacy: "BSOD");
        var other = SavedSession(db, 2, legacy: "SMART caution");
        other.Profile.ClientId = "another-client";
        var compatibility = SavedSession(db, 3, legacy: "loud fan");
        compatibility.Profile.ClientId = "old-profile-client";
        compatibility.Answers.Add(Answer(compatibility, "client_id", ClientId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = Repository(db);
        var result = new WarningSignService().BuildReference(await repo.GetObservedWarningTextsAsync(), observedOnly: true);
        Assert.Equal(new[] { "WS-002", "WS-007" }, result.warning_signs.Select(w => w.id));
        var dashboard = JsonSerializer.SerializeToElement(await repo.GetDashboardSummaryAsync());
        Assert.Equal(2, dashboard.GetProperty("warning_signs_active_count").GetInt32());
        Assert.DoesNotContain(dashboard.GetProperty("recent_warning_signs").EnumerateArray(),
            w => w.GetProperty("id").GetString()!.StartsWith(other.Id.ToString()));
    }

    [Fact]
    public async Task WarningLinksPageOriginalMatchingChecksWithClientIsolationAndNoMutation()
    {
        await using var db = CreateDb();
        for (var index = 1; index <= 25; index++)
        {
            var saved = SavedSession(db, index, proof: "[{\"metric_id\":\"memory_usage\",\"status\":\"high\"},{\"metric_id\":\"memory_usage\",\"status\":\"high\"}]");
            saved.Answers.Add(Answer(saved, "resolution_status", "resolved"));
        }
        SavedSession(db, 26, proof: "[{\"metric_id\":\"memory_usage\",\"status\":\"normal\"}]");
        var foreign = SavedSession(db, 27, legacy: "WS-011"); foreign.Profile.ClientId = "another-client";
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await AnswerSnapshot(db);
        var repo = Repository(db);
        var first = await repo.GetWarningChecksAsync("ws-011", 0, 20);
        var second = await repo.GetWarningChecksAsync("WS-011", 20, 20);
        Assert.Equal("WS-011", first.reference_id); Assert.Equal(25, first.total_checks);
        Assert.Equal(20, first.checks.Count); Assert.Equal(5, second.checks.Count);
        Assert.Equal(25, first.checks.Concat(second.checks).Select(c => c.session_id).Distinct().Count());
        Assert.Equal(OldDate.AddMinutes(25), first.checks[0].created_at);
        Assert.All(first.checks, c => Assert.Equal("resolved", c.resolution_status));
        Assert.Empty((await repo.GetWarningChecksAsync("WS-006", 0, 20)).checks);
        Assert.Empty((await repo.GetWarningChecksAsync("WS-011", 100, 20)).checks);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Equal(before, await AnswerSnapshot(db));
    }

    [Theory]
    [InlineData(-1, 20)] [InlineData(0, 0)] [InlineData(0, 51)]
    public async Task WarningLinksRejectInvalidPaging(int offset, int limit)
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repository(db).GetWarningChecksAsync("WS-011", offset, limit));
    }

    [Fact]
    public async Task WarningLinksRejectUnknownReference()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<ArgumentException>(() => Repository(db).GetWarningChecksAsync("unknown", 0, 20));
    }

    [Fact]
    public async Task WarningLinksEndpointRequiresAuthenticationAndReturnsRealMatchedIds()
    {
        await using var db = CreateDb();
        var saved = SavedSession(db, 1, proof: "[{\"metric_id\":\"memory_usage\",\"status\":\"high\"}]");
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(RigMD.Api.Controllers.WarningSignsController).Assembly);
        builder.Services.AddSingleton(new RigMD.Api.Services.LocalApiSession(new string('A', 64)));
        builder.Services.AddSingleton<WarningSignService>();
        builder.Services.AddScoped<RigMD.Application.Contracts.Persistence.IDiagnosticSessionRepository>(_ => Repository(db));
        await using var app = builder.Build(); app.UseMiddleware<RigMD.Api.Middleware.LocalApiAuthenticationMiddleware>(); app.MapControllers();
        await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)), Timeout = TimeSpan.FromSeconds(5) };
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/warning-signs/WS-011/checks")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", new string('A', 64));
            client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("/api/warning-signs/WS-011/checks")).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            using var response = await client.GetAsync("/api/warning-signs/WS-011/checks"); Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(saved.Id.ToString(), Assert.Single(result.RootElement.GetProperty("checks").EnumerateArray()).GetProperty("session_id").GetString());
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/api/warning-signs/WS-999/checks")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.GetAsync("/api/warning-signs/WS-011/checks?offset=-1")).StatusCode);
            await db.Database.CloseConnectionAsync();
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/warning-signs/WS-011/checks")).StatusCode);
        }
        finally { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await app.StopAsync(budget.Token); }
    }

    private static RigMdDbContext CreateDb()
    {
        var db = new RigMdDbContext(new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite("DataSource=:memory:").Options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static DiagnosticSession SavedSession(RigMdDbContext db, int order, string? legacy = null, string? proof = null)
    {
        var profile = new SystemProfile { ClientId = ClientId };
        var session = new DiagnosticSession { Profile = profile, SystemProfileId = profile.Id, CreatedAt = OldDate.AddMinutes(order) };
        if (legacy != null) session.Answers.Add(Answer(session, "warning_signs", legacy));
        if (proof != null) session.Answers.Add(Answer(session, "proof", proof));
        // A concerning diagnosis/action alone must not generate warning evidence.
        session.Output = new DiagnosticOutput { Session = session, DiagnosticSessionId = session.Id,
            DiagnosedCategory = "Storage health behavior", ActionCategory = "Escalate" };
        db.DiagnosticSessions.Add(session);
        return session;
    }

    private static SessionAnswer Answer(DiagnosticSession session, string key, string value) => new()
    { Session = session, DiagnosticSessionId = session.Id, QuestionKey = key, AnswerValue = value };

    private static async Task<string> AnswerSnapshot(RigMdDbContext db) => JsonSerializer.Serialize(
        await db.SessionAnswers.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Id, a.QuestionKey, a.AnswerValue }).ToArrayAsync());

    private static DiagnosticSessionRepository Repository(RigMdDbContext db) => new(db, new Client());
    private sealed class Client : ICurrentClientProvider
    {
        public string GetCurrentClientId() => ClientId;
    }
}
