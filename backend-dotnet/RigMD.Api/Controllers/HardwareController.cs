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

    [HttpGet("live")]
    public IActionResult GetLiveHardware()
    {
        try
        {
            var profile = _profileService.GetLiveSystemProfile();

            return Ok(new
            {
                device_name = profile.DeviceName,
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
                    status = d.IsFailingSmart ? "Warning / Failing S.M.A.R.T." : "Healthy / OK",
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
                    usage_percent = profile.Cpu.UsagePercent,
                    cores = profile.Cpu.Cores,
                    threads = profile.Cpu.Threads,
                    frequency_mhz = profile.Cpu.FrequencyMhz,
                    temperature_celsius = profile.Cpu.TemperatureCelsius
                },
                gpu = new
                {
                    name = profile.Gpu.Name,
                    driver = profile.Gpu.Driver,
                    type = profile.Gpu.Type,
                    vram_gb = profile.Gpu.VramGb,
                    temperature_celsius = profile.Gpu.TemperatureCelsius
                },
                ram = new
                {
                    total_gb = profile.Ram.TotalGb,
                    used_gb = profile.Ram.UsedGb,
                    usage_percent = profile.Ram.UsagePercent
                },
                device_type = profile.DeviceType,
                active_power_plan = profile.ActivePowerPlan,
                connected_displays = profile.ConnectedDisplays,
                battery = profile.Battery != null ? new
                {
                    is_charging = profile.Battery.IsCharging,
                    charge_percent = profile.Battery.ChargePercent,
                    health_status = profile.Battery.HealthStatus
                } : null,
                network = profile.Network != null && profile.Network.HasActiveAdapter ? new
                {
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
