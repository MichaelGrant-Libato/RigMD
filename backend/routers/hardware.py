from fastapi import APIRouter
import psutil
import platform
from datetime import datetime, timedelta
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

try:
    import wmi
    import pythoncom

    has_wmi = True
    logger.info("WMI module loaded successfully")
except ImportError:
    has_wmi = False
    logger.warning("WMI module not available")

router = APIRouter(prefix="/api/hardware", tags=["Hardware"])

_static_cache = None
_static_cache_time = None
CACHE_DURATION_SECONDS = 60


BROWSER_PROCESS_NAMES = {
    "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe",
    "opera.exe", "opera_gx.exe", "vivaldi.exe",
}

GAME_PROCESS_HINTS = {
    "valorant", "roblox", "genshin", "starrail", "fortnite",
    "cs2", "dota2", "league", "minecraft", "steam.exe",
    "epicgameslauncher.exe", "riotclientservices.exe",
}


def get_process_insights():
    process_totals = {}
    browser_memory_mb = 0
    browser_process_count = 0
    game_processes = []

    for proc in psutil.process_iter(["pid", "name", "memory_info"]):
        try:
            name = (proc.info.get("name") or "unknown").lower()
            memory_info = proc.info.get("memory_info")
            memory_mb = round((getattr(memory_info, "rss", 0) or 0) / (1024 * 1024), 2)

            if memory_mb <= 0:
                continue

            if name not in process_totals:
                process_totals[name] = {
                    "name": name,
                    "process_count": 0,
                    "memory_mb": 0,
                }

            process_totals[name]["process_count"] += 1
            process_totals[name]["memory_mb"] += memory_mb

            if name in BROWSER_PROCESS_NAMES:
                browser_memory_mb += memory_mb
                browser_process_count += 1

            if name in GAME_PROCESS_HINTS or any(hint in name for hint in GAME_PROCESS_HINTS):
                if name not in game_processes:
                    game_processes.append(name)

        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
        except Exception:
            continue

    top_memory_apps = sorted(
        process_totals.values(),
        key=lambda app: app["memory_mb"],
        reverse=True,
    )[:8]

    for app in top_memory_apps:
        app["memory_mb"] = round(app["memory_mb"], 2)

    browser_memory_mb = round(browser_memory_mb, 2)

    return {
        "browser_detected": browser_process_count > 0,
        "browser_process_count": browser_process_count,
        "browser_memory_mb": browser_memory_mb,
        "browser_heavy": browser_process_count >= 8 or browser_memory_mb >= 1024,
        "game_detected": len(game_processes) > 0,
        "game_processes": game_processes[:5],
        "top_memory_apps": top_memory_apps,
    }


def default_static_hardware():
    return {
        "device_name": platform.node() or "Unknown PC",
        "cpu_name": platform.processor() or "Unknown CPU",
        "gpu_name": "Unknown",
        "gpu_driver": "Unknown",
        "gpu_type": "Unknown",
        "gpu_vram_gb": 0,
        "os_version": f"Windows {platform.release()}",
        "system_age": "Unknown",
        "storage_type": "Unknown",
        "chipset_driver": "Standard/Auto-Managed",
    }


def get_static_hardware_fresh():
    data = default_static_hardware()

    if not has_wmi:
        logger.warning("WMI not available. Using fallback hardware detection.")
        return data

    try:
        pythoncom.CoInitialize()
        w = wmi.WMI()

        try:
            systems = w.Win32_ComputerSystem()
            if systems and systems[0].Name:
                data["device_name"] = systems[0].Name.strip()
        except Exception as error:
            logger.error(f"Device name detection failed: {error}")

        try:
            processors = w.Win32_Processor()
            if processors and processors[0].Name:
                data["cpu_name"] = " ".join(processors[0].Name.strip().split())
        except Exception as error:
            logger.error(f"CPU detection failed: {error}")

        try:
            gpus = w.Win32_VideoController()

            selected_gpu = None

            for gpu in gpus:
                gpu_name = gpu.Name or ""

                if "Microsoft Basic" in gpu_name:
                    continue

                if any(
                    brand in gpu_name
                    for brand in ["NVIDIA", "GeForce", "RTX", "GTX", "AMD", "Radeon RX", "Radeon Pro"]
                ) and "Intel" not in gpu_name:
                    selected_gpu = gpu
                    break

                if selected_gpu is None:
                    selected_gpu = gpu

            if selected_gpu:
                data["gpu_name"] = selected_gpu.Name or "Unknown GPU"
                data["gpu_driver"] = selected_gpu.DriverVersion or "Not Available"

                try:
                    vram_bytes = selected_gpu.AdapterRAM
                    if vram_bytes and int(vram_bytes) > 0:
                        data["gpu_vram_gb"] = round(abs(int(vram_bytes)) / (1024**3), 1)
                except Exception:
                    data["gpu_vram_gb"] = 0

                gpu_name_lower = data["gpu_name"].lower()

                if any(
                    brand in gpu_name_lower
                    for brand in ["nvidia", "geforce", "rtx", "gtx", "radeon rx", "radeon pro"]
                ):
                    data["gpu_type"] = "Dedicated"
                elif "intel" in gpu_name_lower or "radeon graphics" in gpu_name_lower:
                    data["gpu_type"] = "Integrated"
                else:
                    data["gpu_type"] = "Unknown"

        except Exception as error:
            logger.error(f"GPU detection failed: {error}")

        try:
            os_info = w.Win32_OperatingSystem()

            if os_info:
                data["os_version"] = f"{os_info[0].Caption} ({os_info[0].Version})"

                try:
                    install_date_str = os_info[0].InstallDate.split(".")[0]
                    install_date = datetime.strptime(install_date_str, "%Y%m%d%H%M%S")
                    days_old = (datetime.now() - install_date).days
                    years_old = round(days_old / 365.25, 1)
                    data["system_age"] = f"~{years_old} years"
                except Exception as error:
                    logger.error(f"System age calculation failed: {error}")

        except Exception as error:
            logger.error(f"OS detection failed: {error}")

        try:
            drives = w.Win32_DiskDrive()
            storage_types = []

            for drive in drives:
                model = (drive.Model or "").lower()
                media_type = (drive.MediaType or "").lower()
                interface_type = (drive.InterfaceType or "").lower()

                if "nvme" in model or "nvme" in interface_type:
                    storage_types.append("NVMe SSD")
                elif "ssd" in model or "solid state" in media_type:
                    storage_types.append("SATA SSD")
                elif "fixed hard disk" in media_type or "hdd" in model:
                    storage_types.append("HDD")
                else:
                    storage_types.append("Unknown")

            if "NVMe SSD" in storage_types:
                data["storage_type"] = "NVMe SSD"
            elif "SATA SSD" in storage_types:
                data["storage_type"] = "SATA SSD"
            elif "HDD" in storage_types:
                data["storage_type"] = "HDD"
            elif storage_types:
                data["storage_type"] = storage_types[0]

        except Exception as error:
            logger.error(f"Storage detection failed: {error}")

        try:
            boards = w.Win32_BaseBoard()
            if boards and boards[0].Product:
                data["chipset_driver"] = f"{boards[0].Product} (Auto-Managed)"
        except Exception as error:
            logger.error(f"Motherboard detection failed: {error}")

    except Exception as error:
        logger.error(f"WMI general error: {error}")

    finally:
        try:
            pythoncom.CoUninitialize()
        except Exception:
            pass

    return data


def get_static_hardware_cached():
    global _static_cache
    global _static_cache_time

    now = datetime.now()

    if (
        _static_cache is not None
        and _static_cache_time is not None
        and now - _static_cache_time < timedelta(seconds=CACHE_DURATION_SECONDS)
    ):
        return _static_cache

    _static_cache = get_static_hardware_fresh()
    _static_cache_time = now

    return _static_cache


@router.get("/test")
def test_endpoint():
    return {"message": "hardware router is working"}


@router.get("/live")
def get_live_hardware_stats():
    static_data = get_static_hardware_cached()

    cpu_percent = psutil.cpu_percent(interval=0.1)
    cpu_freq = psutil.cpu_freq()
    vm = psutil.virtual_memory()

    try:
        c_disk = psutil.disk_usage("C:\\" if platform.system() == "Windows" else "/")
    except Exception:
        c_disk = None

    disk_total_gb = round(c_disk.total / (1024**3), 2) if c_disk else 0
    disk_usage_percent = c_disk.percent if c_disk else 0

    disk_info = []

    for partition in psutil.disk_partitions():
        try:
            usage = psutil.disk_usage(partition.mountpoint)
            disk_info.append(
                {
                    "drive": partition.device,
                    "mountpoint": partition.mountpoint,
                    "fstype": partition.fstype,
                    "total_gb": round(usage.total / (1024**3), 2),
                    "used_gb": round(usage.used / (1024**3), 2),
                    "usage_percent": usage.percent,
                }
            )
        except Exception:
            continue

    process_insights = get_process_insights()

    return {
        "device_name": static_data["device_name"],
        "os_version": static_data["os_version"],
        "system_age": static_data["system_age"],
        "chipset_driver": static_data["chipset_driver"],
        "storage_type": static_data["storage_type"],
        "cpu": {
            "name": static_data["cpu_name"],
            "usage_percent": cpu_percent,
            "cores": psutil.cpu_count(logical=False),
            "threads": psutil.cpu_count(logical=True),
            "frequency_mhz": round(cpu_freq.current, 2) if cpu_freq else 0,
        },
        "gpu": {
            "name": static_data["gpu_name"],
            "driver": static_data["gpu_driver"],
            "type": static_data["gpu_type"],
            "vram_gb": static_data["gpu_vram_gb"],
        },
        "ram": {
            "total_gb": round(vm.total / (1024**3), 2),
            "used_gb": round(vm.used / (1024**3), 2),
            "usage_percent": vm.percent,
        },
        "disk": {
            "total_gb": disk_total_gb,
            "usage_percent": disk_usage_percent,
        },
        "all_disks": disk_info,
        "process_insights": process_insights,
    }


@router.post("/refresh")
def refresh_hardware_cache():
    global _static_cache
    global _static_cache_time

    _static_cache = None
    _static_cache_time = None

    return {"message": "Hardware cache refreshed successfully"}
