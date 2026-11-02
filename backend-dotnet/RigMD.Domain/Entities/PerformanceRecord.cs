namespace RigMD.Domain.Entities;

/// <summary>Local operational evidence, independent of diagnostic findings and user feedback.</summary>
public sealed class PerformanceRecord : BaseEntity
{
    public string ClientId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public int? RecordCount { get; set; }
    public int? UnavailableCount { get; set; }
    public string Mode { get; set; } = "normal";
    public string Engine { get; set; } = "unknown";
    public int? HttpStatus { get; set; }
    public double? ApiWorkingSetMb { get; set; }
    public double? ApiCpuTimeMs { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string BuildConfiguration { get; set; } = string.Empty;
    public string DeviceKey { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int LogicalProcessors { get; set; }
}
