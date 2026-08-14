namespace RigMD.Domain.Entities;

public class RollbackEvent : BaseEntity
{
    public Guid RemediationRunId { get; set; }
    public RemediationRun Run { get; set; } = null!;

    public Guid ActionAttemptId { get; set; }
    public ActionAttempt AttemptTarget { get; set; } = null!;

    public bool WasSuccessful { get; set; }
    public string RestoredState { get; set; } = string.Empty;
}
