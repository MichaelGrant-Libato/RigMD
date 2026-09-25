using LibreHardwareMonitor.Hardware;
using RigMD.Application.Models;
using System.Diagnostics;

namespace RigMD.Infrastructure.Windows;

public interface IHardwareMonitorService
{
    void Tick();
    double? GetCpuTemperature();
    double? GetGpuTemperature();
    double? GetGpuDedicatedMemoryUsedGb();
    double? GetGpuDedicatedMemoryTotalGb();
    
    // Fallbacks if WMI is too slow
    double? GetCpuLoad();
    double? GetRamLoad();
    double? GetGpuLoad();
}

public class HardwareMonitorService : IHardwareMonitorService, IDisposable
{
    private readonly Computer _computer;
    private readonly bool _isInitialized;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true
        };
        
        try 
        {
            _computer.Open();
            _isInitialized = true;
            Tick(); // Initial tick to read sensors immediately
        }
        catch (Exception ex)
        {
            _isInitialized = false;
            Debug.WriteLine($"Failed to open LHM Computer (falling back to WMI): {ex.Message}");
        }
    }

    public void Tick()
    {
        if (!_isInitialized)
        {
            return;
        }

        try 
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var subHardware in hardware.SubHardware)
                {
                    subHardware.Update();
                }
            }
        }
        catch { }
    }

    public double? GetCpuTemperature()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            // 1. Check direct CPU temperature sensors
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    // Prefer Tctl/Tdie, Package, or Core Average / Core temperatures
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0)
                        {
                            if (sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Average", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
                            {
                                return Math.Round(sensor.Value.Value, 1);
                            }
                        }
                    }
                    
                    // Fallback to first valid temperature sensor on CPU
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0)
                            return Math.Round(sensor.Value.Value, 1);
                    }
                }
            }

            // 2. Fallback to motherboard/SuperIO sensors if CPU sensors are missing
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Motherboard)
                {
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        foreach (var sensor in subHardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0 && 
                                (sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)))
                            {
                                return Math.Round(sensor.Value.Value, 1);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back to WMI provider on sensor exception
        }

        return null;
    }

    public double? GetGpuTemperature()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                {
                    // Prefer Core or Hot Spot
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0 && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            return Math.Round(sensor.Value.Value, 1);
                    }

                    // Fallback to any valid temperature on GPU
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0)
                            return Math.Round(sensor.Value.Value, 1);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public double? GetGpuDedicatedMemoryUsedGb()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                            return sensor.Value / 1024.0;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public double? GetGpuDedicatedMemoryTotalGb()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                            return sensor.Value / 1024.0;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public double? GetCpuLoad()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            return sensor.Value;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public double? GetRamLoad()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Memory)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                            return sensor.Value;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public double? GetGpuLoad()
    {
        if (!_isInitialized)
        {
            return null;
        }

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            return sensor.Value;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        try
        {
            _computer.Close();
        }
        catch { }
    }
}
