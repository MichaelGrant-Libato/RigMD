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
    private readonly IBatteryProvider _batteryProvider;
    private readonly IDeviceTypeProvider _deviceTypeProvider;
    private readonly IPowerProvider _powerProvider;
    private readonly IDisplayProvider _displayProvider;

    public WindowsSystemProfileService(
        ICpuProvider cpuProvider,
        IGpuProvider gpuProvider,
        IMemoryProvider memoryProvider,
        IOperatingSystemProvider osProvider,
        IStorageProvider storageProvider,
        IMotherboardProvider motherboardProvider,
        IProcessProvider processProvider,
        INetworkProvider networkProvider,
        IBatteryProvider batteryProvider,
        IDeviceTypeProvider deviceTypeProvider,
        IPowerProvider powerProvider,
        IDisplayProvider displayProvider)
    {
        _cpuProvider = cpuProvider;
        _gpuProvider = gpuProvider;
        _memoryProvider = memoryProvider;
        _osProvider = osProvider;
        _storageProvider = storageProvider;
        _motherboardProvider = motherboardProvider;
        _processProvider = processProvider;
        _networkProvider = networkProvider;
        _batteryProvider = batteryProvider;
        _deviceTypeProvider = deviceTypeProvider;
        _powerProvider = powerProvider;
        _displayProvider = displayProvider;
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
            DeviceType = _deviceTypeProvider.GetDeviceType(),
            OsVersion = _osProvider.GetOsVersion(),
            SystemAge = _osProvider.GetSystemAge(),
            ChipsetDriver = _motherboardProvider.GetChipsetDriver(),
            PrimaryStorageType = _storageProvider.GetPrimaryStorageType(),
            ActivePowerPlan = _powerProvider.GetActivePowerPlan(),
            ConnectedDisplays = _displayProvider.GetConnectedDisplays(),
            Displays = _displayProvider.GetDisplays(),
            DeviceErrors = GetDeviceErrors(),
            
            Battery = _batteryProvider.GetBatteryStats(),
            
            Cpu = _cpuProvider.GetCpuStats(),
            Gpu = _gpuProvider.GetGpuStats(),
            Ram = _memoryProvider.GetMemoryStats(),
            Network = _networkProvider.GetNetworkStats(),
            
            StorageDrives = storageDrives,
            AllDisks = allDisks,
            
            ProcessInsights = _processProvider.GetProcessInsights()
        };
    }

    private List<DeviceErrorDto> GetDeviceErrors()
    {
        var errors = new List<DeviceErrorDto>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, DeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown Device";
                var deviceId = obj["DeviceID"]?.ToString() ?? "";
                var errorCode = obj["ConfigManagerErrorCode"] != null ? Convert.ToUInt32(obj["ConfigManagerErrorCode"]) : 0;
                var status = obj["Status"]?.ToString() ?? "";

                errors.Add(new DeviceErrorDto
                {
                    Name = name,
                    DeviceId = deviceId,
                    ErrorCode = errorCode,
                    Description = $"Device reported error code {errorCode}. Status: {status}"
                });
            }
        }
        catch
        {
            // Ignore
        }
        return errors;
    }
}
