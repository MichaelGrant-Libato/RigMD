namespace RigMD.Domain.Entities;

public class OutputWarningSign : BaseEntity
{
    public Guid DiagnosticOutputId { get; set; }
    public DiagnosticOutput Output { get; set; } = null!;

    public Guid WarningSignId { get; set; }
    public WarningSign WarningSign { get; set; } = null!;

    public string ObservedValue { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}
