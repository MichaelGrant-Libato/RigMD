namespace RigMD.Application.Models;

public class DiskTelemetryDto
{
    public string DeviceId { get; set; } = string.Empty;
    public double ActiveTimePercent { get; set; }
    public double ReadKbps { get; set; }
    public double WriteKbps { get; set; }
}

public class TelemetryUpdateDto
{
    public double CpuUsagePercent { get; set; }
    public double CpuSpeedMhz { get; set; }
    public double? CpuTempCelsius { get; set; }
    public int CpuProcesses { get; set; }
    public int CpuThreads { get; set; }
    public int CpuHandles { get; set; }
    
    public double RamUsagePercent { get; set; }
    public double RamUsedGb { get; set; }
    
    public double GpuUsagePercent { get; set; }
    public double GpuMemoryUsedGb { get; set; }
    public double? GpuMemoryTotalGb { get; set; }
    public double? GpuTempCelsius { get; set; }
    
    public List<DiskTelemetryDto> Disks { get; set; } = new();
    
    public double NetworkSendKbps { get; set; }
    public double NetworkReceiveKbps { get; set; }
}
