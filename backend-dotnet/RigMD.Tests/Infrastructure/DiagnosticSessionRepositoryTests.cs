using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Rules;
using RigMD.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace RigMD.Tests.Infrastructure;

public class DiagnosticSessionRepositoryTests
{
    private RigMdDbContext GetMemoryContext()
    {
        var options = new DbContextOptionsBuilder<RigMdDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new RigMdDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task SaveDiagnosisAsync_SavesSessionWithAllAnswers()
    {
        // Arrange
        var db = GetMemoryContext();
        var repo = new DiagnosticSessionRepository(db);

        var payload = new DiagnosticSymptomPayload
        {
            SymptomType = "Hardware Failure",
            AffectedActivity = "Gaming",
            Frequency = "Always",
            Severity = "High",
            Duration = "1 week",
            RecentChanges = "None",
            SystemState = "Overheating",
            WarningSigns = "Blue Screen"
        };

        var hardware = new HardwareProfileDto
        {
            Cpu = new CpuStatsDto { Name = "Intel i9" },
            OsVersion = "Windows 11",
            Ram = new MemoryStatsDto { TotalGb = 32 },
            PrimaryStorageType = "NVMe"
        };

        // Act
        var sessionId = await repo.SaveDiagnosisAsync(
            payload, 
            hardware, 
            "Thermal Issue", 
            "Cooling", 
            "High", 
            "Clean fans", 
            "test-client-123");

        // Assert
        var savedSession = await db.DiagnosticSessions
            .Include(s => s.Answers)
            .Include(s => s.Output)
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        Assert.NotNull(savedSession);
        Assert.Equal("Intel i9", savedSession.Profile.CpuModel);
        Assert.Equal("Windows 11", savedSession.Profile.OsVersion);
        
        Assert.NotNull(savedSession.Output);
        Assert.Equal("Thermal Issue", savedSession.Output.DiagnosedCategory);
        
        Assert.Equal(9, savedSession.Answers.Count);
        Assert.Equal("test-client-123", savedSession.Answers.First(a => a.QuestionKey == "client_id").AnswerValue);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var db = GetMemoryContext();
        var repo = new DiagnosticSessionRepository(db);

        var hardware = new HardwareProfileDto
        {
            Cpu = new CpuStatsDto { Name = "Intel i9" },
            OsVersion = "Windows 11",
            Ram = new MemoryStatsDto { TotalGb = 32 }
        };

        var id1 = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), hardware, "Cat1", "Act1", "High", "Exp", "client");
        db.ChangeTracker.Clear();
        var id2 = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), hardware, "Cat2", "Act2", "High", "Exp", "client");
        db.ChangeTracker.Clear();
        var id3 = await repo.SaveDiagnosisAsync(new DiagnosticSymptomPayload(), hardware, "Cat3", "Act3", "High", "Exp", "client");
        db.ChangeTracker.Clear();

        await repo.UpdateResolutionAsync(id1, "resolved", DateTime.UtcNow.ToString("O"), "Fixed", Array.Empty<object>());
        db.ChangeTracker.Clear();
        await repo.MarkNeedsRecheckAsync(id2);
        db.ChangeTracker.Clear();

        // Act
        var summary = await repo.GetDashboardSummaryAsync();

        // Assert
        var json = System.Text.Json.JsonSerializer.Serialize(summary);
        Assert.Contains("\"total_sessions\":3", json);
        Assert.Contains("\"resolved_sessions\":1", json);
        Assert.Contains("\"needs_recheck_sessions\":1", json);
        Assert.Contains("\"open_sessions\":1", json);
    }
}
