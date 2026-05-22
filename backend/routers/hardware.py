from fastapi import APIRouter
import psutil
import platform
from datetime import datetime
import logging

# Set up logging
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

def get_static_hardware():
    """Get static hardware information using WMI"""
    logger.info("Starting hardware detection...")
    data = {
        "cpu_name": platform.processor() or "Unknown CPU",
        "gpu_name": "Unknown",
        "gpu_driver": "Unknown",
        "gpu_type": "Unknown",
        "gpu_vram_gb": 0,
        "os_version": f"Windows {platform.release()}",
        "system_age": "Unknown",
        "storage_type": "Unknown",
        "chipset_driver": "Standard/Auto-Managed"
    }

    if has_wmi:
        logger.info("WMI is available, starting detection...")
        try:
            # Required to run WMI inside FastAPI async threads
            pythoncom.CoInitialize()
            w = wmi.WMI()

            # 1. Get CPU true name
            try:
                processors = w.Win32_Processor()
                if processors and processors[0].Name:
                    cpu_name = processors[0].Name.strip()
                    # Clean up CPU name (remove extra spaces)
                    cpu_name = ' '.join(cpu_name.split())
                    data["cpu_name"] = cpu_name
                    logger.info(f"CPU detected: {cpu_name}")
            except Exception as e:
                logger.error(f"CPU detection failed: {e}")
                # Fallback: try to get from platform
                if not data["cpu_name"] or "Family" in data["cpu_name"]:
                    data["cpu_name"] = "AMD Ryzen (Model detection failed)"

            # 2. Get GPU info - prioritize dedicated GPUs
            try:
                gpus = w.Win32_VideoController()
                logger.info(f"Found {len(gpus)} GPU(s)")
                
                if gpus:
                    # Find dedicated GPU first, fallback to any GPU
                    dedicated_gpu = None
                    all_gpus = []
                    
                    for gpu in gpus:
                        gpu_name = gpu.Name or ""
                        logger.info(f"GPU found: {gpu_name}")
                        
                        # Skip Microsoft Basic Display Adapter
                        if "Microsoft Basic" in gpu_name:
                            continue
                            
                        all_gpus.append(gpu)
                        
                        # Prioritize NVIDIA, AMD dedicated cards
                        if any(brand in gpu_name for brand in ["NVIDIA", "AMD", "Radeon RX", "Radeon HD", "Radeon Pro", "GeForce", "RTX", "GTX"]):
                            if "Intel" not in gpu_name:  # Exclude Intel integrated
                                dedicated_gpu = gpu
                                break
                    
                    # Use dedicated GPU if found, otherwise use first non-basic GPU
                    if dedicated_gpu:
                        primary_gpu = dedicated_gpu
                    elif all_gpus:
                        primary_gpu = all_gpus[0]
                    else:
                        primary_gpu = gpus[0]
                    
                    data["gpu_name"] = primary_gpu.Name or "Unknown GPU"
                    data["gpu_driver"] = primary_gpu.DriverVersion or "Not Available"
                    
                    logger.info(f"Selected GPU: {data['gpu_name']}")
                    logger.info(f"GPU Driver: {data['gpu_driver']}")
                    
                    # Try to get VRAM
                    vram_bytes = primary_gpu.AdapterRAM
                    if vram_bytes and int(vram_bytes) > 0:
                        vram_gb = round(abs(int(vram_bytes)) / (1024**3), 1)
                        data["gpu_vram_gb"] = vram_gb
                        logger.info(f"GPU VRAM: {vram_gb} GB")
                    else:
                        logger.warning(f"VRAM not available from WMI")
                    
                    # Determine GPU type
                    gpu_name_lower = data["gpu_name"].lower()
                    if any(brand in gpu_name_lower for brand in ["nvidia", "geforce", "rtx", "gtx", "radeon rx", "radeon pro"]):
                        data["gpu_type"] = "Dedicated"
                    elif "intel" in gpu_name_lower or "radeon graphics" in gpu_name_lower:
                        data["gpu_type"] = "Integrated"
                    elif data["gpu_vram_gb"] > 2.0:
                        data["gpu_type"] = "Dedicated"
                    else:
                        data["gpu_type"] = "Unknown"
                        
            except Exception as e:
                logger.error(f"GPU detection failed: {e}")
                data["gpu_name"] = "Detection Failed"
                data["gpu_driver"] = "N/A"

            # 3. Get OS and System Age
            try:
                os_info = w.Win32_OperatingSystem()
                if os_info:
                    try:
                        install_date_str = os_info[0].InstallDate.split('.')[0]
                        install_date = datetime.strptime(install_date_str, "%Y%m%d%H%M%S")
                        days_old = (datetime.now() - install_date).days
                        years_old = round(days_old / 365.25, 1)
                        data["system_age"] = f"~{years_old} years"
                    except Exception as e:
                        logger.error(f"System age calculation failed: {e}")
                        data["system_age"] = "Unknown"
                    
                    try:
                        data["os_version"] = f"{os_info[0].Caption} ({os_info[0].Version})"
                    except:
                        data["os_version"] = f"Windows {platform.release()}"
            except Exception as e:
                logger.error(f"OS info detection failed: {e}")
                
            # 4. Get Storage Type - check all drives and prioritize OS drive
            try:
                drives = w.Win32_DiskDrive()
                storage_types = []
                logger.info(f"Found {len(drives)} drive(s)")
                
                if drives:
                    for drive in drives:
                        model = (drive.Model or "").lower()
                        media_type = (drive.MediaType or "").lower()
                        interface_type = (drive.InterfaceType or "").lower()
                        
                        logger.info(f"Drive: {drive.Model}, MediaType: {media_type}, Interface: {interface_type}")
                        
                        # Check multiple indicators for drive type
                        if "nvme" in model or "nvme" in interface_type:
                            storage_types.append("NVMe SSD")
                        elif "ssd" in model or "solid state" in media_type:
                            storage_types.append("SATA SSD")
                        elif "fixed hard disk" in media_type or "hdd" in model:
                            storage_types.append("HDD")
                        else:
                            # Try to determine from model name patterns
                            if any(x in model for x in ["samsung", "crucial", "kingston", "wd", "western digital"]):
                                if any(x in model for x in ["ssd", "solid"]):
                                    storage_types.append("SATA SSD")
                                else:
                                    storage_types.append("Unknown")
                            else:
                                storage_types.append("Unknown")
                    
                    # Prioritize best storage type found
                    if "NVMe SSD" in storage_types:
                        data["storage_type"] = "NVMe SSD"
                    elif "SATA SSD" in storage_types:
                        data["storage_type"] = "SATA SSD"
                    elif "HDD" in storage_types:
                        data["storage_type"] = "HDD"
                    else:
                        data["storage_type"] = storage_types[0] if storage_types else "Unknown"
                    
                    logger.info(f"Final storage type: {data['storage_type']}")
            except Exception as e:
                logger.error(f"Storage detection failed: {e}")
                    
            # 5. Get Motherboard/Chipset proxy
            try:
                boards = w.Win32_BaseBoard()
                if boards and boards[0].Product:
                    data["chipset_driver"] = f"{boards[0].Product} (Auto-Managed)"
            except Exception as e:
                logger.error(f"Motherboard detection failed: {e}")

        except Exception as e:
            logger.error(f"WMI General Error: {e}")
            import traceback
            traceback.print_exc()
        finally:
            try:
                pythoncom.CoUninitialize()
            except:
                pass
    else:
        logger.warning("WMI not available - using fallback detection")

    logger.info(f"Final hardware data - CPU: {data['cpu_name']}, GPU: {data['gpu_name']}, Storage: {data['storage_type']}")
    return data


@router.get("/test")
def test_endpoint():
    """Test endpoint to verify logging works"""
    logger.info("TEST ENDPOINT CALLED!")
    print("TEST ENDPOINT PRINT!")
    return {"message": "test successful"}


@router.get("/live")
def get_live_hardware_stats():
    logger.info("=== /live endpoint called ===")
    static_data = get_static_hardware()
    logger.info(f"=== Got static data: CPU={static_data.get('cpu_name', 'N/A')} ===")

    # LIVE Telemetry
    cpu_percent = psutil.cpu_percent(interval=None)
    cpu_freq = psutil.cpu_freq()
    vm = psutil.virtual_memory()
    
    # Get all disk partitions
    disk_info = []
    partitions = psutil.disk_partitions()
    for partition in partitions:
        try:
            usage = psutil.disk_usage(partition.mountpoint)
            disk_info.append({
                "drive": partition.device,
                "mountpoint": partition.mountpoint,
                "fstype": partition.fstype,
                "total_gb": round(usage.total / (1024 ** 3), 2),
                "used_gb": round(usage.used / (1024 ** 3), 2),
                "usage_percent": usage.percent
            })
        except PermissionError:
            # Skip drives that can't be accessed
            continue
    
    # Get primary C: drive for backward compatibility
    c_disk = psutil.disk_usage('C:\\' if platform.system() == 'Windows' else '/')

    return {
        "os_version": static_data["os_version"],
        "system_age": static_data["system_age"],
        "chipset_driver": static_data["chipset_driver"],
        "storage_type": static_data["storage_type"],
        "cpu": {
            "name": static_data["cpu_name"],
            "usage_percent": cpu_percent,
            "cores": psutil.cpu_count(logical=False),
            "threads": psutil.cpu_count(logical=True),
            "frequency_mhz": cpu_freq.current if cpu_freq else 0
        },
        "gpu": {
            "name": static_data["gpu_name"],
            "driver": static_data["gpu_driver"],
            "type": static_data["gpu_type"],
            "vram_gb": static_data["gpu_vram_gb"]
        },
        "ram": {
            "total_gb": round(vm.total / (1024 ** 3), 2),
            "used_gb": round(vm.used / (1024 ** 3), 2),
            "usage_percent": vm.percent
        },
        "disk": {
            "total_gb": round(c_disk.total / (1024 ** 3), 2),
            "usage_percent": c_disk.percent
        },
        "all_disks": disk_info  # New: All detected drives
    }


@router.post("/refresh")
def refresh_hardware_cache():
    """Endpoint kept for compatibility - no longer uses cache"""
    logger.info("Refresh endpoint called (no cache to clear)")
    return {"message": "Detection will run fresh on next request"}