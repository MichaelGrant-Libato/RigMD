using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Rules;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public class DiagnosticSessionRepositoryTests
{
    private const string TestClientId =
        "test-client-123";

    private RigMdDbContext GetMemoryContext()
    {
        var options =
            new DbContextOptionsBuilder<RigMdDbContext>()
                .UseSqlite("DataSource=:memory:")
                .Options;

        var context =
            new RigMdDbContext(options);

        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        return context;
    }

    private DiagnosticSessionRepository CreateRepository(
        RigMdDbContext db,
        string clientId = TestClientId)
    {
        var clientProvider =
            new TestCurrentClientProvider(
                clientId);

        return new DiagnosticSessionRepository(
            db,
            clientProvider);
    }

    [Fact]
    public async Task SaveDiagnosisAsync_SavesSessionWithAllAnswers()
    {
        await using var db =
            GetMemoryContext();

        var repo =
            CreateRepository(db);

        var payload =
            new DiagnosticSymptomPayload
            {
                SymptomType =
                    "Hardware Failure",

                AffectedActivity =
                    "Gaming",

                Frequency =
                    "Always",

                Severity =
                    "High",

                Duration =
                    "1 week",

                RecentChanges =
                    "None",

                SystemState =
                    "Overheating",

                WarningSigns =
                    "Blue Screen"
            };

        var hardware =
            new HardwareProfileDto
            {
                Cpu =
                    new CpuStatsDto
                    {
                        Name =
                            "Intel i9"
                    },

                OsVersion =
                    "Windows 11",

                Ram =
                    new MemoryStatsDto
                    {
                        TotalGb = 32
                    },

                PrimaryStorageType =
                    "NVMe"
            };

        var sessionId =
            await repo.SaveDiagnosisAsync(
                payload,
                hardware,
                "Thermal Issue",
                "Cooling",
                "High",
                "Clean fans",
                TestClientId);

        var savedSession =
            await db.DiagnosticSessions
                .Include(s => s.Answers)
                .Include(s => s.Output)
                .Include(s => s.Profile)
                .FirstOrDefaultAsync(
                    s => s.Id == sessionId);

        Assert.NotNull(savedSession);
        Assert.NotNull(savedSession.Profile);

        Assert.Equal(
            "Intel i9",
            savedSession.Profile.CpuModel);

        Assert.Equal(
            "Windows 11",
            savedSession.Profile.OsVersion);

        Assert.NotNull(
            savedSession.Output);

        Assert.Equal(
            "Thermal Issue",
            savedSession.Output.DiagnosedCategory);

        Assert.Equal(
            9,
            savedSession.Answers.Count);

        Assert.Equal(
            TestClientId,
            savedSession.Answers
                .First(
                    a =>
                        a.QuestionKey ==
                        "client_id")
                .AnswerValue);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_ReturnsCorrectCounts()
    {
        await using var db =
            GetMemoryContext();

        var repo =
            CreateRepository(
                db,
                "client");

        var hardware =
            new HardwareProfileDto
            {
                Cpu =
                    new CpuStatsDto
                    {
                        Name =
                            "Intel i9"
                    },

                OsVersion =
                    "Windows 11",

                Ram =
                    new MemoryStatsDto
                    {
                        TotalGb = 32
                    }
            };

        var id1 =
            await repo.SaveDiagnosisAsync(
                new DiagnosticSymptomPayload(),
                hardware,
                "Cat1",
                "Act1",
                "High",
                "Exp",
                "client");

        db.ChangeTracker.Clear();

        var id2 =
            await repo.SaveDiagnosisAsync(
                new DiagnosticSymptomPayload(),
                hardware,
                "Cat2",
                "Act2",
                "High",
                "Exp",
                "client");

        db.ChangeTracker.Clear();

        await repo.SaveDiagnosisAsync(
            new DiagnosticSymptomPayload(),
            hardware,
            "Cat3",
            "Act3",
            "High",
            "Exp",
            "client");

        db.ChangeTracker.Clear();

        await repo.UpdateResolutionAsync(
            id1,
            "resolved",
            DateTime.UtcNow.ToString("O"),
            "Fixed",
            Array.Empty<object>());

        db.ChangeTracker.Clear();

        await repo.MarkNeedsRecheckAsync(
            id2);

        db.ChangeTracker.Clear();

        var summary =
            await repo.GetDashboardSummaryAsync();

        var json =
            System.Text.Json.JsonSerializer.Serialize(
                summary);

        Assert.Contains(
            "\"total_sessions\":3",
            json);

        Assert.Contains(
            "\"action_distribution\"",
            json);

        Assert.Contains(
            "\"last_saved_session\"",
            json);
    }

    [Fact]
    public async Task GetRecurringPatternsAsync_WhenMultipleSessionsWithSameCategory_FlagsAsRecurring()
    {
        await using var db =
            GetMemoryContext();

        var repo =
            CreateRepository(
                db,
                "client");

        var hardware =
            new HardwareProfileDto
            {
                Cpu =
                    new CpuStatsDto
                    {
                        Name =
                            "Intel i9"
                    },

                OsVersion =
                    "Windows 11",

                Ram =
                    new MemoryStatsDto
                    {
                        TotalGb = 32
                    }
            };

        await repo.SaveDiagnosisAsync(
            new DiagnosticSymptomPayload(),
            hardware,
            "Thermal condition",
            "Maintain",
            "High",
            "Exp",
            "client");

        db.ChangeTracker.Clear();

        await repo.SaveDiagnosisAsync(
            new DiagnosticSymptomPayload(),
            hardware,
            "Thermal condition",
            "Maintain",
            "High",
            "Exp",
            "client");

        db.ChangeTracker.Clear();

        await repo.SaveDiagnosisAsync(
            new DiagnosticSymptomPayload(),
            hardware,
            "Boot and startup failure",
            "Escalate",
            "High",
            "Exp",
            "client");

        db.ChangeTracker.Clear();

        var patterns =
            await repo.GetRecurringPatternsAsync();

        var json =
            System.Text.Json.JsonSerializer.Serialize(
                patterns);

        Assert.Contains(
            "\"category\":\"Thermal condition\",\"count\":2,\"is_recurring\":true",
            json);

        Assert.Contains(
            "\"category\":\"Boot and startup failure\",\"count\":1,\"is_recurring\":false",
            json);
    }

    [Fact]
    public async Task MarkNeedsRecheckAsync_TransitionsStateToNeedsRecheck()
    {
        await using var db =
            GetMemoryContext();

        var repo =
            CreateRepository(
                db,
                "client");

        var hardware =
            new HardwareProfileDto
            {
                Cpu =
                    new CpuStatsDto
                    {
                        Name =
                            "Intel"
                    },

                OsVersion =
                    "Win11",

                Ram =
                    new MemoryStatsDto
                    {
                        TotalGb = 16
                    }
            };

        var sessionId =
            await repo.SaveDiagnosisAsync(
                new DiagnosticSymptomPayload(),
                hardware,
                "Cat",
                "Act",
                "High",
                "Exp",
                "client");

        db.ChangeTracker.Clear();

        var result =
            await repo.MarkNeedsRecheckAsync(
                sessionId);

        db.ChangeTracker.Clear();

        Assert.True(result);

        var session =
            await db.DiagnosticSessions
                .Include(
                    s => s.Answers)
                .FirstOrDefaultAsync(
                    s =>
                        s.Id ==
                        sessionId);

        Assert.NotNull(session);

        Assert.Equal(
            "needs_recheck",
            session.Answers
                .FirstOrDefault(
                    a =>
                        a.QuestionKey ==
                        "resolution_status")
                ?.AnswerValue);

        Assert.Equal(
            "completed",
            session.Answers
                .FirstOrDefault(
                    a =>
                        a.QuestionKey ==
                        "last_action_status")
                ?.AnswerValue);
    }

    [Fact]
    public async Task UpdateResolutionAsync_TransitionsStateToResolvedAndSavesProof()
    {
        await using var db =
            GetMemoryContext();

        var repo =
            CreateRepository(
                db,
                "client");

        var hardware =
            new HardwareProfileDto
            {
                Cpu =
                    new CpuStatsDto
                    {
                        Name =
                            "Intel"
                    },

                OsVersion =
                    "Win11",

                Ram =
                    new MemoryStatsDto
                    {
                        TotalGb = 16
                    }
            };

        var sessionId =
            await repo.SaveDiagnosisAsync(
                new DiagnosticSymptomPayload(),
                hardware,
                "Cat",
                "Act",
                "High",
                "Exp",
                "client");

        db.ChangeTracker.Clear();

        var proof =
            new[]
            {
                new
                {
                    Name =
                        "Process",

                    Value =
                        "Stopped"
                }
            };

        var result =
            await repo.UpdateResolutionAsync(
                sessionId,
                "resolved",
                "2026-08-21T00:00:00Z",
                "Fixed issue",
                proof);

        db.ChangeTracker.Clear();

        Assert.True(result);

        var session =
            await db.DiagnosticSessions
                .Include(
                    s => s.Answers)
                .FirstOrDefaultAsync(
                    s =>
                        s.Id ==
                        sessionId);

        Assert.NotNull(session);

        Assert.Equal(
            "resolved",
            session.Answers
                .FirstOrDefault(
                    a =>
                        a.QuestionKey ==
                        "resolution_status")
                ?.AnswerValue);

        Assert.Equal(
            "Fixed issue",
            session.Answers
                .FirstOrDefault(
                    a =>
                        a.QuestionKey ==
                        "resolution_summary")
                ?.AnswerValue);

        Assert.Contains(
            "Process",
            session.Answers
                .FirstOrDefault(
                    a =>
                        a.QuestionKey ==
                        "resolution_proof")
                ?.AnswerValue ?? "");
    }

    private sealed class TestCurrentClientProvider
        : ICurrentClientProvider
    {
        private readonly string _clientId;

        public TestCurrentClientProvider(
            string clientId)
        {
            _clientId =
                clientId;
        }

        public string GetCurrentClientId()
        {
            return _clientId;
        }
    }
}