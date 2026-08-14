namespace RigMD.Domain.Entities;

public class RemediationRun : BaseEntity
{
    public Guid DiagnosticOutputId { get; set; }
    public DiagnosticOutput Output { get; set; } = null!;

    public string Status { get; set; } = string.Empty; // e.g. "InProgress", "Resolved", "Failed", "Escalated"
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<ActionAttempt> ActionAttempts { get; set; } = new List<ActionAttempt>();
    public ICollection<RollbackEvent> RollbackEvents { get; set; } = new List<RollbackEvent>();
    public ICollection<PivotEvent> PivotEvents { get; set; } = new List<PivotEvent>();
}
