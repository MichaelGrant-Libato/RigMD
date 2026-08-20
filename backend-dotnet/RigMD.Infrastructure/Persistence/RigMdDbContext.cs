using Microsoft.EntityFrameworkCore;
using RigMD.Domain.Entities;
using System.Text.Json;

namespace RigMD.Infrastructure.Persistence;

public class RigMdDbContext : DbContext
{
    public RigMdDbContext(DbContextOptions<RigMdDbContext> options)
        : base(options)
    {
    }

    public DbSet<SystemProfile> SystemProfiles { get; set; } = null!;
    public DbSet<DiagnosticSession> DiagnosticSessions { get; set; } = null!;
    public DbSet<SessionAnswer> SessionAnswers { get; set; } = null!;
    public DbSet<DiagnosticOutput> DiagnosticOutputs { get; set; } = null!;
    public DbSet<RemediationRun> RemediationRuns { get; set; } = null!;
    public DbSet<ActionAttempt> ActionAttempts { get; set; } = null!;
    public DbSet<RollbackEvent> RollbackEvents { get; set; } = null!;
    public DbSet<PivotEvent> PivotEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Example JSON serialization for JSON columns
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // We can add configuration for domain entities if necessary
        modelBuilder.Entity<DiagnosticOutput>()
            .Property(e => e.ReasoningFactors)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<ReasoningFactor>>(v, jsonOptions) ?? new List<ReasoningFactor>());
        
        modelBuilder.Entity<DiagnosticOutput>()
            .Property(e => e.WarningSigns)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<OutputWarningSign>>(v, jsonOptions) ?? new List<OutputWarningSign>());
    }
}
