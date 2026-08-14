namespace RigMD.Domain.Entities;

public class SystemProfile : BaseEntity
{
    public string CpuModel { get; set; } = string.Empty;
    public string RamCapacity { get; set; } = string.Empty;
    public string StorageType { get; set; } = string.Empty;
    public string StorageCapacity { get; set; } = string.Empty;
    public string? StorageDetails { get; set; } 
    public string OsVersion { get; set; } = string.Empty;
    public string? GpuDriver { get; set; }
    public string? ChipsetDriver { get; set; }
    public string? SystemAge { get; set; }

    public ICollection<DiagnosticSession> Sessions { get; set; } = new List<DiagnosticSession>();
}
