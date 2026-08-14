namespace RigMD.Application.Models;

public class CpuStatsDto
{
    public string Name { get; set; } = string.Empty;
    public double UsagePercent { get; set; }
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double FrequencyMhz { get; set; }
}

public class GpuStatsDto
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Dedicated / Integrated
    public double VramGb { get; set; }
}

public class MemoryStatsDto
{
    public double TotalGb { get; set; }
    public double UsedGb { get; set; }
    public double UsagePercent { get; set; }
}

public class StorageDriveDto
{
    public string Model { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // NVMe SSD, SATA SSD, HDD
    public double SizeGb { get; set; }
    public string Interface { get; set; } = string.Empty;
    public int? DiskIndex { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? BusType { get; set; }
    public string DetectionSource { get; set; } = string.Empty;
    
    public double? UsedGb { get; set; }
    public double? UsagePercent { get; set; }
    
    public List<DiskVolumeDto> Volumes { get; set; } = new();
}

public class DiskVolumeDto
{
    public string Drive { get; set; } = string.Empty; // e.g. C:\
    public string Mountpoint { get; set; } = string.Empty;
    public string FsType { get; set; } = string.Empty; // NTFS, FAT32
    public int? DiskIndex { get; set; }
    public double TotalGb { get; set; }
    public double UsedGb { get; set; }
    public double UsagePercent { get; set; }
}

public class ProcessInsightsDto
{
    public bool BrowserDetected { get; set; }
    public int BrowserProcessCount { get; set; }
    public double BrowserMemoryMb { get; set; }
    public bool BrowserHeavy { get; set; }
    
    public bool GameDetected { get; set; }
    public List<string> GameProcesses { get; set; } = new();
    
    public List<ProcessAppDto> TopMemoryApps { get; set; } = new();
}

public class ProcessAppDto
{
    public string Name { get; set; } = string.Empty;
    public int ProcessCount { get; set; }
    public double MemoryMb { get; set; }
}

public class HardwareProfileDto
{
    public string DeviceName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string SystemAge { get; set; } = string.Empty;
    public string ChipsetDriver { get; set; } = string.Empty;
    public string PrimaryStorageType { get; set; } = string.Empty;
    
    public CpuStatsDto Cpu { get; set; } = new();
    public GpuStatsDto Gpu { get; set; } = new();
    public MemoryStatsDto Ram { get; set; } = new();
    
    public List<StorageDriveDto> StorageDrives { get; set; } = new();
    public List<DiskVolumeDto> AllDisks { get; set; } = new();
    
    public ProcessInsightsDto ProcessInsights { get; set; } = new();
}
