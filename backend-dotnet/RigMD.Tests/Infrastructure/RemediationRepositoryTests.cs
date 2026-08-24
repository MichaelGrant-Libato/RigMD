using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Infrastructure;

public class RemediationRepositoryTests
{
    private RigMdDbContext GetMemoryContext()
    {
        var options =
            new DbContextOptionsBuilder<RigMdDbContext>()
                .UseSqlite("DataSource=:memory:")
                .Options;

        var context = new RigMdDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task GetFailedActionCodesAsync_ReturnsDistinctFailedCodes()
    {
        await using var db = GetMemoryContext();
        var repo = new RemediationRepository(db);

        // Create a diagnostic output to link the remediation run
        var session = new DiagnosticSession();
        var output = new DiagnosticOutput
        {
            DiagnosticSessionId = session.Id,
            DiagnosedCategory = "Test",
            ActionCategory = "Test",
            ConfidenceLabel = "High"
        };
        session.Output = output;

        // Need a profile for session
        var profile = new SystemProfile
        {
            CpuModel = "TestCpu",
            OsVersion = "TestOs"
        };
        session.SystemProfileId = profile.Id;
        session.Profile = profile;

        db.SystemProfiles.Add(profile);
        db.DiagnosticSessions.Add(session);
        await db.SaveChangesAsync();

        // Create a remediation run with a failed action
        var run = new RemediationRun
        {
            DiagnosticOutputId = output.Id,
            Status = "Failed"
        };
        db.RemediationRuns.Add(run);
        await db.SaveChangesAsync();

        var failedAttempt = new ActionAttempt
        {
            RemediationRunId = run.Id,
            ActionCode = "clear_user_temp_files"
        };
        db.ActionAttempts.Add(failedAttempt);
        await db.SaveChangesAsync();

        var failedVerification = new VerificationResult
        {
            ActionAttemptId = failedAttempt.Id,
            IsSuccessful = false,
            ObservedState = "Files still present",
            FailureReason = "Unresolved"
        };
        db.VerificationResults.Add(failedVerification);

        // Also create a successful action to prove it's excluded
        var successfulAttempt = new ActionAttempt
        {
            RemediationRunId = run.Id,
            ActionCode = "flush_dns"
        };
        db.ActionAttempts.Add(successfulAttempt);
        await db.SaveChangesAsync();

        var successVerification = new VerificationResult
        {
            ActionAttemptId = successfulAttempt.Id,
            IsSuccessful = true,
            ObservedState = "DNS cache cleared"
        };
        db.VerificationResults.Add(successVerification);
        await db.SaveChangesAsync();

        var failedCodes = await repo.GetFailedActionCodesAsync();

        Assert.Single(failedCodes);
        Assert.Contains("clear_user_temp_files", failedCodes);
        Assert.DoesNotContain("flush_dns", failedCodes);
    }

    [Fact]
    public async Task GetFailedActionCodesAsync_WhenNoFailures_ReturnsEmptyList()
    {
        await using var db = GetMemoryContext();
        var repo = new RemediationRepository(db);

        var failedCodes = await repo.GetFailedActionCodesAsync();

        Assert.Empty(failedCodes);
    }
}
