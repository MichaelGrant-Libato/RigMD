import os
import shutil
import subprocess
from pathlib import Path


def _format_mb(bytes_count: int) -> str:
    return f"{round(bytes_count / (1024 * 1024), 2)} MB"


def _get_temp_locations() -> list[Path]:
    test_temp_dir = os.environ.get("RIGMD_TEST_TEMP_DIR")
    if test_temp_dir:
        return [Path(test_temp_dir)]

    raw_locations = [os.environ.get("TEMP"), os.environ.get("TMP")]
    locations: list[Path] = []
    seen = set()

    for raw_location in raw_locations:
        if not raw_location:
            continue

        path = Path(raw_location).expanduser()

        try:
            resolved_key = str(path.resolve()).lower()
        except Exception:
            resolved_key = str(path).lower()

        if resolved_key not in seen:
            locations.append(path)
            seen.add(resolved_key)

    return locations


def _safe_file_size(path: Path) -> int:
    try:
        if path.is_file():
            return path.stat().st_size

        if path.is_dir():
            total = 0
            for child in path.rglob("*"):
                try:
                    if child.is_file() and not child.is_symlink():
                        total += child.stat().st_size
                except Exception:
                    continue
            return total
    except Exception:
        return 0

    return 0


def _get_drive_usage_percent(path: Path) -> float | None:
    try:
        usage = shutil.disk_usage(path)
        return round((usage.used / usage.total) * 100, 2)
    except Exception:
        return None


def _format_percent(value: float | None) -> str:
    return f"{value}% full" if value is not None else "Unavailable"


def _proof(label: str, status: str, meaning: str, before=None, after=None, change=None) -> dict:
    item = {
        "label": label,
        "status": status,
        "meaning": meaning,
    }

    if before is not None:
        item["before"] = before
    if after is not None:
        item["after"] = after
    if change is not None:
        item["change"] = change

    return item


def _open(command: list[str], action: str, summary: str, tool_name: str) -> dict:
    try:
        subprocess.Popen(command, shell=False)

        return {
            "action": action,
            "success": True,
            "summary": summary,
            "proof": [
                _proof(
                    label="Windows tool opened",
                    status="completed",
                    meaning=f"RigMD opened {tool_name}. No files, drivers, apps, or settings were changed automatically.",
                    after=tool_name,
                )
            ],
        }
    except Exception as error:
        return {
            "action": action,
            "success": False,
            "summary": str(error),
            "proof": [
                _proof(
                    label="Windows tool opened",
                    status="not completed",
                    meaning=f"RigMD could not open {tool_name}. No system changes were made.",
                    after="Not opened",
                )
            ],
        }


def clear_user_temp_files() -> dict:
    cleared_bytes = 0
    deleted_items = 0
    skipped_items = 0

    locations = _get_temp_locations()
    measure_path = locations[0] if locations else Path.home()
    before_usage = _get_drive_usage_percent(measure_path)

    scanned_locations = [str(location) for location in locations]

    for location in locations:
        if not location.exists() or not location.is_dir():
            continue

        for item in location.iterdir():
            try:
                if item.is_symlink():
                    skipped_items += 1
                    continue

                item_size = _safe_file_size(item)

                if item.is_file():
                    item.unlink(missing_ok=True)
                    cleared_bytes += item_size
                    deleted_items += 1
                elif item.is_dir():
                    shutil.rmtree(item)
                    cleared_bytes += item_size
                    deleted_items += 1

            except Exception:
                skipped_items += 1

    after_usage = _get_drive_usage_percent(measure_path)
    cleared_text = _format_mb(cleared_bytes)

    return {
        "action": "clear_user_temp_files",
        "success": True,
        "cleared": cleared_text,
        "cleared_bytes": cleared_bytes,
        "deleted_items": deleted_items,
        "skipped_errors": skipped_items,
        "scanned_locations": scanned_locations,
        "summary": (
            f"{cleared_text} of temporary files removed. "
            f"{skipped_items} locked or protected item(s) were safely skipped."
        ),
        "proof": [
            _proof(
                label="Drive usage",
                before=_format_percent(before_usage),
                after=_format_percent(after_usage),
                change=f"{cleared_text} cleared",
                status="changed" if cleared_bytes > 0 else "no measurable change",
                meaning="This compares drive usage before and after clearing temporary files.",
            ),
            _proof(
                label="Temporary files removed",
                before="Temporary folders scanned",
                after=f"{deleted_items} item(s) removed",
                change=cleared_text,
                status="changed" if deleted_items > 0 else "no removable files found",
                meaning="Only files inside the approved user TEMP/TMP cleanup scope were removed.",
            ),
            _proof(
                label="Locked files skipped",
                after=skipped_items,
                status="safe skip",
                meaning="Windows was using these files, so RigMD left them untouched.",
            ),
        ],
    }


def open_device_manager() -> dict:
    return _open(
        ["devmgmt.msc"],
        "open_device_manager",
        "Device Manager opened. Look for devices with warning icons. RigMD did not change any driver.",
        "Device Manager",
    )


def open_startup_apps() -> dict:
    return _open(
        ["explorer.exe", "ms-settings:startupapps"],
        "open_startup_apps",
        "Startup Apps settings opened. You can disable apps you do not need at startup.",
        "Startup Apps settings",
    )


def open_task_manager() -> dict:
    return _open(
        ["taskmgr.exe"],
        "open_task_manager",
        "Task Manager opened. Check the Processes tab for apps using high CPU, memory, or disk.",
        "Task Manager",
    )


def open_storage_settings() -> dict:
    return _open(
        ["explorer.exe", "ms-settings:storagesense"],
        "open_storage_settings",
        "Storage settings opened. Review free space and temporary files before deeper storage checks.",
        "Storage settings",
    )


def open_power_settings() -> dict:
    return _open(
        ["explorer.exe", "ms-settings:powersleep"],
        "open_power_settings",
        "Power and sleep settings opened. Review power behavior without changing hardware settings.",
        "Power and sleep settings",
    )


def open_reliability_monitor() -> dict:
    return _open(
        ["perfmon.exe", "/rel"],
        "open_reliability_monitor",
        "Reliability Monitor opened. Review recent crashes or Windows errors around the time the issue happened.",
        "Reliability Monitor",
    )


def open_event_viewer() -> dict:
    return _open(
        ["eventvwr.msc"],
        "open_event_viewer",
        "Event Viewer opened. Review recent System or Application errors carefully.",
        "Event Viewer",
    )


def open_backup_settings() -> dict:
    return _open(
        ["explorer.exe", "ms-settings:backup"],
        "open_backup_settings",
        "Windows backup settings opened. Back up important files before deeper storage troubleshooting.",
        "Backup settings",
    )


def run_chkdsk_readonly() -> dict:
    try:
        result = subprocess.run(
            ["chkdsk", "C:"],
            capture_output=True,
            text=True,
            timeout=120,
        )

        output = result.stdout.strip() or result.stderr.strip()
        output_lower = output.lower()

        has_errors = (
            "errors found" in output_lower
            or "corrupt" in output_lower
            or "problems" in output_lower
            or "failed" in output_lower
        )

        return {
            "action": "chkdsk_readonly",
            "success": True,
            "has_errors": has_errors,
            "output": output,
            "summary": (
                "Read-only disk scan completed. Possible file system issues were detected. Back up important files before running any repair."
                if has_errors
                else "Read-only disk scan completed. No obvious file system errors were detected."
            ),
            "proof": [
                _proof(
                    label="Disk scan mode",
                    status="read-only",
                    meaning="RigMD ran chkdsk without /f or /r repair flags, so it did not repair, move, or delete files.",
                    after="Read-only scan",
                ),
                _proof(
                    label="Scan result",
                    status="issues found" if has_errors else "no obvious errors",
                    meaning=(
                        "Windows reported possible file system issues. Back up important files before any repair command."
                        if has_errors
                        else "Windows did not report obvious file system errors in this read-only scan."
                    ),
                    after="Issues found" if has_errors else "No obvious errors",
                ),
            ],
        }

    except subprocess.TimeoutExpired:
        return {
            "action": "chkdsk_readonly",
            "success": False,
            "summary": "Read-only disk scan timed out. The drive may be large, busy, or slow.",
            "proof": [
                _proof(
                    label="Disk scan",
                    status="timed out",
                    meaning="Windows did not finish the read-only scan in time. No repair flags were used.",
                    after="Timed out",
                )
            ],
        }

    except PermissionError:
        return _chkdsk_blocked_by_admin()

    except Exception as error:
        message = str(error)

        if "Access is denied" in message or "WinError 5" in message:
            return _chkdsk_blocked_by_admin()

        return {
            "action": "chkdsk_readonly",
            "success": False,
            "summary": message,
            "proof": [
                _proof(
                    label="Disk scan",
                    status="not completed",
                    meaning="Windows did not complete the read-only scan. No changes were made by RigMD.",
                    after="Not completed",
                )
            ],
        }


def _chkdsk_blocked_by_admin() -> dict:
    return {
        "action": "chkdsk_readonly",
        "success": False,
        "needs_admin": True,
        "summary": "Windows needs administrator permission to run this disk check. Nothing was changed.",
        "proof": [
            _proof(
                label="Administrator permission",
                status="blocked",
                meaning="Windows blocked the read-only disk scan because RigMD is not running as Administrator. No repair, deletion, or file movement happened.",
                after="Required",
            ),
            _proof(
                label="Disk scan mode",
                status="read-only not started",
                meaning="The scan did not run, and no repair flags were used.",
                after="No changes made",
            ),
        ],
    }


def show_gpu_reset_shortcut() -> dict:
    return {
        "action": "show_gpu_reset_shortcut",
        "success": True,
        "summary": (
            "To safely reset the display driver, press Windows + Ctrl + Shift + B. "
            "Your screen may blink briefly, then return."
        ),
        "proof": [
            _proof(
                label="Instruction shown",
                status="completed",
                meaning="RigMD only showed the Windows display reset shortcut. It did not change the display driver.",
                after="Shortcut displayed",
            )
        ],
    }


ACTION_REGISTRY = {
    "clear_user_temp_files": clear_user_temp_files,
    "open_device_manager": open_device_manager,
    "open_startup_apps": open_startup_apps,
    "open_task_manager": open_task_manager,
    "open_storage_settings": open_storage_settings,
    "open_power_settings": open_power_settings,
    "open_reliability_monitor": open_reliability_monitor,
    "open_event_viewer": open_event_viewer,
    "open_backup_settings": open_backup_settings,
    "chkdsk_readonly": run_chkdsk_readonly,
    "show_gpu_reset_shortcut": show_gpu_reset_shortcut,
}


TARGET_REGISTRY = {
    "device_manager": open_device_manager,
    "startup_apps": open_startup_apps,
    "task_manager": open_task_manager,
    "storage_settings": open_storage_settings,
    "power_settings": open_power_settings,
    "reliability_monitor": open_reliability_monitor,
    "event_viewer": open_event_viewer,
    "backup_settings": open_backup_settings,
    "disk_scan": run_chkdsk_readonly,
}


def get_available_actions(diagnosed_category: str) -> list:
    category = diagnosed_category.lower()

    if "no active issue" in category:
        return []

    if "os performance" in category:
        return [
            {
                "id": "open_task_manager",
                "label": "Open Task Manager",
                "description": "Opens Task Manager so you can see which apps are using the most CPU, memory, or disk.",
                "risk": "Assisted action. RigMD only opens the Windows tool.",
            },
            {
                "id": "clear_user_temp_files",
                "label": "Clear User Temp Files",
                "description": "Removes temporary files from the current user's TEMP and TMP folders.",
                "risk": "Automated cleanup. Personal folders and system files are not included.",
            },
            {
                "id": "open_startup_apps",
                "label": "Open Startup Apps",
                "description": "Opens Windows Startup Apps settings so you can disable unnecessary startup apps.",
                "risk": "Assisted action. RigMD opens the settings page, but you choose what to disable.",
            },
        ]

    if "thermal" in category:
        return [
            {
                "id": "open_task_manager",
                "label": "Open Task Manager",
                "description": "Opens Task Manager so you can inspect heavy apps before the PC gets hotter.",
                "risk": "Assisted action. RigMD does not close apps automatically.",
            },
            {
                "id": "open_power_settings",
                "label": "Open Power Settings",
                "description": "Opens Windows power settings so you can review sleep and power behavior.",
                "risk": "Assisted action. RigMD does not change power settings automatically.",
            },
        ]

    if "boot" in category or "startup" in category:
        return [
            {
                "id": "open_startup_apps",
                "label": "Open Startup Apps",
                "description": "Opens Windows Startup Apps settings to review programs that run when Windows starts.",
                "risk": "Assisted action. No registry edits are made by RigMD.",
            },
            {
                "id": "open_reliability_monitor",
                "label": "Open Reliability Monitor",
                "description": "Opens Windows Reliability Monitor to review startup crashes or failures.",
                "risk": "Read-only inspection. RigMD does not change Windows logs.",
            },
        ]

    if "driver conflict" in category:
        return [
            {
                "id": "open_device_manager",
                "label": "Open Device Manager",
                "description": "Opens Device Manager so you can inspect flagged devices and driver status.",
                "risk": "Assisted action. RigMD does not update, remove, or roll back drivers automatically.",
            },
        ]

    if "storage health" in category:
        return [
            {
                "id": "open_backup_settings",
                "label": "Open Backup Settings",
                "description": "Opens Windows backup settings before deeper storage troubleshooting.",
                "risk": "Assisted action. Nothing is backed up or changed without your confirmation.",
            },
            {
                "id": "open_storage_settings",
                "label": "Open Storage Settings",
                "description": "Opens Windows Storage settings so you can review disk space.",
                "risk": "Assisted action. RigMD does not delete files from this screen.",
            },
            {
                "id": "chkdsk_readonly",
                "label": "Run Read-Only Disk Scan",
                "description": "Runs Windows Disk Check in read-only mode to scan the C: drive without making repairs.",
                "risk": "Read-only check. No repair flags are used.",
            },
        ]

    if "display driver" in category:
        return [
            {
                "id": "show_gpu_reset_shortcut",
                "label": "Show Display Driver Reset Shortcut",
                "description": "Shows the built-in Windows shortcut for safely resetting the display driver.",
                "risk": "Instruction-only action. RigMD does not change the driver.",
            },
            {
                "id": "open_device_manager",
                "label": "Open Device Manager",
                "description": "Opens Device Manager so you can inspect the display adapter driver.",
                "risk": "Assisted action. RigMD does not update or remove drivers automatically.",
            },
        ]

    return [
        {
            "id": "open_reliability_monitor",
            "label": "Open Reliability Monitor",
            "description": "Opens Windows Reliability Monitor to review recent errors.",
            "risk": "Read-only inspection. RigMD does not change Windows logs.",
        },
    ]


def execute_action(action_id: str) -> dict:
    fn = ACTION_REGISTRY.get(action_id)

    if not fn:
        return {
            "success": False,
            "summary": f"Unknown or unsupported action: {action_id}",
            "proof": [
                _proof(
                    label="Action lookup",
                    status="unsupported",
                    meaning="RigMD did not run anything because this action is not registered.",
                    after=action_id,
                )
            ],
        }

    return fn()


def open_target(target: str) -> dict:
    fn = TARGET_REGISTRY.get(str(target or "").lower())

    if not fn:
        return {
            "success": False,
            "summary": f"Unknown or unsupported verification target: {target}",
            "proof": [
                _proof(
                    label="Target lookup",
                    status="unsupported",
                    meaning="RigMD did not open anything because this verification target is not registered.",
                    after=target,
                )
            ],
        }

    return fn()