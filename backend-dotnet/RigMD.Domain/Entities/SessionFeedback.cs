namespace RigMD.Domain.Entities;

/// <summary>A user's observation, separate from diagnostic evidence and repair verification.</summary>
public sealed class SessionFeedback : BaseEntity
{
    public Guid DiagnosticSessionId { get; set; }
    public DiagnosticSession Session { get; set; } = null!;
    public string Outcome { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
