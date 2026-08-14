namespace RigMD.Domain.Entities;

public class PivotEvent : BaseEntity
{
    public Guid RemediationRunId { get; set; }
    public RemediationRun Run { get; set; } = null!;

    public string Reason { get; set; } = string.Empty;
    public string FromActionCode { get; set; } = string.Empty;
    public string ToActionCode { get; set; } = string.Empty;
}
