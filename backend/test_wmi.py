import wmi
import pythoncom

pythoncom.CoInitialize()
try:
    w = wmi.WMI()
    
    print("=== CPU Detection ===")
    processors = w.Win32_Processor()
    for proc in processors:
        print(f"CPU: {proc.Name}")
    
    print("\n=== GPU Detection ===")
    gpus = w.Win32_VideoController()
    for gpu in gpus:
        print(f"GPU: {gpu.Name}")
        print(f"Driver: {gpu.DriverVersion}")
        print(f"VRAM: {gpu.AdapterRAM}")
        print("---")
    
    print("\n=== Storage Detection ===")
    drives = w.Win32_DiskDrive()
    for drive in drives:
        print(f"Drive: {drive.Model}")
        print(f"MediaType: {drive.MediaType}")
        print(f"InterfaceType: {drive.InterfaceType}")
        print("---")
        
    print("\n=== OS Info ===")
    os_info = w.Win32_OperatingSystem()
    for os in os_info:
        print(f"OS: {os.Caption}")
        print(f"Version: {os.Version}")
        print(f"InstallDate: {os.InstallDate}")
        
finally:
    pythoncom.CoUninitialize()
