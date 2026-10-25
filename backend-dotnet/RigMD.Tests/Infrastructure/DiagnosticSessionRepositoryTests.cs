using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Rules;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public class DiagnosticSessionRepositoryTests
{
    [Fact]
    public async Task DeviceIdentityIsSavedSeparatelyForNewChecksAndLegacyFollowups()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var id = await repo.SaveAutomaticDiagnosisAsync(new HardwareProfileDto(), "component", ["memory"],
            null, Guid.Empty, "local", "High Memory Pressure", "Troubleshoot", "Partial", "Saved warning.",
            TestClientId, proof: [new() { MetricId = "memory_usage", Label = "Memory", Value = "90%", Status = "high", EvidenceKind = "measurement" }]);
        var identity = await db.SessionAnswers.SingleAsync(a => a.DiagnosticSessionId == id && a.QuestionKey == "device_id");
        Assert.Equal(identity.AnswerValue, (await repo.GetSessionAsync(id))!.DeviceId);
        Assert.Matches("^([A-F0-9]{64})?$", identity.AnswerValue);
        db.SessionAnswers.Remove(identity);
        await db.SaveChangesAsync();
        await repo.UpdateResolutionAsync(id, "still_active", DateTimeOffset.UtcNow.ToString("o"), "Fresh reading.",
            [new { metric_id = "memory_usage", label = "Memory", value = "91%", status = "high", evidence_kind = "measurement" }]);
        db.ChangeTracker.Clear();
        var loaded = (await repo.GetSessionAsync(id))!;
        var followupIdentity = await db.SessionAnswers.SingleAsync(a => a.DiagnosticSessionId == id && a.QuestionKey == "resolution_device_id");
        Assert.Equal("", loaded.DeviceId);
        Assert.Equal(followupIdentity.AnswerValue, loaded.ResolutionDeviceId);
        Assert.Equal(loaded.ResolutionDeviceId, Assert.Single(await repo.GetSessionsAsync()).ResolutionDeviceId);
        Assert.Equal("90%", Assert.IsType<System.Text.Json.JsonElement>(Assert.Single(loaded.Proof)).GetProperty("value").GetString());
    }

    private const string TestClientId =
        "test-client-123";

    [Theory]
    [InlineData("gemini", "gemini-test", "gemini-test")]
    [InlineData("local", "ignored-model", "")]
    public async Task ExplanationProvenanceSurvivesReload_AndLegacyRecordsStayUnknown(string source, string model, string savedModel)
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var id = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), new HardwareProfileDto(),
            "High Memory Pressure", "Troubleshoot", "High", "Explanation", TestClientId);
        Assert.Empty((await repo.GetSessionAsync(id))!.ExplanationSource);
        await CreateRepository(db, "other-client").SaveExplanationSourceAsync(id, "gemini", "must-not-be-saved");
        Assert.Empty((await repo.GetSessionAsync(id))!.ExplanationSource);
        await repo.SaveExplanationSourceAsync(id, source, model);
        db.ChangeTracker.Clear();
        var saved = await repo.GetSessionAsync(id);
        Assert.Equal(source, saved!.ExplanationSource);
        Assert.Equal(savedModel, saved.ExplanationModel);
    }

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
    public async Task AutomaticReadingsAndNextStep_SurviveReload_SeparatelyFromFollowUp()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var proof = new[] { new AutomaticDiagnosisProof
        {
            MetricId = "cpu_temperature", CapturedAt = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
            EvidenceKind = "unavailable", RuleVersion = "windows-review-v2",
            Label = "CPU temperature", Value = "Unavailable", Status = "unknown",
            Meaning = "The required sensor could not be read."
        } };
        var id = await repo.SaveAutomaticDiagnosisAsync(new HardwareProfileDto(), "component", new[] { "cpu" },
            null, Guid.Empty, "local", "Thermal telemetry unavailable", "Monitor", "Low", "No measured temperature.",
            TestClientId, proof: proof, recommendedNextStep: "Check again when a temperature reading is available.");
        await repo.UpdateResolutionAsync(id, "unknown", DateTimeOffset.UtcNow.ToString("O"), "Follow-up unavailable",
            new object[] { new { label = "CPU temperature", value = "Still unavailable", status = "unknown" } });
        db.ChangeTracker.Clear();
        var loaded = await repo.GetSessionAsync(id);
        Assert.NotNull(loaded);
        var original = Assert.IsType<System.Text.Json.JsonElement>(Assert.Single(loaded.Proof));
        Assert.Equal("Unavailable", original.GetProperty("value").GetString());
        Assert.Equal("cpu_temperature", original.GetProperty("metric_id").GetString());
        Assert.Equal(proof[0].CapturedAt, original.GetProperty("captured_at").GetDateTimeOffset());
        Assert.Equal("unavailable", original.GetProperty("evidence_kind").GetString());
        Assert.Equal("windows-review-v2", original.GetProperty("rule_version").GetString());
        Assert.Equal("unknown", original.GetProperty("status").GetString());
        Assert.Equal("The required sensor could not be read.", original.GetProperty("meaning").GetString());
        var followUp = Assert.IsType<System.Text.Json.JsonElement>(Assert.Single(loaded.ResolutionProof));
        Assert.Equal("Still unavailable", followUp.GetProperty("value").GetString());
        Assert.Equal("Check again when a temperature reading is available.", loaded.RecommendedNextStep);
        Assert.Single((await repo.GetSessionsAsync()).Single().Proof);
    }

    [Fact]
    public async Task EventWindowsSurviveReload_WithoutReplacingHistoricalEvidence()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var start = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var original = new AutomaticDiagnosisProof
        {
            MetricId = "application_crash_history", EvidenceKind = "event_log", RuleVersion = "windows-review-v2",
            Label = "Application crash records (last 7 days)", Value = "2 found", Status = "high",
            WindowKind = "rolling_history", WindowStart = start.AddDays(-7), WindowEnd = start
        };
        var id = await repo.SaveAutomaticDiagnosisAsync(new HardwareProfileDto(), "scenario", Array.Empty<string>(),
            "app-crashes", Guid.Empty, "local", "Application crash history requires review", "Troubleshoot", "Partial", "Review records.",
            TestClientId, proof: new[] { original });
        await repo.UpdateResolutionAsync(id, "resolved", start.AddMinutes(1).ToString("O"), "No new matching records; not proof of repair.",
            new object[] { new { metric_id = original.MetricId, label = "Application crash records since diagnosis", value = "0 found",
                status = "normal", window_kind = "since_diagnosis", window_start = start, window_end = start.AddMinutes(1) } });
        db.ChangeTracker.Clear();
        var loaded = (await repo.GetSessionAsync(id))!;
        foreach (var session in new[] { loaded, (await repo.GetSessionsAsync()).Single() })
        {
            var historical = Assert.IsType<System.Text.Json.JsonElement>(Assert.Single(session.Proof));
            Assert.Equal("rolling_history", historical.GetProperty("window_kind").GetString());
            Assert.Equal(start.AddDays(-7), historical.GetProperty("window_start").GetDateTimeOffset());
            Assert.Equal(start, historical.GetProperty("window_end").GetDateTimeOffset());
            Assert.Equal("2 found", historical.GetProperty("value").GetString());
            var followUp = Assert.IsType<System.Text.Json.JsonElement>(Assert.Single(session.ResolutionProof));
            Assert.Equal("since_diagnosis", followUp.GetProperty("window_kind").GetString());
            Assert.Equal(start, followUp.GetProperty("window_start").GetDateTimeOffset());
            Assert.Equal("0 found", followUp.GetProperty("value").GetString());
        }
    }

    [Fact]
    public async Task LegacyCheck_DoesNotInventOriginalReadings()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var id = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), new HardwareProfileDto(),
            "No Active Issue Detected", "Monitor", "High", "Saved legacy summary", TestClientId);
        var loaded = await repo.GetSessionAsync(id);
        Assert.NotNull(loaded);
        Assert.Empty(loaded.Proof);
        Assert.False(string.IsNullOrWhiteSpace(loaded.RecommendedNextStep));
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
            10,
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
    public async Task GetRecurringPatternsAsync_CategoryRepetitionAloneDoesNotProveRepeatedWarnings()
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

        Assert.Equal("{\"patterns\":[]}", json);
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

    [Fact]
    public async Task GetSessionsAsync_AfterSaveAndNewContext_ReturnsSavedSessions()
    {
        // Use a shared SQLite file-like connection to simulate restart
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();

        // Save session using first context
        Guid savedSessionId;
        {
            var options = new DbContextOptionsBuilder<RigMdDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var db1 = new RigMdDbContext(options);
            db1.Database.EnsureCreated();

            var repo1 = CreateRepository(db1, "restart-client");

            var hardware = new HardwareProfileDto
            {
                Cpu = new CpuStatsDto { Name = "Intel i9" },
                OsVersion = "Windows 11",
                Ram = new MemoryStatsDto { TotalGb = 32 }
            };

            savedSessionId = await repo1.SaveDiagnosisAsync(
                new DiagnosticSymptomPayload { SymptomType = "Thermal condition" },
                hardware,
                "Thermal condition",
                "Maintain",
                "High",
                "Clean fans",
                "restart-client");
        }

        // Read back using a fresh context (simulated restart)
        {
            var options = new DbContextOptionsBuilder<RigMdDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var db2 = new RigMdDbContext(options);

            var repo2 = CreateRepository(db2, "restart-client");

            var sessions = await repo2.GetSessionsAsync();

            Assert.NotEmpty(sessions);
            Assert.Contains(sessions, s => s.SessionId == savedSessionId.ToString());

            var loaded = sessions.First(s => s.SessionId == savedSessionId.ToString());
            Assert.Equal("Thermal condition", loaded.DiagnosedCategory);
            Assert.Equal("Maintain", loaded.ActionCategory);
            Assert.Equal("Thermal condition", loaded.SymptomType);
        }

        connection.Dispose();
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesSessionAnswersAndOutput()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db, "client");

        var hardware = new HardwareProfileDto
        {
            Cpu = new CpuStatsDto { Name = "Intel i9" },
            OsVersion = "Windows 11",
            Ram = new MemoryStatsDto { TotalGb = 32 }
        };

        var sessionId = await repo.SaveDiagnosisAsync(
            new DiagnosticSymptomPayload
            {
                SymptomType = "Slow system",
                WarningSigns = "None"
            },
            hardware,
            "Elevated Memory Usage",
            "Monitor",
            "Moderate",
            "Memory use was high during the check.",
            "client");

        db.ChangeTracker.Clear();

        var deleted = await repo.DeleteSessionAsync(sessionId);

        Assert.True(deleted);
        Assert.Empty(await db.DiagnosticSessions.ToListAsync());
        Assert.Empty(await db.SessionAnswers.ToListAsync());
        Assert.Empty(await db.DiagnosticOutputs.ToListAsync());
    }

    [Fact]
    public async Task GetRemediationHistoryAsync_ReturnsRunsAndAttempts()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db, "client");

        var hardware = new HardwareProfileDto
        {
            Cpu = new CpuStatsDto { Name = "Intel i9" },
            OsVersion = "Windows 11",
            Ram = new MemoryStatsDto { TotalGb = 32 }
        };

        var sessionId = await repo.SaveDiagnosisAsync(
            new DiagnosticSymptomPayload(),
            hardware,
            "OS performance degradation",
            "Troubleshoot",
            "High",
            "Explanation",
            "client");

        db.ChangeTracker.Clear();

        // Get the session's output to link the remediation run
        var session = await db.DiagnosticSessions
            .Include(s => s.Output)
            .FirstAsync(s => s.Id == sessionId);

        var run = new RigMD.Domain.Entities.RemediationRun
        {
            DiagnosticOutputId = session.Output!.Id,
            Status = "Resolved"
        };
        db.RemediationRuns.Add(run);
        await db.SaveChangesAsync();

        var attempt = new RigMD.Domain.Entities.ActionAttempt
        {
            RemediationRunId = run.Id,
            ActionCode = "clear_user_temp_files"
        };
        db.ActionAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var verification = new RigMD.Domain.Entities.VerificationResult
        {
            ActionAttemptId = attempt.Id,
            IsSuccessful = true,
            ObservedState = "Temp files cleared"
        };
        db.VerificationResults.Add(verification);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var history = await repo.GetRemediationHistoryAsync(sessionId);

        Assert.Single(history);
        Assert.Equal("Resolved", history[0].Status);
        Assert.Single(history[0].Attempts);
        Assert.Equal("clear_user_temp_files", history[0].Attempts[0].ActionCode);
        Assert.Equal("Resolved", history[0].Attempts[0].VerificationStatus);
    }

    [Fact]
    public async Task ActionPendingRecheck_DoesNotReusePreActionReadingsAsItsResult()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var id = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), new HardwareProfileDto(),
            "High Memory Pressure", "Troubleshoot", "High", "Saved check", TestClientId);
        await repo.UpdateResolutionAsync(id, "still_active", "2026-10-20T10:00:00Z", "Previous check",
            [new { metric_id = "memory_usage", label = "Memory", value = "91%", status = "high" }]);
        await repo.MarkNeedsRecheckAsync(id);
        db.ChangeTracker.Clear();
        var detail = await repo.GetSessionAsync(id);
        Assert.NotNull(detail);
        Assert.Equal("needs_recheck", detail.ResolutionStatus);
        Assert.Empty(detail.ResolutionProof);
        Assert.Equal("", detail.ResolutionCheckedAt);
        Assert.Contains("still needs checking", detail.ResolutionSummary);
        Assert.Contains(await db.SessionAnswers.Where(a => a.DiagnosticSessionId == id).ToListAsync(),
            a => a.QuestionKey == "pre_action_readings" && a.AnswerValue.Contains("91%"));
    }

    [Fact]
    public async Task RecurringCountsAndHistoryBadgesUseTheSameMeasuredWarnings()
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        foreach (var day in new[] { 1, 2 })
        {
            var id = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), new HardwareProfileDto(),
                "High Memory Pressure", "Troubleshoot", "High", "Saved check", TestClientId);
            db.SessionAnswers.Add(new RigMD.Domain.Entities.SessionAnswer { DiagnosticSessionId = id,
                QuestionKey = "proof", AnswerValue = "[{\"metric_id\":\"memory_usage\",\"label\":\"Memory\",\"value\":\"91%\",\"status\":\"high\"}]" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        var saved = await repo.GetSessionsAsync();
        Assert.All(saved, s => Assert.True(s.IsRecurring));
        var patterns = new RigMD.Application.Services.RecurringPatternService().BuildPatterns(await repo.GetRecurringSessionsAsync());
        Assert.Single(patterns.patterns);
        Assert.Equal(2, patterns.metrics.total_occurrences);
        Assert.Contains("\"recurring_issues_count\":1", System.Text.Json.JsonSerializer.Serialize(await repo.GetDashboardSummaryAsync()));
        Assert.Contains("\"count\":2,\"is_recurring\":true", System.Text.Json.JsonSerializer.Serialize(await repo.GetRecurringPatternsAsync()));
    }

    [Theory]
    [InlineData("[{\"label\":\"Memory use\",\"value\":\"89%\",\"status\":\"high\",\"meaning\":\"Saved reading\"}]", 1)]
    [InlineData("", 0)]
    [InlineData("null", 0)]
    [InlineData("{}", 0)]
    [InlineData("invalid json", 0)]
    [InlineData("[null,42,{}]", 0)]
    public async Task SavedFollowUpReadings_SurviveReopeningWithoutInventingMissingData(string proof, int expectedCount)
    {
        await using var db = GetMemoryContext();
        var repo = CreateRepository(db);
        var id = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), new HardwareProfileDto(),
            "High Memory Pressure", "Troubleshoot", "High", "Saved check", TestClientId);
        db.SessionAnswers.Add(new RigMD.Domain.Entities.SessionAnswer
        {
            DiagnosticSessionId = id, QuestionKey = "resolution_proof", AnswerValue = proof
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var detail = await repo.GetSessionAsync(id);
        var list = await repo.GetSessionsAsync();
        Assert.NotNull(detail);
        Assert.Equal(expectedCount, detail.ResolutionProof.Length);
        Assert.Equal(expectedCount, Assert.Single(list).ResolutionProof.Length);
        if (expectedCount > 0)
        {
            var item = Assert.IsType<System.Text.Json.JsonElement>(detail.ResolutionProof[0]);
            Assert.Equal("89%", item.GetProperty("value").GetString());
            Assert.Equal("high", item.GetProperty("status").GetString());
        }
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
