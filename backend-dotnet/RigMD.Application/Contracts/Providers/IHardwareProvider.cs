using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Providers;

public interface ICpuProvider
{
    CpuStatsDto GetCpuStats();
}

public interface IGpuProvider
{
    GpuStatsDto GetGpuStats();
}

public interface IMemoryProvider
{
    MemoryStatsDto GetMemoryStats();
}

public interface IOperatingSystemProvider
{
    string GetDeviceName();
    string GetOsVersion();
    string GetSystemAge();
}

public interface IStorageProvider
{
    string GetPrimaryStorageType();
    List<StorageDriveDto> GetStorageDrives();
    List<DiskVolumeDto> GetAllDisks();
}

public interface IMotherboardProvider
{
    string GetChipsetDriver();
}

public interface IProcessProvider
{
    ProcessInsightsDto GetProcessInsights();
}

public interface IWindowsSystemProfileService
{
    HardwareProfileDto GetLiveSystemProfile();
}
