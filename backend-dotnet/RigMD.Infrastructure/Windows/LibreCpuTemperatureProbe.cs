using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Infrastructure.Windows;

internal interface ICpuTemperatureProbe : IDisposable
{
    IReadOnlyList<HardwareTemperatureSample> Read();
}

internal sealed class LibreCpuTemperatureProbe : ICpuTemperatureProbe
{
    private readonly Computer _computer;

    public LibreCpuTemperatureProbe()
    {
        _computer = CreateCpuComputer();
        try
        {
            _computer.Open();
        }
        catch
        {
            _computer.Close();
            throw;
        }
    }

    internal static Computer CreateCpuComputer() => new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = false,
        IsMotherboardEnabled = false,
        IsMemoryEnabled = false,
        IsStorageEnabled = false,
        IsNetworkEnabled = false,
        IsControllerEnabled = false,
        IsBatteryEnabled = false,
        IsPsuEnabled = false,
        IsPowerMonitorEnabled = false
    };

    public IReadOnlyList<HardwareTemperatureSample> Read()
    {
        var samples = new List<HardwareTemperatureSample>();
        foreach (var cpu in _computer.Hardware.Where(hardware => hardware.HardwareType == HardwareType.Cpu))
        {
            cpu.Update();
            samples.AddRange(cpu.Sensors.Where(sensor => sensor.SensorType == SensorType.Temperature)
                .Select(sensor => new HardwareTemperatureSample("Cpu", cpu.Identifier.ToString(),
                    sensor.Identifier.ToString(), "Temperature", sensor.Value, sensor.Name)));
        }
        return samples;
    }

    internal static bool CanAccessDriver()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var handle = CreateFileW(@"\\?\GLOBALROOT\Device\PawnIO", 0xc0000000, 3, nint.Zero, 3, 0, nint.Zero);
        return !handle.IsInvalid;
    }

    public void Dispose() => _computer.Close();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);
}
