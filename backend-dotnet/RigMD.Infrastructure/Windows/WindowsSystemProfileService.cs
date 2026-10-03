using System.Diagnostics;
using System.Xml.Linq;
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
    private readonly IHardwareMonitorService? _hardwareMonitor;

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
        IDisplayProvider displayProvider,
        IHardwareMonitorService? hardwareMonitor = null)
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
        _hardwareMonitor = hardwareMonitor;
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

        var cpuStats = _cpuProvider.GetCpuStats();
        var gpuStats = _gpuProvider.GetGpuStats();

        if (_hardwareMonitor != null)
        {
            try
            {
                _hardwareMonitor.Tick();
                cpuStats.TemperatureCelsius = _hardwareMonitor.GetCpuTemperature() ?? cpuStats.TemperatureCelsius;
                gpuStats.TemperatureCelsius = _hardwareMonitor.GetGpuTemperature() ?? gpuStats.TemperatureCelsius;

                var totalVramGb = _hardwareMonitor.GetGpuDedicatedMemoryTotalGb();
                if (totalVramGb.HasValue && totalVramGb.Value > gpuStats.VramGb)
                {
                    gpuStats.VramGb = Math.Round(totalVramGb.Value, 1);
                    gpuStats.DedicatedMemoryGb = gpuStats.VramGb;
                }
            }
            catch { }
        }

        var deviceType = _deviceTypeProvider.GetDeviceType();
        var battery = _batteryProvider.GetBatteryStats();
        var presence = BuildPresenceProbe(deviceType, battery, gpuStats);

        return new HardwareProfileDto
        {
            DeviceName = _osProvider.GetDeviceName(),
            DeviceType = deviceType,
            OsVersion = _osProvider.GetOsVersion(),
            SystemAge = _osProvider.GetSystemAge(),
            ChipsetDriver = _motherboardProvider.GetChipsetDriver(),
            PrimaryStorageType = _storageProvider.GetPrimaryStorageType(),
            ActivePowerPlan = _powerProvider.GetActivePowerPlan(),
            ConnectedDisplays = _displayProvider.GetConnectedDisplays(),
            Displays = _displayProvider.GetDisplays(),
            DeviceErrors = GetDeviceErrors(),
            
            Battery = battery,
            
            Cpu = cpuStats,
            Gpu = gpuStats,
            Ram = _memoryProvider.GetMemoryStats(),
            Network = _networkProvider.GetNetworkStats(),
            
            StorageDrives = storageDrives,
            AllDisks = allDisks,
            
            ProcessInsights = _processProvider.GetProcessInsights(),
            StabilityEvents = GetStabilityEvents(),
            Presence = presence
        };
    }

    public HardwarePresenceProbeDto GetHardwarePresenceProbe()
    {
        var deviceType = _deviceTypeProvider.GetDeviceType();
        var battery = _batteryProvider.GetBatteryStats();
        var gpu = _gpuProvider.GetGpuStats();
        return BuildPresenceProbe(deviceType, battery, gpu);
    }

    private static HardwarePresenceProbeDto BuildPresenceProbe(
        string deviceType,
        BatteryStatsDto? battery,
        GpuStatsDto gpu)
    {
        bool hasBattery = battery?.HasBattery == true;
        bool hasGpu = gpu.HasGpu && !string.Equals(gpu.Name, "Unknown GPU", StringComparison.OrdinalIgnoreCase);
        bool hasDedicatedGpu = gpu.HasDedicatedGpu || string.Equals(gpu.Type, "Dedicated", StringComparison.OrdinalIgnoreCase);

        var probe = new HardwarePresenceProbeDto
        {
            DeviceType = string.IsNullOrWhiteSpace(deviceType) ? (hasBattery ? "Laptop" : "Desktop") : deviceType,
            HasBattery = hasBattery,
            HasGpu = hasGpu,
            HasDedicatedGpu = hasDedicatedGpu,
            GpuName = gpu.Name,
            GpuType = gpu.Type
        };

        var alwaysPresentIds = new[] { "cpu", "memory", "storage", "os", "drivers", "network", "display" };
        foreach (var id in alwaysPresentIds)
        {
            probe.Components[id] = new ComponentPresenceInfoDto
            {
                ComponentId = id,
                Status = nameof(ComponentStatus.Present),
                IsSelectable = true
            };
        }

        probe.Components["gpu"] = new ComponentPresenceInfoDto
        {
            ComponentId = "gpu",
            Status = hasGpu ? nameof(ComponentStatus.Present) : nameof(ComponentStatus.NotPresent),
            IsSelectable = hasGpu,
            Badge = hasDedicatedGpu
                ? "Dedicated GPU"
                : hasGpu
                    ? "Integrated GPU"
                    : "Not Present",
            Reason = hasGpu ? null : "No graphics controller detected on this device"
        };

        probe.Components["battery"] = new ComponentPresenceInfoDto
        {
            ComponentId = "battery",
            Status = hasBattery ? nameof(ComponentStatus.Present) : nameof(ComponentStatus.NotPresent),
            IsSelectable = hasBattery,
            Badge = hasBattery ? "Battery Present" : "Not Present (Desktop PC)",
            Reason = hasBattery ? null : "No battery detected on this device"
        };

        return probe;
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

    private static StabilityEventSnapshotDto GetStabilityEvents()
    {
        const int hoursBack = 168;
        const int maxEvents = 40;

        var snapshot = new StabilityEventSnapshotDto
        {
            WindowDescription = "Last 7 days"
        };

        var timeDiffMs = (long)TimeSpan.FromHours(hoursBack).TotalMilliseconds;

        var applicationQuery =
            $"*[System[(EventID=1000 or EventID=1001 or EventID=1002 or EventID=1026 or Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {timeDiffMs}]]]";
        var systemQuery =
            $"*[System[(EventID=41 or EventID=1001 or EventID=6008 or Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {timeDiffMs}]]]";

        var warnings = new List<string>();

        snapshot.ApplicationCrashEvents = QueryRecentWindowsEvents("Application", applicationQuery, maxEvents, warnings)
            .Where(IsApplicationCrashEvent)
            .Take(12)
            .ToList();

        snapshot.SystemCrashEvents = QueryRecentWindowsEvents("System", systemQuery, maxEvents, warnings)
            .Where(IsSystemCrashEvent)
            .Take(12)
            .ToList();

        if (warnings.Count > 0)
        {
            snapshot.QueryWarning = string.Join(" | ", warnings.Distinct());
        }

        return snapshot;
    }

    private static List<WindowsEventSummaryDto> QueryRecentWindowsEvents(
        string logName,
        string xpathQuery,
        int maxEvents,
        List<string> warnings)
    {
        var events = new List<WindowsEventSummaryDto>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wevtutil.exe",
                Arguments = $"qe {logName} /c:{maxEvents} /rd:true /f:RenderedXml /q:\"{xpathQuery}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                warnings.Add($"Could not start Windows Event Log query for {logName}.");
                return events;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                TryKill(process);
                process.WaitForExit(1000);
                warnings.Add($"Windows Event Log query timed out for {logName}.");
                return events;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(stderr) &&
                !stderr.Contains("No events were found", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{logName}: {stderr.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                return events;
            }

            var wrappedXml = $"<Events>{stdout}</Events>";
            var doc = XDocument.Parse(wrappedXml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

            foreach (var ev in doc.Root?.Elements(ns + "Event") ?? Enumerable.Empty<XElement>())
            {
                var sys = ev.Element(ns + "System");
                var renderingInfo = ev.Element(ns + "RenderingInfo");

                var providerName = sys?.Element(ns + "Provider")?.Attribute("Name")?.Value ?? "Unknown";
                var eventIdText = sys?.Element(ns + "EventID")?.Value ?? "0";
                var levelCode = sys?.Element(ns + "Level")?.Value ?? "0";
                var timeCreatedText = sys?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value ?? string.Empty;
                var message = renderingInfo?.Element(ns + "Message")?.Value;

                if (string.IsNullOrWhiteSpace(message))
                {
                    var dataItems = ev.Element(ns + "EventData")?
                        .Elements(ns + "Data")
                        .Select(d => d.Value)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Take(6)
                        .ToList();

                    if (dataItems != null && dataItems.Count > 0)
                    {
                        message = string.Join(" | ", dataItems);
                    }
                }

                if (!string.IsNullOrWhiteSpace(message) && message.Length > 320)
                {
                    message = message[..320] + "...";
                }

                events.Add(new WindowsEventSummaryDto
                {
                    TimeUtc = DateTimeOffset.TryParse(timeCreatedText, out var parsedTime)
                        ? parsedTime.ToUniversalTime()
                        : null,
                    Level = levelCode switch
                    {
                        "1" => "Critical",
                        "2" => "Error",
                        "3" => "Warning",
                        _ => $"Level {levelCode}"
                    },
                    Provider = providerName,
                    EventId = int.TryParse(eventIdText, out var parsedEventId) ? parsedEventId : 0,
                    Message = message ?? "(No rendered message)"
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"{logName}: {ex.Message}");
        }

        return events;
    }

    private static bool IsApplicationCrashEvent(WindowsEventSummaryDto ev)
    {
        var provider = ev.Provider ?? string.Empty;
        var message = ev.Message ?? string.Empty;

        return ev.EventId is 1000 or 1001 or 1002 or 1026 ||
               provider.Contains("Application Error", StringComparison.OrdinalIgnoreCase) ||
               provider.Contains("Windows Error Reporting", StringComparison.OrdinalIgnoreCase) ||
               provider.Contains(".NET Runtime", StringComparison.OrdinalIgnoreCase) ||
               provider.Contains("Application Hang", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("stopped working", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("faulting application", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("application hang", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("appcrash", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystemCrashEvent(WindowsEventSummaryDto ev)
    {
        var provider = ev.Provider ?? string.Empty;
        var message = ev.Message ?? string.Empty;

        return ev.EventId is 41 or 1001 or 6008 ||
               provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase) ||
               provider.Contains("BugCheck", StringComparison.OrdinalIgnoreCase) ||
               provider.Contains("EventLog", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("bugcheck", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unexpected shutdown", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
