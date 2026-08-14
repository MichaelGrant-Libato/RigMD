using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/remediation")]
public class RemediationController : ControllerBase
{
    private static readonly Dictionary<string, List<object>> ActionsByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OS performance degradation"] = new List<object>
        {
            new { id = "open_task_manager", label = "Open Task Manager", description = "Opens Task Manager so you can see which apps are using the most CPU, memory, or disk.", risk = "Assisted action. RigMD only opens the Windows tool." },
            new { id = "clear_user_temp_files", label = "Clear User Temp Files", description = "Removes temporary files from the current user's TEMP and TMP folders.", risk = "Automated cleanup. Personal folders and system files are not included." },
            new { id = "open_startup_apps", label = "Open Startup Apps", description = "Opens Windows Startup Apps settings so you can disable unnecessary startup apps.", risk = "Assisted action. RigMD opens the settings page, but you choose what to disable." }
        },
        ["Thermal condition"] = new List<object>
        {
            new { id = "open_task_manager", label = "Open Task Manager", description = "Opens Task Manager so you can inspect heavy apps before the PC gets hotter.", risk = "Assisted action. RigMD does not close apps automatically." },
            new { id = "open_power_settings", label = "Open Power Settings", description = "Opens Windows power settings so you can review sleep and power behavior.", risk = "Assisted action. RigMD does not change power settings automatically." }
        },
        ["Boot and startup failure"] = new List<object>
        {
            new { id = "open_startup_apps", label = "Open Startup Apps", description = "Opens Windows Startup Apps settings to review programs that run when Windows starts.", risk = "Assisted action. No registry edits are made by RigMD." },
            new { id = "open_reliability_monitor", label = "Open Reliability Monitor", description = "Opens Windows Reliability Monitor to review startup crashes or failures.", risk = "Read-only inspection. RigMD does not change Windows logs." }
        },
        ["Driver conflict"] = new List<object>
        {
            new { id = "open_device_manager", label = "Open Device Manager", description = "Opens Device Manager so you can inspect flagged devices and driver status.", risk = "Assisted action. RigMD does not update, remove, or roll back drivers automatically." }
        },
        ["Storage health behavior"] = new List<object>
        {
            new { id = "open_backup_settings", label = "Open Backup Settings", description = "Opens Windows backup settings before deeper storage troubleshooting.", risk = "Assisted action. Nothing is backed up or changed without your confirmation." },
            new { id = "open_storage_settings", label = "Open Storage Settings", description = "Opens Windows Storage settings so you can review disk space.", risk = "Assisted action. RigMD does not delete files from this screen." },
            new { id = "chkdsk_readonly", label = "Run Read-Only Disk Scan", description = "Runs Windows Disk Check in read-only mode to scan the C: drive without making repairs.", risk = "Read-only check. No repair flags are used." }
        },
        ["Display driver behavior"] = new List<object>
        {
            new { id = "show_gpu_reset_shortcut", label = "Show Display Driver Reset Shortcut", description = "Shows the built-in Windows shortcut for safely resetting the display driver.", risk = "Instruction-only action. RigMD does not change the driver." },
            new { id = "open_device_manager", label = "Open Device Manager", description = "Opens Device Manager so you can inspect the display adapter driver.", risk = "Assisted action. RigMD does not update or remove drivers automatically." }
        }
    };

    [HttpGet("actions")]
    public IActionResult GetActions([FromQuery] string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return BadRequest(new { detail = "category is required" });

        if (category.Contains("no active issue", StringComparison.OrdinalIgnoreCase))
            return Ok(new { category, actions = Array.Empty<object>() });

        // Find the best matching category (partial match)
        List<object>? actions = null;
        foreach (var key in ActionsByCategory.Keys)
        {
            if (category.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                actions = ActionsByCategory[key];
                break;
            }
        }

        actions ??= new List<object>
        {
            new { id = "open_reliability_monitor", label = "Open Reliability Monitor", description = "Opens Windows Reliability Monitor to review recent errors.", risk = "Read-only inspection. RigMD does not change Windows logs." }
        };

        return Ok(new { category, actions });
    }

    [HttpPost("execute")]
    public IActionResult ExecuteAction([FromBody] ExecutePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.action_id))
            return BadRequest(new { detail = "action_id is required" });

        try
        {
            return Ok(RunAction(payload.action_id));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, summary = ex.Message });
        }
    }

    [HttpPost("open-target")]
    public IActionResult OpenTarget([FromBody] OpenTargetPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.target))
            return BadRequest(new { detail = "target is required" });

        try
        {
            return Ok(RunOpenTarget(payload.target));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, summary = ex.Message });
        }
    }

    private static object RunAction(string actionId)
    {
        return actionId switch
        {
            "open_task_manager" => OpenProcess("taskmgr.exe", "open_task_manager", "Task Manager was opened. Review which processes are using the most resources.", "Task Manager"),
            "open_device_manager" => OpenProcess("devmgmt.msc", "open_device_manager", "Device Manager was opened. Look for any devices with warning flags.", "Device Manager"),
            "open_startup_apps" => OpenProcess("ms-settings:startupapps", "open_startup_apps", "Startup Apps settings were opened. Review which apps launch when Windows starts.", "Startup Apps"),
            "open_power_settings" => OpenProcess("ms-settings:powersleep", "open_power_settings", "Power & Sleep settings were opened.", "Power Settings"),
            "open_reliability_monitor" => OpenProcess("perfmon /rel", "open_reliability_monitor", "Reliability Monitor was opened. Review recent system failures.", "Reliability Monitor"),
            "open_backup_settings" => OpenProcess("ms-settings:backup", "open_backup_settings", "Windows Backup settings were opened.", "Backup Settings"),
            "open_storage_settings" => OpenProcess("ms-settings:storagesense", "open_storage_settings", "Windows Storage settings were opened.", "Storage Settings"),
            "show_gpu_reset_shortcut" => new
            {
                action = "show_gpu_reset_shortcut",
                success = true,
                summary = "Press Win+Ctrl+Shift+B to safely reset the display driver without rebooting.",
                proof = new[] { new { label = "Shortcut shown", status = "completed", meaning = "This is a built-in Windows shortcut that resets the GPU driver without changing files or settings." } }
            },
            "chkdsk_readonly" => RunChkdskReadOnly(),
            "clear_user_temp_files" => ClearUserTempFiles(),
            _ => new
            {
                action = actionId,
                success = false,
                summary = $"Unknown or unsupported action: {actionId}",
                proof = new[] { new { label = "Action lookup", status = "unsupported", meaning = "RigMD did not run anything because this action is not registered." } }
            }
        };
    }

    private static object RunOpenTarget(string target)
    {
        return target.ToLower() switch
        {
            "task manager" => OpenProcess("taskmgr.exe", "open_task_manager", "Task Manager opened for verification.", "Task Manager"),
            "device manager" => OpenProcess("devmgmt.msc", "open_device_manager", "Device Manager opened for verification.", "Device Manager"),
            "startup apps" => OpenProcess("ms-settings:startupapps", "open_startup_apps", "Startup Apps opened for verification.", "Startup Apps"),
            "reliability monitor" => OpenProcess("perfmon /rel", "open_reliability_monitor", "Reliability Monitor opened for verification.", "Reliability Monitor"),
            "storage settings" => OpenProcess("ms-settings:storagesense", "open_storage_settings", "Storage settings opened for verification.", "Storage Settings"),
            _ => new
            {
                success = false,
                summary = $"Unknown or unsupported verification target: {target}",
                proof = new[] { new { label = "Target lookup", status = "unsupported", meaning = "RigMD did not open anything because this verification target is not registered." } }
            }
        };
    }

    private static object OpenProcess(string command, string action, string summary, string toolName)
    {
        try
        {
            var parts = command.Split(' ', 2);
            var psi = new ProcessStartInfo
            {
                FileName = parts[0],
                Arguments = parts.Length > 1 ? parts[1] : "",
                UseShellExecute = true
            };
            Process.Start(psi);

            return new
            {
                action,
                success = true,
                summary,
                proof = new[] { new { label = "Windows tool opened", status = "completed", meaning = $"RigMD opened {toolName}. No files, drivers, apps, or settings were changed automatically.", after = toolName } }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                action,
                success = false,
                summary = ex.Message,
                proof = new[] { new { label = "Windows tool opened", status = "not completed", meaning = $"RigMD could not open {toolName}. No system changes were made.", after = "Not opened" } }
            };
        }
    }

    private static object RunChkdskReadOnly()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c chkdsk C: /scan",
                UseShellExecute = true,
                Verb = "runas" // request elevation
            };
            Process.Start(psi);

            return new
            {
                action = "chkdsk_readonly",
                success = true,
                summary = "Disk scan started in a new window. This is a read-only check — no repairs will be made.",
                proof = new[] { new { label = "Disk scan", status = "started", meaning = "chkdsk /scan runs in read-only mode. Review the results in the opened command window." } }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                action = "chkdsk_readonly",
                success = false,
                summary = $"Could not start disk scan: {ex.Message}",
                proof = new[] { new { label = "Disk scan", status = "not started", meaning = "RigMD could not launch chkdsk. Try running it manually from an Administrator command prompt." } }
            };
        }
    }

    private static object ClearUserTempFiles()
    {
        var tempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
        long clearedBytes = 0;
        int deleted = 0;
        int skipped = 0;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(tempDir))
            {
                try
                {
                    var size = new FileInfo(filePath).Length;
                    System.IO.File.Delete(filePath);
                    clearedBytes += size;
                    deleted++;
                }
                catch { skipped++; }
            }
            foreach (var dir in Directory.EnumerateDirectories(tempDir))
            {
                try
                {
                    var size = GetDirSize(dir);
                    Directory.Delete(dir, true);
                    clearedBytes += size;
                    deleted++;
                }
                catch { skipped++; }
            }
        }
        catch (Exception ex)
        {
            return new { action = "clear_user_temp_files", success = false, summary = ex.Message };
        }

        var mb = Math.Round(clearedBytes / 1024.0 / 1024.0, 2);
        var cleared = $"{mb} MB";
        return new
        {
            action = "clear_user_temp_files",
            success = true,
            cleared,
            cleared_bytes = clearedBytes,
            deleted_items = deleted,
            skipped_errors = skipped,
            summary = $"{cleared} of temporary files removed. {skipped} locked or protected item(s) were safely skipped.",
            proof = new[]
            {
                new { label = "Temporary files removed", status = deleted > 0 ? "changed" : "no removable files found", meaning = "Only files inside the user TEMP folder were removed.", after = $"{deleted} item(s) removed" }
            }
        };
    }

    private static long GetDirSize(string path)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }
}

public class ExecutePayload
{
    public string action_id { get; set; } = string.Empty;
}

public class OpenTargetPayload
{
    public string target { get; set; } = string.Empty;
}
