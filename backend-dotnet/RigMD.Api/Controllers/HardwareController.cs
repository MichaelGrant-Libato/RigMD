using System.Linq;
using Microsoft.AspNetCore.Mvc;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/hardware")]
public class HardwareController : ControllerBase
{
    private readonly IWindowsSystemProfileService _profileService;

    public HardwareController(IWindowsSystemProfileService profileService)
    {
        _profileService = profileService;
    }

    private static double? Positive(double value) => double.IsFinite(value) && value > 0 ? value : null;

    [HttpGet("live")]
    public IActionResult GetLiveHardware()
    {
        try
        {
            var profile = _profileService.GetDeviceInventory();
            var knownVolumes = profile.AllDisks.Count > 0 && profile.AllDisks.All(volume =>
                double.IsFinite(volume.TotalGb) && volume.TotalGb > 0 && double.IsFinite(volume.UsedGb)
                && volume.UsedGb >= 0 && volume.UsedGb <= volume.TotalGb);
            var volumeTotal = knownVolumes ? profile.AllDisks.Sum(volume => volume.TotalGb) : 0;
            var volumeUsed = knownVolumes ? profile.AllDisks.Sum(volume => volume.UsedGb) : 0;

            return Ok(new
            {
                device_name = profile.DeviceName,
                inventory_captured_at_utc = DateTimeOffset.UtcNow,
                os_architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                os_version = profile.OsVersion,
                system_age = profile.SystemAge,
                chipset_driver = profile.ChipsetDriver,
                storage_type = profile.PrimaryStorageType,
                storage_drives = profile.StorageDrives.Select(d => new
                {
                    model = d.Model,
                    type = d.Type,
                    size_gb = d.SizeGb,
                    @interface = d.Interface,
                    disk_index = d.DiskIndex,
                    media_type = d.MediaType,
                    bus_type = d.BusType,
                    detection_source = d.DetectionSource,
                    is_failing_smart = d.IsFailingSmart,
                    status = d.IsFailingSmart ? "Storage warning reported" : null,
                    used_gb = d.UsedGb,
                    usage_percent = d.UsagePercent,
                    volumes = d.Volumes.Select(v => new
                    {
                        drive = v.Drive,
                        mountpoint = v.Mountpoint,
                        fstype = v.FsType,
                        disk_index = v.DiskIndex,
                        total_gb = v.TotalGb,
                        used_gb = v.UsedGb,
                        usage_percent = v.UsagePercent
                    })
                }),
                cpu = new
                {
                    name = profile.Cpu.Name,
                    usage_source = profile.Cpu.UsageSource,
                    usage_captured_at_utc = profile.Cpu.UsageCapturedAtUtc,
                    usage_window_seconds = profile.Cpu.UsageWindowSeconds,
                    usage_percent = profile.Cpu.UsageAvailable != false && double.IsFinite(profile.Cpu.UsagePercent) && profile.Cpu.UsagePercent is >= 0 and <= 100
                        ? (double?)profile.Cpu.UsagePercent : null,
                    cores = Positive(profile.Cpu.Cores),
                    threads = Positive(profile.Cpu.Threads),
                    frequency_mhz = profile.Cpu.FrequencyMhz,
                    max_frequency_mhz = Positive(profile.Cpu.MaxFrequencyMhz),
                    sockets = Positive(profile.Cpu.Sockets),
                    virtualization_enabled = profile.Cpu.VirtualizationEnabled,
                    l1_cache_kb = Positive(profile.Cpu.L1CacheKb),
                    l2_cache_mb = Positive(profile.Cpu.L2CacheMb),
                    l3_cache_mb = Positive(profile.Cpu.L3CacheMb),
                    temperature_celsius = profile.Cpu.TemperatureCelsius,
                    temperature_source = profile.Cpu.TemperatureSource,
                    temperature_sensor_identifier = profile.Cpu.TemperatureSensorIdentifier,
                    temperature_captured_at_utc = profile.Cpu.TemperatureCapturedAtUtc
                },
                gpu = new
                {
                    adapters = profile.Gpu.Adapters,
                    adapter_id = profile.Gpu.AdapterId,
                    name = profile.Gpu.Name,
                    driver = profile.Gpu.Driver,
                    driver_date = profile.Gpu.DriverDate,
                    physical_location = profile.Gpu.PhysicalLocation,
                    type = profile.Gpu.Type,
                    vram_gb = profile.Gpu.VramGb,
                    temperature_celsius = profile.Gpu.TemperatureCelsius,
                    temperature_source = profile.Gpu.TemperatureSource,
                    temperature_captured_at_utc = profile.Gpu.TemperatureCapturedAtUtc
                },
                ram = new
                {
                    total_gb = profile.Ram.TotalGb,
                    installed_gb = profile.Ram.InstalledGb,
                    available_gb = profile.Ram.AvailableGb,
                    speed_mtps = Positive(profile.Ram.SpeedMtps),
                    slots_used = profile.Ram.SlotsUsed is > 0 ? profile.Ram.SlotsUsed : null,
                    slots_total = profile.Ram.SlotsTotal is > 0 ? profile.Ram.SlotsTotal : null,
                    form_factor = profile.Ram.FormFactor,
                    hardware_reserved_mb = profile.Ram.HardwareReservedMb,
                    committed_gb = profile.Ram.CommittedGb,
                    commit_limit_gb = profile.Ram.CommitLimitGb,
                    used_gb = profile.Ram.UsageAvailable == false ? (double?)null : profile.Ram.UsedGb,
                    usage_percent = profile.Ram.UsageAvailable != false && double.IsFinite(profile.Ram.UsagePercent) && profile.Ram.UsagePercent is >= 0 and <= 100
                        ? (double?)profile.Ram.UsagePercent : null
                },
                device_type = profile.DeviceType,
                active_power_plan = profile.ActivePowerPlan,
                connected_displays = profile.ConnectedDisplays,
                volume_space = volumeTotal > 0 ? new { free_gb = volumeTotal - volumeUsed, usage_percent = 100 * volumeUsed / volumeTotal } : null,
                battery = profile.Battery is { HasBattery: true } ? new
                {
                    is_charging = profile.Battery.QuerySucceeded == false || profile.Battery.BatteryStatus is 0 or 10
                        ? (bool?)null : profile.Battery.IsCharging,
                    charge_percent = profile.Battery.ChargeAvailable == true ? (int?)profile.Battery.ChargePercent : null,
                    health_status = profile.Battery.HealthStatus
                } : null,
                network = profile.Network != null ? new
                {
                    query_succeeded = profile.Network.QuerySucceeded,
                    has_active_adapter = profile.Network.HasActiveAdapter,
                    adapter_id = profile.Network.AdapterId,
                    adapter_name = profile.Network.AdapterName,
                    has_ipv4_address = profile.Network.HasIpv4Address,
                    has_default_gateway = profile.Network.HasDefaultGateway,
                    has_dns_servers = profile.Network.HasDnsServers,
                    dns_resolution_succeeded = profile.Network.DnsResolutionSucceeded,
                    is_wifi = profile.Network.IsWifi,
                    wifi_signal_strength = profile.Network.WifiSignalStrength,
                    mac_address = profile.Network.MacAddress,
                    ip_address = profile.Network.IpAddress,
                    ping_latency_ms = profile.Network.PingLatencyMs,
                    packet_loss_percent = profile.Network.PacketLossPercent
                } : null,
                disk = new
                {
                    total_gb = profile.StorageDrives.Sum(d => d.SizeGb),
                    used_gb = profile.StorageDrives.Sum(d => d.UsedGb ?? 0),
                    usage_percent = profile.StorageDrives.Any()
                        ? Math.Round(profile.StorageDrives.Sum(d => d.UsedGb ?? 0) / Math.Max(profile.StorageDrives.Sum(d => d.SizeGb), 1) * 100, 1)
                        : 0
                },
                all_disks = profile.AllDisks.Select(d => new
                {
                    drive = d.Drive,
                    mountpoint = d.Mountpoint,
                    fstype = d.FsType,
                    disk_index = d.DiskIndex,
                    total_gb = d.TotalGb,
                    used_gb = d.UsedGb,
                    usage_percent = d.UsagePercent
                }),
                process_insights = new
                {
                    browser_detected = profile.ProcessInsights.BrowserDetected,
                    browser_process_count = profile.ProcessInsights.BrowserProcessCount,
                    browser_memory_mb = profile.ProcessInsights.BrowserMemoryMb,
                    browser_heavy = profile.ProcessInsights.BrowserHeavy,
                    game_detected = profile.ProcessInsights.GameDetected,
                    game_processes = profile.ProcessInsights.GameProcesses,
                    top_memory_apps = profile.ProcessInsights.TopMemoryApps.Select(a => new
                    {
                        name = a.Name,
                        process_count = a.ProcessCount,
                        memory_mb = a.MemoryMb
                    })
                },
                displays = profile.Displays.Select(d => new
                {
                    name = d.Name,
                    resolution = d.Resolution,
                    refresh_rate = d.RefreshRate
                }),
                device_errors = profile.DeviceErrors.Select(e => new
                {
                    name = e.Name,
                    device_id = e.DeviceId,
                    error_code = e.ErrorCode,
                    description = e.Description
                })
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("snapshot")]
    public IActionResult GetSnapshot()
    {
        return Ok(new
        {
            agentId = "local",
            capturedAt = DateTime.UtcNow,
            Hardware = _profileService.GetLiveSystemProfile()
        });
    }

    [HttpPost("refresh")]
    public IActionResult RefreshHardwareCache()
    {
        // The C# WMI providers don't use a cache in the same way;
        // each call to GetLiveSystemProfile() fetches fresh data.
        // This endpoint exists for frontend compatibility.
        return Ok(new { message = "Hardware cache refreshed successfully" });
    }
}
