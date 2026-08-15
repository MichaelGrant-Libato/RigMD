using RigMD.Application.Models;

namespace RigMD.Application.Services;

public class ResolutionResultDto
{
    public string resolution_status { get; set; } = "needs_recheck";
    public string resolution_checked_at { get; set; } = DateTime.UtcNow.ToString("o");
    public string resolution_summary { get; set; } = string.Empty;
    public object[] resolution_proof { get; set; } = Array.Empty<object>();
}

public class ResolutionService
{
    public ResolutionResultDto CheckResolution(
        string diagnosedCategory,
        HardwareProfileDto hardware)
    {
        var category =
            (diagnosedCategory ?? string.Empty).ToLowerInvariant();

        if (category.Contains("os performance"))
        {
            return CheckOsPerformanceResolution(hardware);
        }

        return new ResolutionResultDto
        {
            resolution_status = "needs_recheck",
            resolution_checked_at = DateTime.UtcNow.ToString("o"),
            resolution_summary =
                "This diagnosis type needs a follow-up symptom answer before RigMD can call it resolved.",
            resolution_proof = Array.Empty<object>()
        };
    }

    private static ResolutionResultDto CheckOsPerformanceResolution(
        HardwareProfileDto hardware)
    {
        var ramUsage =
            hardware.Ram.UsagePercent;

        var browserMemoryMb =
            hardware.ProcessInsights.BrowserMemoryMb;

        var browserProcessCount =
            hardware.ProcessInsights.BrowserProcessCount;

        var ramOk =
            ramUsage < 75;

        var browserOk =
            browserMemoryMb < 2048 &&
            browserProcessCount < 20;

        var resolved =
            ramOk &&
            browserOk;

        return new ResolutionResultDto
        {
            resolution_status =
                resolved
                    ? "resolved"
                    : "still_active",

            resolution_checked_at =
                DateTime.UtcNow.ToString("o"),

            resolution_summary =
                resolved
                    ? "The main RAM and browser pressure is no longer showing in the live scan."
                    : "The same RAM or browser pressure is still showing in the live scan.",

            resolution_proof = new object[]
            {
                new
                {
                    label = "RAM usage",
                    value = $"{ramUsage:0.##}%",
                    status =
                        ramOk
                            ? "resolved"
                            : "still high",
                    meaning =
                        "Below 75% is treated as resolved for this diagnosis."
                },

                new
                {
                    label = "Browser workload",
                    value =
                        $"{browserMemoryMb:0.##} MB across {browserProcessCount} processes",
                    status =
                        browserOk
                            ? "resolved"
                            : "still high",
                    meaning =
                        "Browser pressure is resolved when memory is below 2048 MB and process count is below 20."
                }
            }
        };
    }
}