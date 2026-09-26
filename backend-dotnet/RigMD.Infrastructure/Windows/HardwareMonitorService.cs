using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

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

/// <summary>
/// 100% user-mode hardware telemetry service.
/// Uses Windows Performance Counters, WMI ThermalZone/GPUPerformanceCounters,
/// Registry 64-bit VRAM lookup, GlobalMemoryStatusEx, and signed user-mode nvidia-smi CLI when available.
/// Never loads kernel drivers (such as WinRing0.sys).
/// </summary>
public class HardwareMonitorService : IHardwareMonitorService, IDisposable
{
    private readonly object _syncLock = new();
    private DateTime _lastTickUtc = DateTime.MinValue;
    private DateTime _lastGpuCliProbeUtc = DateTime.MinValue;
    private static readonly TimeSpan MinTickInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan MinGpuCliInterval = TimeSpan.FromMilliseconds(2500);

    private PerformanceCounter? _cpuCounter;
    private readonly string? _nvidiaSmiPath;
    private bool _nvidiaSmiUnavailable;

    private double? _cpuTempCelsius;
    private double? _gpuTempCelsius;
    private double? _gpuMemoryUsedGb;
    private double? _gpuMemoryTotalGb;
    private double? _cpuLoadPercent;
    private double? _ramLoadPercent;
    private double? _gpuLoadPercent;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public void Init()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public HardwareMonitorService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            _ = _cpuCounter.NextValue();
        }
        catch
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ = _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null;
            }
        }

        _nvidiaSmiPath = ResolveNvidiaSmiPath();
        _gpuMemoryTotalGb = ReadRegistryMaxGpuVramGb();

        Tick();
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;
        lock (_syncLock)
        {
            if (now - _lastTickUtc < MinTickInterval)
            {
                return;
            }
            _lastTickUtc = now;
        }

        UpdateCpuAndRamMetrics();
        UpdateCpuTemperature();
        UpdateGpuMetrics(now);
    }

    public double? GetCpuTemperature() => _cpuTempCelsius;

    public double? GetGpuTemperature() => _gpuTempCelsius;

    public double? GetGpuDedicatedMemoryUsedGb() => _gpuMemoryUsedGb;

    public double? GetGpuDedicatedMemoryTotalGb() => _gpuMemoryTotalGb;

    public double? GetCpuLoad() => _cpuLoadPercent;

    public double? GetRamLoad() => _ramLoadPercent;

    public double? GetGpuLoad() => _gpuLoadPercent;

    private void UpdateCpuAndRamMetrics()
    {
        // 1. RAM Load via fast Win32 user-mode GlobalMemoryStatusEx
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.Init();
            if (GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
            {
                var used = memStatus.ullTotalPhys - memStatus.ullAvailPhys;
                _ramLoadPercent = Math.Round((double)used / memStatus.ullTotalPhys * 100.0, 1);
            }
        }
        catch
        {
            // Ignore and leave previous reading
        }

        // 2. CPU Load via PerformanceCounter or WMI fallback
        try
        {
            if (_cpuCounter != null)
            {
                var raw = (double)_cpuCounter.NextValue();
                _cpuLoadPercent = Math.Round(Math.Clamp(raw, 0.0, 100.0), 1);
                return;
            }
        }
        catch
        {
            // Fall through to WMI
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, PercentProcessorUtility, PercentProcessorTime FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var util = TryParseDouble(obj["PercentProcessorUtility"]);
                var time = TryParseDouble(obj["PercentProcessorTime"]);
                var val = util > 0 ? util : time;
                _cpuLoadPercent = Math.Round(Math.Clamp(val, 0.0, 100.0), 1);
                break;
            }
        }
        catch
        {
            // Ignore WMI failure
        }
    }

    private void UpdateCpuTemperature()
    {
        var temp = ReadThermalZoneTemperatureCelsius();
        if (temp.HasValue)
        {
            _cpuTempCelsius = temp.Value;
        }
    }

    public static double? ReadThermalZoneTemperatureCelsius()
    {
        // 1. Try Win32_PerfFormattedData_Counters_ThermalZoneInformation (accessible without admin in root\CIMV2)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            double? bestCelsius = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                double? candidate = null;
                var highPrec = TryParseDouble(obj["HighPrecisionTemperature"]);
                if (highPrec >= 2732 && highPrec <= 3882)
                {
                    candidate = (highPrec / 10.0) - 273.15;
                }
                else
                {
                    var kelvin = TryParseDouble(obj["Temperature"]);
                    if (kelvin >= 273 && kelvin <= 388)
                    {
                        candidate = kelvin - 273.15;
                    }
                }

                if (candidate.HasValue && candidate.Value >= 15.0 && candidate.Value <= 115.0)
                {
                    if (!bestCelsius.HasValue || candidate.Value > bestCelsius.Value)
                    {
                        bestCelsius = Math.Round(candidate.Value, 1);
                    }
                }
            }

            if (bestCelsius.HasValue)
            {
                return bestCelsius;
            }
        }
        catch
        {
            // Ignore and try MSAcpi fallback
        }

        // 2. Fallback to root\WMI MSAcpi_ThermalZoneTemperature
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            double? bestCelsius = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                var raw = TryParseDouble(obj["CurrentTemperature"]);
                if (raw >= 2732 && raw <= 3882)
                {
                    var celsius = (raw / 10.0) - 273.15;
                    if (celsius >= 15.0 && celsius <= 115.0)
                    {
                        if (!bestCelsius.HasValue || celsius > bestCelsius.Value)
                        {
                            bestCelsius = Math.Round(celsius, 1);
                        }
                    }
                }
            }

            if (bestCelsius.HasValue)
            {
                return bestCelsius;
            }
        }
        catch
        {
            // Ignore if ACPI thermal zone is not exposed by motherboard BIOS
        }

        return null;
    }

    private void UpdateGpuMetrics(DateTime nowUtc)
    {
        bool gotNvidiaMetrics = false;

        if (!_nvidiaSmiUnavailable && _nvidiaSmiPath != null)
        {
            if (nowUtc - _lastGpuCliProbeUtc >= MinGpuCliInterval)
            {
                _lastGpuCliProbeUtc = nowUtc;
                gotNvidiaMetrics = TryProbeNvidiaSmi(_nvidiaSmiPath);
            }
            else if (_gpuTempCelsius.HasValue || _gpuLoadPercent.HasValue)
            {
                gotNvidiaMetrics = true;
            }
        }

        if (!gotNvidiaMetrics)
        {
            UpdateGpuFromWindowsPerformanceCounters();
        }
    }

    private bool TryProbeNvidiaSmi(string exePath)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--query-gpu=temperature.gpu,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (!proc.Start())
            {
                _nvidiaSmiUnavailable = true;
                return false;
            }

            string output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(1200) || proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                return false;
            }

            var firstLine = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return false;
            }

            var parts = firstLine.Split(',');
            if (parts.Length >= 4)
            {
                if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tempC) &&
                    tempC > 0 && tempC < 130)
                {
                    _gpuTempCelsius = Math.Round(tempC, 1);
                }

                if (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var utilPct) &&
                    utilPct >= 0)
                {
                    _gpuLoadPercent = Math.Round(Math.Clamp(utilPct, 0.0, 100.0), 1);
                }

                if (double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var usedMib) &&
                    usedMib >= 0)
                {
                    _gpuMemoryUsedGb = Math.Round(usedMib / 1024.0, 2);
                }

                if (double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMib) &&
                    totalMib > 0)
                {
                    _gpuMemoryTotalGb = Math.Round(totalMib / 1024.0, 2);
                }

                return true;
            }
        }
        catch
        {
            _nvidiaSmiUnavailable = true;
        }

        return false;
    }

    private void UpdateGpuFromWindowsPerformanceCounters()
    {
        // 1. Dedicated VRAM Used via Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory
        try
        {
            using var memSearcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");

            double maxDedicatedBytes = 0;
            foreach (ManagementObject obj in memSearcher.Get())
            {
                var bytes = TryParseDouble(obj["DedicatedUsage"]);
                if (bytes > maxDedicatedBytes)
                {
                    maxDedicatedBytes = bytes;
                }
            }

            if (maxDedicatedBytes > 0)
            {
                _gpuMemoryUsedGb = Math.Round(maxDedicatedBytes / (1024.0 * 1024.0 * 1024.0), 2);
            }
        }
        catch
        {
            // Ignore if GPU adapter memory counters are unavailable
        }

        // 2. GPU Utilization % via Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine
        try
        {
            using var engineSearcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            var sumsByEngineType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject obj in engineSearcher.Get())
            {
                var util = TryParseDouble(obj["UtilizationPercentage"]);
                if (util <= 0)
                {
                    continue;
                }

                var name = obj["Name"]?.ToString() ?? string.Empty;
                var idx = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                var engineKey = idx >= 0 ? name.Substring(idx) : "default";

                sumsByEngineType.TryGetValue(engineKey, out var current);
                sumsByEngineType[engineKey] = current + util;
            }

            if (sumsByEngineType.Count > 0)
            {
                var maxEngineLoad = sumsByEngineType.Values.Max();
                _gpuLoadPercent = Math.Round(Math.Clamp(maxEngineLoad, 0.0, 100.0), 1);
            }
            else
            {
                _gpuLoadPercent ??= 0.0;
            }
        }
        catch
        {
            // Ignore if GPU engine performance counters are unavailable
        }
    }

    private static string? ResolveNvidiaSmiPath()
    {
        try
        {
            var sys32Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
            if (File.Exists(sys32Path))
            {
                return sys32Path;
            }

            var programFilesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation",
                "NVSMI",
                "nvidia-smi.exe");
            if (File.Exists(programFilesPath))
            {
                return programFilesPath;
            }
        }
        catch
        {
            // Ignore path resolution errors
        }

        return null;
    }

    /// <summary>
    /// Reads true 64-bit GPU VRAM size from Windows Display Class Registry keys
    /// (avoids the 32-bit 4 GB cap on Win32_VideoController.AdapterRAM).
    /// </summary>
    public static double? ReadRegistryMaxGpuVramGb(string? filterAdapterName = null)
    {
        const string displayClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(displayClassKeyPath);
            if (baseKey == null)
            {
                return null;
            }

            ulong maxBytes = 0;
            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith("0", StringComparison.Ordinal))
                {
                    continue;
                }

                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(filterAdapterName))
                {
                    var driverDesc = subKey.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                    if (!driverDesc.Contains(filterAdapterName, StringComparison.OrdinalIgnoreCase) &&
                        !filterAdapterName.Contains(driverDesc, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                ulong vramBytes = ExtractVramBytesFromRegistrySubKey(subKey);
                if (vramBytes > maxBytes)
                {
                    maxBytes = vramBytes;
                }
            }

            if (maxBytes > 0)
            {
                return Math.Round(maxBytes / (1024.0 * 1024.0 * 1024.0), 1);
            }
        }
        catch
        {
            // Ignore registry permission or read errors
        }

        return null;
    }

    private static ulong ExtractVramBytesFromRegistrySubKey(RegistryKey subKey)
    {
        var qwVal = subKey.GetValue("HardwareInformation.qwMemorySize");
        if (qwVal is long qwLong && qwLong > 0)
        {
            return (ulong)qwLong;
        }
        if (qwVal is ulong qwUlong && qwUlong > 0)
        {
            return qwUlong;
        }
        if (qwVal is byte[] qwBytes && qwBytes.Length >= 8)
        {
            return BitConverter.ToUInt64(qwBytes, 0);
        }

        var memVal = subKey.GetValue("HardwareInformation.MemorySize");
        if (memVal is int memInt && memInt > 0)
        {
            return (ulong)memInt;
        }
        if (memVal is long memLong && memLong > 0)
        {
            return (ulong)memLong;
        }
        if (memVal is byte[] memBytes)
        {
            if (memBytes.Length >= 8)
            {
                return BitConverter.ToUInt64(memBytes, 0);
            }
            if (memBytes.Length >= 4)
            {
                return BitConverter.ToUInt32(memBytes, 0);
            }
        }

        return 0;
    }

    private static double TryParseDouble(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    public void Dispose()
    {
        try
        {
            _cpuCounter?.Dispose();
        }
        catch
        {
            // Ignore dispose errors
        }
    }
}
