namespace RigMD.Domain.Entities;

public class ActionAttempt : BaseEntity
{
    public Guid RemediationRunId { get; set; }
    public RemediationRun Run { get; set; } = null!;

    public string ActionCode { get; set; } = string.Empty;
    
    // JSON snapshot of state before execution
    public string PreconditionState { get; set; } = string.Empty; 
    
    public VerificationResult? Verification { get; set; }
}
