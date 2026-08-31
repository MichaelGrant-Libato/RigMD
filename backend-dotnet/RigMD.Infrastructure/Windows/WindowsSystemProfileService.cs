using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WindowsSystemProfileService : IWindowsSystemProfileService
{
    private readonly ICpuProvider _cpuProvider;
    private readonly IGpuProvider _gpuProvider;
    private readonly IMemoryProvider _memoryProvider;
    private readonly IOperatingSystemProvider _osProvider;
    private readonly IStorageProvider _storageProvider;
    private readonly IMotherboardProvider _motherboardProvider;
    private readonly IProcessProvider _processProvider;
    private readonly INetworkProvider _networkProvider;

    public WindowsSystemProfileService(
        ICpuProvider cpuProvider,
        IGpuProvider gpuProvider,
        IMemoryProvider memoryProvider,
        IOperatingSystemProvider osProvider,
        IStorageProvider storageProvider,
        IMotherboardProvider motherboardProvider,
        IProcessProvider processProvider,
        INetworkProvider networkProvider)
    {
        _cpuProvider = cpuProvider;
        _gpuProvider = gpuProvider;
        _memoryProvider = memoryProvider;
        _osProvider = osProvider;
        _storageProvider = storageProvider;
        _motherboardProvider = motherboardProvider;
        _processProvider = processProvider;
        _networkProvider = networkProvider;
    }

    public HardwareProfileDto GetLiveSystemProfile()
    {
        var storageDrives = _storageProvider.GetStorageDrives();
        var allDisks = _storageProvider.GetAllDisks();

        // In a full implementation, we'd map logical disks (AllDisks) to Physical Disks (StorageDrives) using WMI DiskDriveToDiskPartition.
        // For now we just return both datasets to the DTO.

        return new HardwareProfileDto
        {
            DeviceName = _osProvider.GetDeviceName(),
            OsVersion = _osProvider.GetOsVersion(),
            SystemAge = _osProvider.GetSystemAge(),
            ChipsetDriver = _motherboardProvider.GetChipsetDriver(),
            PrimaryStorageType = _storageProvider.GetPrimaryStorageType(),
            
            Cpu = _cpuProvider.GetCpuStats(),
            Gpu = _gpuProvider.GetGpuStats(),
            Ram = _memoryProvider.GetMemoryStats(),
            Network = _networkProvider.GetNetworkStats(),
            
            StorageDrives = storageDrives,
            AllDisks = allDisks,
            
            ProcessInsights = _processProvider.GetProcessInsights()
        };
    }
}
