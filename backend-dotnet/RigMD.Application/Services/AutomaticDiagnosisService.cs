using RigMD.Application.Models;

namespace RigMD.Application.Services;

public sealed class AutomaticDiagnosisService :
    IAutomaticDiagnosisService
{
    public AutomaticDiagnosisResult Diagnose(
        AutomaticDiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Hardware);

        var evidence =
            BuildEvidence(input.Hardware);

        var performanceFocus =
            IsPerformanceFocus(input);

        var storageFocus =
            IsStorageFocus(input);

        if (performanceFocus)
        {
            var result =
                DiagnosePerformance(
                    input.Hardware,
                    evidence);

            if (result != null)
            {
                return result;
            }
        }

        if (storageFocus)
        {
            var result =
                DiagnoseStorage(
                    input.Hardware,
                    evidence);

            if (result != null)
            {
                return result;
            }
        }

        return new AutomaticDiagnosisResult
        {
            DiagnosedCategory =
                "No Active Issue Detected",

            ActionCategory =
                "Monitor",

            ConfidenceLabel =
                evidence.Count >= 4
                    ? "Medium"
                    : "Low",

            Explanation =
                "The latest supported Agent evidence does not currently verify an active issue within the selected diagnosis scope.",

            RecommendedNextStep =
                "Continue monitoring the system and run another diagnosis if the problem occurs again.",

            Proof =
                evidence
        };
    }

    private static AutomaticDiagnosisResult?
        DiagnosePerformance(
            HardwareProfileDto hardware,
            IReadOnlyList<AutomaticDiagnosisProof> evidence)
    {
        var cpuUsage =
            hardware.Cpu.UsagePercent;

        var ramUsage =
            hardware.Ram.UsagePercent;

        var processes =
            hardware.ProcessInsights;

        /*
         * CPU is intentionally handled conservatively.
         *
         * The Agent currently supplies a fresh point-in-time
         * CPU measurement rather than a sustained utilization
         * window. A single elevated reading therefore should
         * not produce a high-confidence diagnosis.
         */
        if (cpuUsage >= 90)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Elevated CPU Utilization",

                ActionCategory =
                    "Troubleshoot",

                ConfidenceLabel =
                    "Medium",

                Explanation =
                    "The latest Agent scan captured very high processor utilization. A single CPU snapshot cannot prove sustained processor pressure, but the reading is significant enough to justify checking active workloads.",

                RecommendedNextStep =
                    "Review CPU usage in Task Manager and identify applications or background processes that remain consistently high before taking further action.",

                Proof =
                    evidence,

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "task_manager",

                        Label =
                            "Task Manager - Processes",

                        Description =
                            "Review current CPU utilization and identify processes that remain consistently CPU-intensive."
                    }
            };
        }

        if (ramUsage >= 90)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "High Memory Pressure",

                ActionCategory =
                    "Troubleshoot",

                ConfidenceLabel =
                    "High",

                Explanation =
                    "The latest Agent scan shows very high memory utilization. Available working memory is limited and may contribute to slowdowns, freezing, or application instability.",

                RecommendedNextStep =
                    "Open Task Manager and review the applications using the most memory. Close unnecessary workloads before deeper troubleshooting.",

                Proof =
                    evidence,

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "task_manager",

                        Label =
                            "Task Manager - Processes",

                        Description =
                            "Review applications currently consuming the most memory."
                    }
            };
        }

        if (
            ramUsage >= 80 &&
            processes != null &&
            processes.BrowserHeavy &&
            processes.BrowserMemoryMb >= 3000)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Elevated Memory Pressure From Active Workloads",

                ActionCategory =
                    "Troubleshoot",

                ConfidenceLabel =
                    "High",

                Explanation =
                    "The fresh Agent scan shows elevated memory utilization together with a browser-heavy workload. This combination may contribute to sluggish performance or intermittent freezing.",

                RecommendedNextStep =
                    "Review high-memory browser tabs and other large applications in Task Manager and reduce unnecessary workloads.",

                Proof =
                    evidence,

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "task_manager",

                        Label =
                            "Task Manager - Processes",

                        Description =
                            "Compare memory usage across active applications."
                    }
            };
        }

        if (ramUsage >= 80)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Elevated Memory Usage",

                ActionCategory =
                    "Monitor",

                ConfidenceLabel =
                    "Medium",

                Explanation =
                    "The latest Agent scan shows elevated memory utilization, but the available evidence does not yet identify a single verified workload as the cause.",

                RecommendedNextStep =
                    "Monitor memory usage and inspect high-memory applications if the system becomes slow or unresponsive.",

                Proof =
                    evidence,

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "task_manager",

                        Label =
                            "Task Manager - Processes",

                        Description =
                            "Review current memory utilization and the largest active applications."
                    }
            };
        }

        return null;
    }

    private static AutomaticDiagnosisResult?
        DiagnoseStorage(
            HardwareProfileDto hardware,
            IReadOnlyList<AutomaticDiagnosisProof> evidence)
    {
        var primaryDisk =
            hardware.AllDisks?
                .FirstOrDefault();

        if (primaryDisk == null)
        {
            return null;
        }

        if (primaryDisk.UsagePercent >= 90)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Low Available Storage Space",

                ActionCategory =
                    "Maintain",

                ConfidenceLabel =
                    "High",

                Explanation =
                    "The latest Agent scan shows that the primary storage volume is almost full. Limited free space can affect temporary files, Windows updates, and application performance.",

                RecommendedNextStep =
                    "Review storage usage and remove unnecessary temporary files or unused applications.",

                Proof =
                    evidence,

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "storage_settings",

                        Label =
                            "Windows Storage Settings",

                        Description =
                            "Review applications and files consuming space on the primary drive."
                    }
            };
        }

        if (primaryDisk.UsagePercent >= 80)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Elevated Storage Utilization",

                ActionCategory =
                    "Maintain",

                ConfidenceLabel =
                    "Medium",

                Explanation =
                    "The primary storage volume is becoming heavily utilized, although it is not yet critically full.",

                RecommendedNextStep =
                    "Review storage usage and remove unnecessary files before free space becomes critically low.",

                Proof =
                    evidence,

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "storage_settings",

                        Label =
                            "Windows Storage Settings",

                        Description =
                            "Review current storage usage."
                    }
            };
        }

        return null;
    }

    private static List<AutomaticDiagnosisProof>
        BuildEvidence(
            HardwareProfileDto hardware)
    {
        var proof =
            new List<AutomaticDiagnosisProof>();

        // -------------------------------------------------------
        // CPU
        // -------------------------------------------------------

        var cpuUsage =
            hardware.Cpu.UsagePercent;

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "CPU Usage",

                Value =
                    $"{cpuUsage:0.0}%",

                Status =
                    cpuUsage >= 90
                        ? "high"
                        : cpuUsage >= 80
                            ? "elevated"
                            : "normal",

                Meaning =
                    cpuUsage >= 90
                        ? "The Agent captured very high processor utilization."
                        : cpuUsage >= 80
                            ? "Processor utilization is elevated in the current snapshot."
                            : "Processor utilization is within the normal diagnostic range."
            });

        if (hardware.Cpu.FrequencyMhz > 0)
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "CPU Frequency",

                    Value =
                        $"{hardware.Cpu.FrequencyMhz:0} MHz",

                    Status =
                        "observed",

                    Meaning =
                        "Current processor frequency reported by the installed Agent."
                });
        }

        // -------------------------------------------------------
        // MEMORY
        // -------------------------------------------------------

        var ramUsage =
            hardware.Ram.UsagePercent;

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "Memory Usage",

                Value =
                    $"{ramUsage:0.0}%",

                Status =
                    ramUsage >= 90
                        ? "high"
                        : ramUsage >= 80
                            ? "elevated"
                            : "normal",

                Meaning =
                    ramUsage >= 90
                        ? "Current memory pressure is very high."
                        : ramUsage >= 80
                            ? "Current memory utilization is elevated."
                            : "Current memory utilization is within the normal diagnostic range."
            });

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "Memory Capacity",

                Value =
                    $"{hardware.Ram.UsedGb:0.0} GB of {hardware.Ram.TotalGb:0.0} GB",

                Status =
                    "observed",

                Meaning =
                    "Live memory capacity and utilization reported by the installed Agent."
            });

        // -------------------------------------------------------
        // PROCESSES
        // -------------------------------------------------------

        if (hardware.ProcessInsights != null)
        {
            var processes =
                hardware.ProcessInsights;

            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Browser Workload",

                    Value =
                        processes.BrowserHeavy
                            ? "Heavy"
                            : "Normal",

                    Status =
                        processes.BrowserHeavy
                            ? "elevated"
                            : "normal",

                    Meaning =
                        processes.BrowserHeavy
                            ? "The Agent detected a browser-heavy workload."
                            : "The Agent did not detect unusual browser workload pressure."
                });

            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Browser Memory",

                    Value =
                        $"{processes.BrowserMemoryMb:0} MB",

                    Status =
                        processes.BrowserMemoryMb >= 4000
                            ? "elevated"
                            : "observed",

                    Meaning =
                        "Combined memory currently used by detected browser processes."
                });
        }

        // -------------------------------------------------------
        // STORAGE
        // -------------------------------------------------------

        var primaryDisk =
            hardware.AllDisks?
                .FirstOrDefault();

        if (primaryDisk != null)
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Primary Disk Usage",

                    Value =
                        $"{primaryDisk.UsagePercent:0.0}%",

                    Status =
                        primaryDisk.UsagePercent >= 90
                            ? "high"
                            : primaryDisk.UsagePercent >= 80
                                ? "elevated"
                                : "normal",

                    Meaning =
                        primaryDisk.UsagePercent >= 90
                            ? "The primary storage volume is almost full."
                            : primaryDisk.UsagePercent >= 80
                                ? "Primary storage utilization is elevated."
                                : "Primary storage utilization is within the normal diagnostic range."
                });
        }

        return proof;
    }

    private static bool IsPerformanceFocus(
        AutomaticDiagnosisInput input)
    {
        if (
            input.Mode.Equals(
                "full",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (
            input.ComponentIds.Any(
                value =>
                    value.Equals(
                        "cpu",
                        StringComparison.OrdinalIgnoreCase) ||
                    value.Equals(
                        "processor",
                        StringComparison.OrdinalIgnoreCase) ||
                    value.Equals(
                        "memory",
                        StringComparison.OrdinalIgnoreCase) ||
                    value.Equals(
                        "os",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return
            string.Equals(
                input.ScenarioId,
                "slow-system",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                input.ScenarioId,
                "stuttering-freezing",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStorageFocus(
        AutomaticDiagnosisInput input)
    {
        if (
            input.Mode.Equals(
                "full",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (
            input.ComponentIds.Any(
                value =>
                    value.Equals(
                        "storage",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(
            input.ScenarioId,
            "storage-problem",
            StringComparison.OrdinalIgnoreCase);
    }
}