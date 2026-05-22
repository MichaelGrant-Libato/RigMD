from fastapi import APIRouter
import psutil
import platform
from datetime import datetime

try:
    import wmi
    import pythoncom
    has_wmi = True
except ImportError:
    has_wmi = False

router = APIRouter(prefix="/api/hardware", tags=["Hardware"])

# Cache static data so we don't freeze the PC by running WMI queries every 1.5 seconds
static_hardware_cache = None

def get_static_hardware():
    global static_hardware_cache
    if static_hardware_cache:
        return static_hardware_cache

    data = {
        "cpu_name": platform.processor(),
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
        try:
            # Required to run WMI inside FastAPI async threads
            pythoncom.CoInitialize()
            w = wmi.WMI()

            # 1. Get CPU true name
            processors = w.Win32_Processor()
            if processors:
                data["cpu_name"] = processors[0].Name.strip()

            # 2. Get GPU info
            gpus = w.Win32_VideoController()
            if gpus:
                primary_gpu = gpus[-1] 
                data["gpu_name"] = primary_gpu.Name
                data["gpu_driver"] = primary_gpu.DriverVersion
                
                vram_bytes = primary_gpu.AdapterRAM
                if vram_bytes:
                    vram_gb = round(abs(int(vram_bytes)) / (1024**3), 1)
                    data["gpu_vram_gb"] = vram_gb
                    if "Intel" in data["gpu_name"] or "Radeon Graphics" in data["gpu_name"] or vram_gb <= 2.0:
                        data["gpu_type"] = "Integrated"
                    else:
                        data["gpu_type"] = "Dedicated"

            # 3. Get OS and System Age
            os_info = w.Win32_OperatingSystem()
            if os_info:
                install_date_str = os_info[0].InstallDate.split('.')[0]
                install_date = datetime.strptime(install_date_str, "%Y%m%d%H%M%S")
                days_old = (datetime.now() - install_date).days
                years_old = round(days_old / 365.25, 1)
                data["system_age"] = f"~{years_old} years"
                data["os_version"] = f"{os_info[0].Caption} ({os_info[0].Version})"
                
            # 4. Get Storage Type (Heuristic based on model name)
            drives = w.Win32_DiskDrive()
            if drives:
                model = drives[0].Model.lower()
                if "nvme" in model:
                    data["storage_type"] = "NVMe SSD"
                elif "ssd" in model:
                    data["storage_type"] = "SATA SSD"
                else:
                    data["storage_type"] = "HDD / Standard"
                    
            # 5. Get Motherboard/Chipset proxy
            boards = w.Win32_BaseBoard()
            if boards:
                data["chipset_driver"] = f"{boards[0].Product} (Auto-Managed)"

        except Exception as e:
            print(f"WMI Error: {e}")
        finally:
            pythoncom.CoUninitialize()

    static_hardware_cache = data
    return static_hardware_cache


@router.get("/live")
def get_live_hardware_stats():
    static_data = get_static_hardware()

    # LIVE Telemetry
    cpu_percent = psutil.cpu_percent(interval=None)
    cpu_freq = psutil.cpu_freq()
    vm = psutil.virtual_memory()
    disk = psutil.disk_usage('C:\\' if platform.system() == 'Windows' else '/')

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
            "total_gb": round(disk.total / (1024 ** 3), 2),
            "usage_percent": disk.percent
        }
    }