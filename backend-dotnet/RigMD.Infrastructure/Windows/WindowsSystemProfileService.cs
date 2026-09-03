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

        foreach (var drive in storageDrives)
        {
            if (drive.DiskIndex.HasValue)
            {
                drive.Volumes = allDisks.Where(d => d.DiskIndex == drive.DiskIndex.Value).ToList();
                
                if (drive.Volumes.Any())
                {
                    double totalGb = 0;
                    double usedGb = 0;
                    foreach (var v in drive.Volumes)
                    {
                        totalGb += v.TotalGb;
                        usedGb += v.UsedGb;
                    }
                    
                    drive.UsedGb = Math.Round(usedGb, 2);
                    if (totalGb > 0)
                    {
                        drive.UsagePercent = Math.Round((usedGb / totalGb) * 100, 1);
                    }
                }
            }
        }

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
