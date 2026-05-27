"""
remediation_service.py
----------------------
Safe Guided Remediation service for RigMD.

This module only provides low-risk, user-confirmed actions:
- clear user temp folders only
- open Windows tools
- run read-only checks

It does not delete personal files, edit the registry, remove drivers,
repair system files, or touch protected Windows maintenance folders.
"""

import os
import shutil
import subprocess
from pathlib import Path


def _format_mb(bytes_count: int) -> str:
    return f"{round(bytes_count / (1024 * 1024), 2)} MB"


def _get_temp_locations() -> list[Path]:
    """
    Returns only user-level temp locations.

    For safe demo/testing, you may set:
    RIGMD_TEST_TEMP_DIR=C:\\RigMD_Test_Temp

    If that variable exists, RigMD clears only that test folder.
    """
    test_temp_dir = os.environ.get("RIGMD_TEST_TEMP_DIR")

    if test_temp_dir:
        return [Path(test_temp_dir)]

    raw_locations = [
        os.environ.get("TEMP"),
        os.environ.get("TMP"),
    ]

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


def clear_user_temp_files() -> dict:
    """
    Clears only user-level temp folders.

    This does NOT clear:
    - Documents
    - Desktop
    - Downloads
    - Pictures
    - Games
    - Program Files
    - System32
    - Windows Prefetch
    - Registry entries
    - Drivers
    """
    cleared_bytes = 0
    deleted_items = 0
    skipped_items = 0
    failed_errors = []

    locations = _get_temp_locations()

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

            except Exception as error:
                skipped_items += 1
                failed_errors.append(str(error))

    return {
        "action": "clear_user_temp_files",
        "success": True,
        "cleared": _format_mb(cleared_bytes),
        "cleared_bytes": cleared_bytes,
        "deleted_items": deleted_items,
        "skipped_errors": skipped_items,
        "summary": (
            f"{_format_mb(cleared_bytes)} of temporary files removed. "
            f"{skipped_items} locked or protected item(s) were safely skipped."
        ),
    }


def open_device_manager() -> dict:
    """
    Opens Windows Device Manager.

    This is an assisted action. RigMD does not change drivers automatically.
    """
    try:
        subprocess.Popen(["devmgmt.msc"], shell=True)

        return {
            "action": "open_device_manager",
            "success": True,
            "summary": "Device Manager opened. Check for devices with warning icons, then update or roll back only if needed.",
        }
    except Exception as error:
        return {
            "action": "open_device_manager",
            "success": False,
            "summary": str(error),
        }


def open_startup_apps() -> dict:
    """
    Opens Windows Startup Apps settings.

    This avoids registry editing. The user chooses what to disable.
    """
    try:
        subprocess.Popen(["explorer.exe", "ms-settings:startupapps"])

        return {
            "action": "open_startup_apps",
            "success": True,
            "summary": "Startup Apps settings opened. You can disable unnecessary apps from starting with Windows.",
        }
    except Exception as error:
        return {
            "action": "open_startup_apps",
            "success": False,
            "summary": str(error),
        }


def run_chkdsk_readonly() -> dict:
    """
    Runs chkdsk C: in read-only mode.
    No /f and no /r are used, so this scans and reports only.
    """
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
        }

    except subprocess.TimeoutExpired:
        return {
            "action": "chkdsk_readonly",
            "success": False,
            "summary": "Read-only disk scan timed out. The drive may be large, busy, or slow.",
        }

    except PermissionError:
        return {
            "action": "chkdsk_readonly",
            "success": False,
            "needs_admin": True,
            "summary": "Windows blocked the read-only disk scan because RigMD is not running as Administrator. No changes were made.",
        }

    except Exception as error:
        message = str(error)

        if "Access is denied" in message or "WinError 5" in message:
            return {
                "action": "chkdsk_readonly",
                "success": False,
                "needs_admin": True,
                "summary": "Windows blocked the read-only disk scan because RigMD is not running as Administrator. No changes were made.",
            }

        return {
            "action": "chkdsk_readonly",
            "success": False,
            "summary": message,
        }


def open_backup_settings() -> dict:
    """
    Opens Windows backup settings.

    RigMD does not create or modify backups automatically.
    """
    try:
        subprocess.Popen(["explorer.exe", "ms-settings:backup"])

        return {
            "action": "open_backup_settings",
            "success": True,
            "summary": "Windows backup settings opened. Back up important files before deeper storage troubleshooting.",
        }

    except Exception as error:
        return {
            "action": "open_backup_settings",
            "success": False,
            "summary": str(error),
        }


def show_gpu_reset_shortcut() -> dict:
    """
    Shows the safe Windows display reset shortcut.

    RigMD does not restart or remove the GPU driver automatically.
    """
    return {
        "action": "show_gpu_reset_shortcut",
        "success": True,
        "summary": (
            "To safely reset the display driver, press Windows + Ctrl + Shift + B. "
            "Your screen may blink briefly, then return."
        ),
    }


ACTION_REGISTRY = {
    "clear_user_temp_files": clear_user_temp_files,
    "open_device_manager": open_device_manager,
    "open_startup_apps": open_startup_apps,
    "chkdsk_readonly": run_chkdsk_readonly,
    "open_backup_settings": open_backup_settings,
    "show_gpu_reset_shortcut": show_gpu_reset_shortcut,
}


def get_available_actions(diagnosed_category: str) -> list:
    """
    Returns safe assisted actions for a diagnosed category.
    The frontend displays each returned action as a Fix button.
    """
    category = diagnosed_category.lower()

    if "os performance" in category:
        return [
            {
                "id": "clear_user_temp_files",
                "label": "Clear User Temp Files",
                "description": "Removes temporary files from the current user's TEMP and TMP folders to reduce software clutter.",
                "risk": "Low-risk maintenance. Personal folders and system files are not included.",
            },
            {
                "id": "open_startup_apps",
                "label": "Open Startup Apps",
                "description": "Opens Windows Startup Apps settings so you can disable unnecessary apps that slow startup.",
                "risk": "Assisted action. RigMD opens the settings page, but you choose what to disable.",
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
        ]

    if "driver conflict" in category:
        return [
            {
                "id": "open_device_manager",
                "label": "Open Device Manager",
                "description": "Opens Device Manager so you can inspect flagged devices and review driver status.",
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
        ]

    return []


def execute_action(action_id: str) -> dict:
    """
    Looks up and runs the requested action by ID.
    """
    fn = ACTION_REGISTRY.get(action_id)

    if not fn:
        return {
            "success": False,
            "summary": f"Unknown or unsupported action: {action_id}",
        }

    return fn()