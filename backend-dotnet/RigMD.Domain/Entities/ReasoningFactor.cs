namespace RigMD.Domain.Entities;

public class ReasoningFactor : BaseEntity
{
    public Guid DiagnosticOutputId { get; set; }
    public DiagnosticOutput Output { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
    public int ScoreImpact { get; set; }
}
