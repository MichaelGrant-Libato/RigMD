namespace RigMD.Domain.Entities;

public class WarningSign : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultThreshold { get; set; } = string.Empty;
}
