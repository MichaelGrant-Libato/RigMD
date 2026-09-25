namespace RigMD.Application.Models;

public class CpuStatsDto
{
    public string Name { get; set; } = string.Empty;
    public double UsagePercent { get; set; }
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double FrequencyMhz { get; set; }
    public double MaxFrequencyMhz { get; set; }
    public bool IsThermallyThrottling { get; set; }
    public int Sockets { get; set; } = 1;
    public bool VirtualizationEnabled { get; set; }
    public double L1CacheKb { get; set; }
    public double L2CacheMb { get; set; }
    public double L3CacheMb { get; set; }
    public int Processes { get; set; }
    public int Handles { get; set; }
    public double? TemperatureCelsius { get; set; }
}

public enum ComponentStatus
{
    Present,
    NotPresent
}

public class ComponentPresenceInfoDto
{
    public string ComponentId { get; set; } = string.Empty;
    public string Status { get; set; } = nameof(ComponentStatus.Present);
    public bool IsSelectable { get; set; } = true;
    public string? Badge { get; set; }
    public string? Reason { get; set; }
}

public class HardwarePresenceProbeDto
{
    public string DeviceType { get; set; } = "Desktop";
    public bool HasBattery { get; set; }
    public bool HasGpu { get; set; } = true;
    public bool HasDedicatedGpu { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public string GpuType { get; set; } = string.Empty;
    public Dictionary<string, ComponentPresenceInfoDto> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class GpuStatsDto
{
    public bool HasGpu { get; set; } = true;
    public bool HasDedicatedGpu { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Dedicated / Integrated
    public double VramGb { get; set; }
    public double DedicatedMemoryGb { get; set; }
    public double SharedMemoryGb { get; set; }
    public string DriverDate { get; set; } = string.Empty;
    public string DirectXVersion { get; set; } = string.Empty;
    public string PhysicalLocation { get; set; } = string.Empty;
    public double? TemperatureCelsius { get; set; }
}

public class MemoryStatsDto
{
    public double TotalGb { get; set; }
    public double UsedGb { get; set; }
    public double UsagePercent { get; set; }
    public double SpeedMtps { get; set; }
    public int SlotsUsed { get; set; }
    public int SlotsTotal { get; set; }
    public string FormFactor { get; set; } = string.Empty;
    public double HardwareReservedMb { get; set; }
    public double CommittedGb { get; set; }
    public double CachedGb { get; set; }
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
    public bool IsFailingSmart { get; set; }
    
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
    
    public string? MemoryLeakWarning { get; set; }
    
    public List<ProcessAppDto> TopMemoryApps { get; set; } = new();
}

public class ProcessAppDto
{
    public string Name { get; set; } = string.Empty;
    public int ProcessCount { get; set; }
    public double MemoryMb { get; set; }
}

public class NetworkStatsDto
{
    public bool HasActiveAdapter { get; set; }
    public string AdapterName { get; set; } = string.Empty;
    public bool HasIpv4Address { get; set; }
    public bool HasDefaultGateway { get; set; }
    public bool HasDnsServers { get; set; }
    public bool DnsResolutionSucceeded { get; set; }
    public string DnsTestHost { get; set; } = string.Empty;
    public string DnsResolutionMessage { get; set; } = string.Empty;
    public bool IsWifi { get; set; }
    public int WifiSignalStrength { get; set; }
    public long? PingLatencyMs { get; set; }
    public double? PacketLossPercent { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

public class BatteryStatsDto
{
    public bool HasBattery { get; set; }
    public int EstimatedChargeRemaining { get; set; }
    public int BatteryStatus { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public int EstimatedRunTime { get; set; }
    
    // Laptop UI Properties
    public bool IsCharging { get; set; }
    public int ChargePercent { get; set; }
    public string HealthStatus { get; set; } = "Good";
}

public class DisplayStatsDto
{
    public string Name { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public int RefreshRate { get; set; }
}

public class DeviceErrorDto
{
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public uint ErrorCode { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class HardwareProfileDto
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = "Unknown";
    public string OsVersion { get; set; } = string.Empty;
    public string SystemAge { get; set; } = string.Empty;
    public string ChipsetDriver { get; set; } = string.Empty;
    public string PrimaryStorageType { get; set; } = string.Empty;
    public string ActivePowerPlan { get; set; } = string.Empty;
    public int ConnectedDisplays { get; set; }
    
    public List<DisplayStatsDto> Displays { get; set; } = new();
    public List<DeviceErrorDto> DeviceErrors { get; set; } = new();
    
    public CpuStatsDto Cpu { get; set; } = new();
    public GpuStatsDto Gpu { get; set; } = new();
    public MemoryStatsDto Ram { get; set; } = new();

    public NetworkStatsDto Network { get; set; } = new();
    public BatteryStatsDto? Battery { get; set; }
    
    public List<StorageDriveDto> StorageDrives { get; set; } = new();
    public List<DiskVolumeDto> AllDisks { get; set; } = new();
    
    public ProcessInsightsDto ProcessInsights { get; set; } = new();
    public HardwarePresenceProbeDto? Presence { get; set; }
}

public class SaveProfilePayload
{
    public string CpuModel { get; set; } = string.Empty;
    public string RamCapacity { get; set; } = string.Empty;
    public string StorageType { get; set; } = string.Empty;
    public string StorageCapacity { get; set; } = string.Empty;
    public object? StorageDetails { get; set; }
    public string OsVersion { get; set; } = string.Empty;
    public string? GpuDriver { get; set; }
    public string? ChipsetDriver { get; set; }
    public string? SystemAge { get; set; }
}
