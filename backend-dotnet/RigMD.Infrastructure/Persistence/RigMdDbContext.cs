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
    public DbSet<VerificationResult> VerificationResults { get; set; } = null!;
    public DbSet<WarningSign> WarningSigns { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // JSON value converters for collection navigation properties stored as JSON columns
        modelBuilder.Entity<DiagnosticOutput>()
            .Property(e => e.ReasoningFactors)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<ICollection<ReasoningFactor>>(v, jsonOptions)
                     ?? new List<ReasoningFactor>())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<ICollection<ReasoningFactor>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c != null ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())) : 0,
                c => c != null ? c.ToList() : new List<ReasoningFactor>()));

        modelBuilder.Entity<DiagnosticOutput>()
            .Property(e => e.WarningSigns)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<ICollection<OutputWarningSign>>(v, jsonOptions)
                     ?? new List<OutputWarningSign>())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<ICollection<OutputWarningSign>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c != null ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())) : 0,
                c => c != null ? c.ToList() : new List<OutputWarningSign>()));

        // Enforce unique session-profile relationships
        modelBuilder.Entity<DiagnosticSession>()
            .HasOne(s => s.Profile)
            .WithMany(p => p.Sessions)
            .HasForeignKey(s => s.SystemProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DiagnosticSession>()
            .HasOne(s => s.Output)
            .WithOne(o => o.Session)
            .HasForeignKey<DiagnosticOutput>(o => o.DiagnosticSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // SQLite doesn't support DateTimeOffset sorting natively. We tell EF to convert them to string
        // which preserves ISO8601 formatting and allows textual sorting in SQLite.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));
                foreach (var property in properties)
                {
                    modelBuilder
                        .Entity(entityType.Name)
                        .Property(property.Name)
                        .HasConversion<string>();
                }
            }
        }

        modelBuilder.Entity<RemediationRun>()
            .HasOne(r => r.Output)
            .WithMany(o => o.RemediationRuns)
            .HasForeignKey(r => r.DiagnosticOutputId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ActionAttempt>()
            .HasOne(a => a.Verification)
            .WithOne(v => v.Attempt)
            .HasForeignKey<VerificationResult>(v => v.ActionAttemptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
