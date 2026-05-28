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


PHYSICAL_DISK_MEDIA_TYPES = {
    3: "HDD",
    4: "SSD",
    5: "SCM",
}

PHYSICAL_DISK_BUS_TYPES = {
    3: "SCSI",
    7: "USB",
    8: "RAID",
    10: "SAS",
    11: "SATA",
    16: "SD",
    17: "NVMe",
}

NVME_MODEL_HINTS = [
    " nvme", "nvme ", "pcie", "pci-e", "samsung 970", "samsung 980", "samsung 990",
    "970 evo", "970 pro", "980 pro", "980 evo", "990 pro", "990 evo",
    "wd_black sn", "wd black sn", "western digital sn", "crucial p", "kingston snv",
    "kingston skc", "adata sx", "adata legend", "corsair mp", "kioxia",
]

SATA_SSD_MODEL_HINTS = [
    "samsung 750", "samsung 850", "samsung 860", "samsung 870", "crucial bx",
    "crucial mx", "sandisk ssd", "kingston sa", "kingston suv", "wd blue sa",
]

HDD_MODEL_HINTS = [
    "barracuda", "ironwolf", "skyhawk", "wd blue", "wd black", "wd red", "wd purple",
    "wdc wd", "seagate st", "toshiba dt", "hitachi", "hgst",
]


def normalize_wmi_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def get_physical_disk_metadata():
    metadata = {}

    if not has_wmi or platform.system() != "Windows":
        return metadata

    try:
        pythoncom.CoInitialize()
        storage_wmi = wmi.WMI(namespace="root\\Microsoft\\Windows\\Storage")

        for disk in storage_wmi.MSFT_PhysicalDisk():
            device_id = normalize_wmi_int(getattr(disk, "DeviceId", None))

            if device_id is None:
                continue

            media_type_code = normalize_wmi_int(getattr(disk, "MediaType", None))
            bus_type_code = normalize_wmi_int(getattr(disk, "BusType", None))

            metadata[device_id] = {
                "friendly_name": getattr(disk, "FriendlyName", None),
                "media_type": PHYSICAL_DISK_MEDIA_TYPES.get(media_type_code),
                "media_type_code": media_type_code,
                "bus_type": PHYSICAL_DISK_BUS_TYPES.get(bus_type_code),
                "bus_type_code": bus_type_code,
            }

    except Exception as error:
        logger.error(f"Physical disk metadata detection failed: {error}")

    finally:
        try:
            pythoncom.CoUninitialize()
        except Exception:
            pass

    return metadata


def classify_storage_drive(model, media_type, interface_type, physical_metadata):
    model_value = model.lower().strip()
    model_text = f" {model.lower()} "
    media_text = (media_type or "").lower()
    interface_text = (interface_type or "").lower()
    physical_media = (physical_metadata.get("media_type") or "").upper()
    physical_bus = (physical_metadata.get("bus_type") or "").upper()

    if physical_bus == "NVME":
        return "NVMe SSD", "Windows physical disk bus type"

    if physical_media == "SSD" and physical_bus in {"SATA", "ATA"}:
        return "SATA SSD", "Windows physical disk media and bus type"

    if physical_media == "HDD":
        return "HDD", "Windows physical disk media type"

    if physical_media == "SSD":
        if "nvme" in interface_text:
            return "NVMe SSD", "Win32 interface type"

        return "SSD", "Windows physical disk media type"

    if "nvme" in interface_text or any(hint in model_text for hint in NVME_MODEL_HINTS):
        return "NVMe SSD", "Model or interface hint"

    if "ssd" in model_text or "solid state" in media_text:
        if any(hint in model_text for hint in SATA_SSD_MODEL_HINTS) or interface_text in {"ide", "ata", "sata"}:
            return "SATA SSD", "Model or interface hint"

        return "SSD", "Model or media hint"

    if any(hint in model_text for hint in HDD_MODEL_HINTS):
        return "HDD", "Model hint"

    if len(model_value) >= 3 and model_value.startswith("st") and model_value[2].isdigit():
        return "HDD", "Model hint"

    return "Unknown", "No reliable storage type signal"


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
        "all_storage_drives": [],
        "chipset_driver": "Standard/Auto-Managed",
    }


def get_logical_disk_indexes():
    disk_indexes = {}

    if not has_wmi or platform.system() != "Windows":
        return disk_indexes

    try:
        pythoncom.CoInitialize()
        w = wmi.WMI()

        for physical_disk in w.Win32_DiskDrive():
            try:
                disk_index = int(physical_disk.Index)
            except Exception:
                continue

            for partition in physical_disk.associators("Win32_DiskDriveToDiskPartition"):
                for logical_disk in partition.associators("Win32_LogicalDiskToPartition"):
                    device_id = (logical_disk.DeviceID or "").upper()
                    if device_id:
                        disk_indexes[device_id] = disk_index

    except Exception as error:
        logger.error(f"Logical disk mapping failed: {error}")

    finally:
        try:
            pythoncom.CoUninitialize()
        except Exception:
            pass

    return disk_indexes


def enrich_storage_drives_with_usage(storage_drives, disk_info):
    drives = [dict(drive) for drive in storage_drives]
    disks_by_index = {}

    if not drives:
        return [
            {
                "model": disk.get("drive") or "Unknown Volume",
                "type": "Unknown",
                "size_gb": disk.get("total_gb", 0),
                "interface": "Unknown",
                "disk_index": disk.get("disk_index"),
                "volumes": [disk],
                "used_gb": disk.get("used_gb"),
                "usage_percent": disk.get("usage_percent"),
            }
            for disk in disk_info
        ]

    for disk in disk_info:
        disk_index = disk.get("disk_index")
        if disk_index is None:
            continue

        disks_by_index.setdefault(disk_index, []).append(disk)

    for position, drive in enumerate(drives):
        disk_index = drive.get("disk_index")
        volumes = disks_by_index.get(disk_index, [])

        if not volumes and len(drives) == len(disk_info):
            volumes = [disk_info[position]]

        total_gb = sum(volume.get("total_gb", 0) for volume in volumes)
        used_gb = sum(volume.get("used_gb", 0) for volume in volumes)

        drive["volumes"] = volumes
        drive["used_gb"] = round(used_gb, 2) if volumes else None
        drive["usage_percent"] = round((used_gb / total_gb) * 100, 1) if total_gb else None

    return drives


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
            physical_disk_metadata = get_physical_disk_metadata()
            storage_details = []
            storage_types = []

            for drive in drives:
                model = drive.Model or ""
                media_type = drive.MediaType or ""
                interface_type = drive.InterfaceType or ""
                size_bytes = drive.Size or 0
                size_gb = round(int(size_bytes) / (1024**3), 2) if size_bytes else 0
                disk_index = int(drive.Index) if drive.Index is not None else None
                physical_metadata = physical_disk_metadata.get(disk_index, {})
                
                logger.info(f"\n=== Drive {len(storage_details)} ===")
                logger.info(
                    f"Model: '{model}', MediaType: '{media_type}', Interface: '{interface_type}', "
                    f"Size: {size_gb}GB, PhysicalMetadata: {physical_metadata}"
                )
                
                detected_type, detection_source = classify_storage_drive(
                    model=model,
                    media_type=media_type,
                    interface_type=interface_type,
                    physical_metadata=physical_metadata,
                )
                
                storage_types.append(detected_type)
                storage_details.append({
                    "model": model or "Unknown",
                    "type": detected_type,
                    "size_gb": size_gb,
                    "interface": interface_type or physical_metadata.get("bus_type") or "Unknown",
                    "disk_index": disk_index,
                    "media_type": media_type or physical_metadata.get("media_type") or "Unknown",
                    "bus_type": physical_metadata.get("bus_type"),
                    "detection_source": detection_source,
                })
                
                logger.info(f"→ Classified as: {detected_type}")

            # Store all drive details for later use
            data["all_storage_drives"] = storage_details
            logger.info(f"Total drives detected: {len(storage_details)}")

            # Primary storage type (for backward compatibility) - prefer NVMe if available
            if "NVMe SSD" in storage_types:
                data["storage_type"] = "NVMe SSD"
            elif "SATA SSD" in storage_types:
                data["storage_type"] = "SATA SSD"
            elif "SSD" in storage_types:
                data["storage_type"] = "SSD"
            elif "HDD" in storage_types:
                data["storage_type"] = "HDD"
            elif storage_types:
                # Use the first detected type even if it's "Unknown (Possible SSD)"
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

    disk_info = []
    disk_indexes = get_logical_disk_indexes()

    for partition in psutil.disk_partitions():
        try:
            usage = psutil.disk_usage(partition.mountpoint)
            drive_key = partition.device.rstrip("\\").upper()
            disk_info.append(
                {
                    "drive": partition.device,
                    "mountpoint": partition.mountpoint,
                    "fstype": partition.fstype,
                    "disk_index": disk_indexes.get(drive_key),
                    "total_gb": round(usage.total / (1024**3), 2),
                    "used_gb": round(usage.used / (1024**3), 2),
                    "usage_percent": usage.percent,
                }
            )
        except Exception:
            continue

    disk_total_gb = round(sum(disk["total_gb"] for disk in disk_info), 2)
    disk_used_gb = round(sum(disk["used_gb"] for disk in disk_info), 2)
    disk_usage_percent = round((disk_used_gb / disk_total_gb) * 100, 1) if disk_total_gb else 0

    process_insights = get_process_insights()
    storage_drives = enrich_storage_drives_with_usage(
        static_data.get("all_storage_drives", []),
        disk_info,
    )

    return {
        "device_name": static_data["device_name"],
        "os_version": static_data["os_version"],
        "system_age": static_data["system_age"],
        "chipset_driver": static_data["chipset_driver"],
        "storage_type": static_data["storage_type"],
        "storage_drives": storage_drives,
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
            "used_gb": disk_used_gb,
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
