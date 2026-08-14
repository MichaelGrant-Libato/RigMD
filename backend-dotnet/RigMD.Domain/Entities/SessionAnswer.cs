namespace RigMD.Domain.Entities;

public class SessionAnswer : BaseEntity
{
    public Guid DiagnosticSessionId { get; set; }
    public DiagnosticSession Session { get; set; } = null!;

    public string QuestionKey { get; set; } = string.Empty; // e.g. symptom_type, severity
    public string AnswerValue { get; set; } = string.Empty;
}
