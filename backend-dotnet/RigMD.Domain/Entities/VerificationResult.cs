namespace RigMD.Domain.Entities;

public class VerificationResult : BaseEntity
{
    public Guid ActionAttemptId { get; set; }
    public ActionAttempt Attempt { get; set; } = null!;

    public bool IsSuccessful { get; set; }
    public string ObservedState { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
}
