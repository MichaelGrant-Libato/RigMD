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
            LoadStaticCpuInfo(dto);
            LoadLiveCpuInfo(dto);
        }
        catch
        {
            ApplyFallback(dto);
        }

        return dto;
    }

    private static void LoadStaticCpuInfo(
        CpuStatsDto dto)
    {
        using var searcher =
            new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, CurrentClockSpeed FROM Win32_Processor");

        foreach (
            ManagementObject obj
            in searcher.Get())
        {
            dto.Name =
                obj["Name"]
                    ?.ToString()
                    ?.Trim()
                ?? "Unknown CPU";

            dto.Cores =
                Convert.ToInt32(
                    obj["NumberOfCores"]
                    ?? 0);

            dto.Threads =
                Convert.ToInt32(
                    obj["NumberOfLogicalProcessors"]
                    ?? 0);

            dto.FrequencyMhz =
                Convert.ToDouble(
                    obj["CurrentClockSpeed"]
                    ?? 0);

            break;
        }
    }

    private static void LoadLiveCpuInfo(
        CpuStatsDto dto)
    {
        using var searcher =
            new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, ProcessorFrequency, PercentProcessorTime, PercentProcessorUtility FROM Win32_PerfFormattedData_Counters_ProcessorInformation");

        foreach (
            ManagementObject obj
            in searcher.Get())
        {
            var name =
                obj["Name"]
                    ?.ToString();

            if (!string.Equals(
                    name,
                    "_Total",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var utility =
                TryConvertToDouble(
                    obj["PercentProcessorUtility"]);

            var processorTime =
                TryConvertToDouble(
                    obj["PercentProcessorTime"]);

            var frequency =
                TryConvertToDouble(
                    obj["ProcessorFrequency"]);

            var usage =
                utility > 0
                    ? utility
                    : processorTime;

            dto.UsagePercent =
                Math.Clamp(
                    usage,
                    0,
                    100);

            if (frequency > 0)
            {
                dto.FrequencyMhz =
                    frequency;
            }

            break;
        }
    }

    private static double TryConvertToDouble(
        object? value)
    {
        if (value == null)
        {
            return 0;
        }

        return double.TryParse(
            value.ToString(),
            out var result)
            ? result
            : 0;
    }

    private static void ApplyFallback(
        CpuStatsDto dto)
    {
        if (string.IsNullOrWhiteSpace(
                dto.Name))
        {
            dto.Name =
                "Unknown CPU";
        }

        if (dto.Cores <= 0)
        {
            dto.Cores =
                Environment.ProcessorCount;
        }

        if (dto.Threads <= 0)
        {
            dto.Threads =
                Environment.ProcessorCount;
        }

        dto.UsagePercent =
            Math.Max(
                dto.UsagePercent,
                0);

        dto.FrequencyMhz =
            Math.Max(
                dto.FrequencyMhz,
                0);
    }
}