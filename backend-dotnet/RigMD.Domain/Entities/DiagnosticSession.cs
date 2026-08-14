namespace RigMD.Domain.Entities;

public class DiagnosticSession : BaseEntity
{
    public Guid SystemProfileId { get; set; }
    public SystemProfile Profile { get; set; } = null!;
    
    public ICollection<SessionAnswer> Answers { get; set; } = new List<SessionAnswer>();
    
    public DiagnosticOutput? Output { get; set; }
}
