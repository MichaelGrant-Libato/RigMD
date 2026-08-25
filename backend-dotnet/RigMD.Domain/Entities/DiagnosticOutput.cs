namespace RigMD.Domain.Entities;

public class DiagnosticOutput : BaseEntity
{
    public Guid DiagnosticSessionId { get; set; }
    public DiagnosticSession Session { get; set; } = null!;

    public string DiagnosedCategory { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string ConfidenceLabel { get; set; } = string.Empty;
    public string? AiExplanation { get; set; }

    public ICollection<ReasoningFactor> ReasoningFactors { get; set; } = new List<ReasoningFactor>();
    public ICollection<OutputWarningSign> WarningSigns { get; set; } = new List<OutputWarningSign>();
    
    public ICollection<RemediationRun> RemediationRuns { get; set; } = new List<RemediationRun>();
}
