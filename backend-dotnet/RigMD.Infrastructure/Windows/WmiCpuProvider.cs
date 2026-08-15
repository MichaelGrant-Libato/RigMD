using System.Management;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiCpuProvider : ICpuProvider
{
    public CpuStatsDto GetCpuStats()
    {
        var dto = new CpuStatsDto();
        
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                dto.Name = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                dto.Cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                dto.Threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                break; // Just grab the first CPU
            }
            
            // Note: UsagePercent and FrequencyMhz are harder to get purely from WMI efficiently in real-time,
            // but we'll try to get frequency if possible or mock the real-time stats for this initial port.
            // For rigorous real-time, System.Diagnostics.PerformanceCounter is better.
            dto.UsagePercent = 0; // Placeholder until real-time hook is built
            dto.FrequencyMhz = 0;
        }
        catch (Exception)
        {
            // Fallbacks
            dto.Name = "Unknown CPU (Fallback)";
            dto.Cores = Environment.ProcessorCount;
            dto.Threads = Environment.ProcessorCount;
        }

        return dto;
    }
}
