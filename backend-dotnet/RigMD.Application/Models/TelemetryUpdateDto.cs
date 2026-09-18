namespace RigMD.Application.Models;

public class TelemetryUpdateDto
{
    public double CpuUsagePercent { get; set; }
    public double CpuSpeedMhz { get; set; }
    
    public double RamUsagePercent { get; set; }
    public double RamUsedGb { get; set; }
    
    public double GpuUsagePercent { get; set; }
    public double GpuMemoryUsedGb { get; set; }
    
    public double DiskActiveTimePercent { get; set; }
    public double DiskReadKbps { get; set; }
    public double DiskWriteKbps { get; set; }
    
    public double NetworkSendKbps { get; set; }
    public double NetworkReceiveKbps { get; set; }
}
