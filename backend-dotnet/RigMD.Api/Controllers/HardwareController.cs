using System;
using System.Collections.Generic;
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
                    frequency_mhz = profile.Cpu.FrequencyMhz
                },
                gpu = new
                {
                    name = profile.Gpu.Name,
                    driver = profile.Gpu.Driver,
                    type = profile.Gpu.Type,
                    vram_gb = profile.Gpu.VramGb
                },
                ram = new
                {
                    total_gb = profile.Ram.TotalGb,
                    used_gb = profile.Ram.UsedGb,
                    usage_percent = profile.Ram.UsagePercent
                },
                disk = new
                {
                    total_gb = profile.AllDisks.Sum(d => d.TotalGb),
                    used_gb = profile.AllDisks.Sum(d => d.UsedGb),
                    usage_percent = profile.AllDisks.Any()
                        ? Math.Round(profile.AllDisks.Sum(d => d.UsedGb) / Math.Max(profile.AllDisks.Sum(d => d.TotalGb), 1) * 100, 1)
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
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
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
