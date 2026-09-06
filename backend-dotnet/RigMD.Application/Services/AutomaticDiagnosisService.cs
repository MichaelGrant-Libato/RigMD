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

        var evaluateCpu =
            ShouldEvaluateCpu(input);

        var evaluateMemory =
            ShouldEvaluateMemory(input);

        var evaluateStorage =
            ShouldEvaluateStorage(input);

        var evaluateNetwork =
            ShouldEvaluateNetwork(input);

        var evidence =
            BuildEvidence(
                input,
                evaluateCpu,
                evaluateMemory,
                evaluateStorage,
                evaluateNetwork);

        if (evaluateCpu)
        {
            var result =
                DiagnoseCpu(
                    input.Hardware,
                    evidence);

            if (result != null)
            {
                return result;
            }
        }

        if (evaluateMemory)
        {
            var result =
                DiagnoseMemory(
                    input.Hardware,
                    evidence);

            if (result != null)
            {
                return result;
            }
        }

        if (evaluateStorage)
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

        if (evaluateNetwork)
        {
            var result =
                DiagnoseNetwork(
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
                evidence.Count >= 2
                    ? "Medium"
                    : "Low",

            Explanation =
                "The latest supported Agent evidence does not currently verify an active issue within the selected diagnosis scope.",

            RecommendedNextStep =
                GetMonitoringRecommendation(
                    input),

            Proof =
                evidence
        };
    }

    // =======================================================
    // CPU DIAGNOSIS
    // =======================================================

    private static AutomaticDiagnosisResult?
        DiagnoseCpu(
            HardwareProfileDto hardware,
            IReadOnlyList<AutomaticDiagnosisProof> evidence)
    {
        var cpuUsage =
            hardware.Cpu.UsagePercent;

        if (hardware.Cpu.IsThermallyThrottling)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Thermal condition",

                ActionCategory =
                    "Troubleshoot",

                ConfidenceLabel =
                    "Medium",

                Explanation =
                    "The latest scan detected high CPU utilization together with a lower-than-expected processor frequency. This may indicate CPU frequency throttling, but the current telemetry does not directly confirm overheating.",

                RecommendedNextStep =
                    "Reduce heavy workloads, check cooling and airflow, and monitor CPU behavior again. If the system repeatedly slows down, shuts down, or shows other heat-related symptoms, have the cooling system inspected.",

                Proof =
                    new List<AutomaticDiagnosisProof>(
                        evidence)
                    {
                        new()
                        {
                            Label =
                                "Possible CPU Frequency Throttling",

                            Value =
                                "Observed",

                            Status =
                                "elevated",

                            Meaning =
                                "High processor utilization was observed while the reported frequency was below the expected maximum."
                        }
                    },

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "task_manager",

                        Label =
                            "Task Manager - Performance",

                        Description =
                            "Review CPU utilization and reported frequency. The current evidence suggests possible frequency throttling but does not directly confirm overheating."
                    }
            };
        }

        /*
         * CPU utilization is intentionally interpreted
         * conservatively.
         *
         * The Agent currently captures a fresh point-in-time
         * processor reading rather than a sustained utilization
         * window. A single high reading therefore does not
         * justify a high-confidence diagnosis.
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
                    "The latest Agent scan captured very high processor utilization. Because this is a point-in-time measurement, RigMD cannot yet confirm that the processor load is sustained.",

                RecommendedNextStep =
                    "Review CPU usage in Task Manager and identify applications or background processes that remain consistently CPU-intensive.",

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

        return null;
    }

    // =======================================================
    // MEMORY DIAGNOSIS
    // =======================================================

    private static AutomaticDiagnosisResult?
        DiagnoseMemory(
            HardwareProfileDto hardware,
            IReadOnlyList<AutomaticDiagnosisProof> evidence)
    {
        var ramUsage =
            hardware.Ram.UsagePercent;

        var processes =
            hardware.ProcessInsights;

        if (!string.IsNullOrEmpty(
                processes?.MemoryLeakWarning))
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "OS performance degradation",

                ActionCategory =
                    "Troubleshoot",

                ConfidenceLabel =
                    "Medium",

                Explanation =
                    "A process is consuming an unusually large amount of memory. This may contribute to system slowdown, but a single high-memory reading does not by itself confirm a memory leak.",

                RecommendedNextStep =
                    "Open Task Manager, identify the high-memory process, and monitor whether its memory usage continues to grow over time. Close or restart the application if it is causing performance problems.",

                Proof =
                    new List<AutomaticDiagnosisProof>(
                        evidence)
                    {
                        new()
                        {
                            Label =
                                "Unusually High Process Memory Usage",

                            Value =
                                processes.MemoryLeakWarning,

                            Status =
                                "elevated",

                            Meaning =
                                "A process is using an unusually large amount of memory."
                        }
                    },

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "task_manager",

                        Label =
                            "Task Manager - Processes",

                        Description =
                            "Review the high-memory process and monitor whether its memory usage continues increasing."
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

    // =======================================================
    // STORAGE DIAGNOSIS
    // =======================================================

    private static AutomaticDiagnosisResult?
        DiagnoseStorage(
            HardwareProfileDto hardware,
            IReadOnlyList<AutomaticDiagnosisProof> evidence)
    {
        if (hardware.StorageDrives.Any(
                drive =>
                    drive.IsFailingSmart))
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Storage health behavior",

                ActionCategory =
                    "Escalate for Professional Inspection",

                ConfidenceLabel =
                    "High",

                Explanation =
                    "Windows reported an abnormal health status for a physical storage device. This indicates a storage health warning, but the current scan does not directly confirm a SMART predictive-failure condition.",

                RecommendedNextStep =
                    "Back up important data promptly and verify the drive using a dedicated storage-health or SMART diagnostic tool before deciding whether replacement is necessary.",

                Proof =
                    new List<AutomaticDiagnosisProof>(
                        evidence)
                    {
                        new()
                        {
                            Label =
                                "Storage Health Warning",

                            Value =
                                "Windows reported a non-OK physical drive status",

                            Status =
                                "high",

                            Meaning =
                                "The physical drive reported an abnormal Windows health status."
                        }
                    },

                VerificationTarget =
                    new AutomaticVerificationTarget
                    {
                        Target =
                            "professional_inspection",

                        Label =
                            "Professional Inspection",

                        Description =
                            "Back up important data and verify the drive using a dedicated storage-health or SMART diagnostic tool."
                    }
            };
        }

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

    // =======================================================
    // NETWORK DIAGNOSIS
    // =======================================================

    private static AutomaticDiagnosisResult?
        DiagnoseNetwork(
            HardwareProfileDto hardware,
            IReadOnlyList<AutomaticDiagnosisProof> evidence)
    {
        var network =
            hardware.Network;

        /*
         * Flush DNS is only appropriate when basic network
         * configuration is present but DNS name resolution fails.
         *
         * Missing adapters, IPv4 configuration, gateways, or DNS
         * servers indicate a different network problem and must not
         * automatically trigger DNS-cache remediation.
         */
        if (
            network.HasActiveAdapter &&
            network.HasIpv4Address &&
            network.HasDefaultGateway &&
            network.HasDnsServers &&
            !network.DnsResolutionSucceeded)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory =
                    "Network issue",

                ActionCategory =
                    "Troubleshoot",

                ConfidenceLabel =
                    "High",

                Explanation =
                    "The installed RigMD Agent detected an active network connection with IPv4, a default gateway, and configured DNS servers, but the DNS resolution test failed. The available evidence is consistent with a DNS resolution problem.",

                RecommendedNextStep =
                    "Flush the Windows DNS resolver cache, then repeat the network diagnosis to verify whether name resolution succeeds.",

                Proof =
                    evidence
            };
        }

        return null;
    }

    // =======================================================
    // EVIDENCE BUILDING
    // =======================================================

    private static List<AutomaticDiagnosisProof>
        BuildEvidence(
            AutomaticDiagnosisInput input,
            bool evaluateCpu,
            bool evaluateMemory,
            bool evaluateStorage,
            bool evaluateNetwork)
    {
        var proof =
            new List<AutomaticDiagnosisProof>();

        var hardware =
            input.Hardware;

        var isFull =
            IsFullMode(input);

        var gpuSelected =
            IsComponentSelected(
                input,
                "gpu",
                "graphics");

        var osSelected =
            IsComponentSelected(
                input,
                "os",
                "operating-system",
                "operating_system");

        /*
         * Full mode contains evidence from all currently supported
         * general component groups.
         *
         * Network evidence is intentionally excluded from Full mode
         * because DNS resolution depends on an external resource and
         * should only be collected when the user explicitly selects a
         * network diagnosis scope.
         */

        if (evaluateCpu)
        {
            AddCpuEvidence(
                proof,
                hardware);
        }

        if (evaluateMemory)
        {
            AddMemoryEvidence(
                proof,
                hardware);
        }

        if (evaluateStorage)
        {
            AddStorageEvidence(
                proof,
                hardware);
        }

        if (evaluateNetwork)
        {
            AddNetworkEvidence(
                proof,
                hardware);
        }

        if (gpuSelected || isFull)
        {
            AddGpuEvidence(
                proof,
                hardware);
        }

        if (osSelected || isFull)
        {
            AddOperatingSystemEvidence(
                proof,
                hardware);
        }

        return proof;
    }

    private static void AddCpuEvidence(
        ICollection<AutomaticDiagnosisProof> proof,
        HardwareProfileDto hardware)
    {
        var cpu =
            hardware.Cpu;

        var cpuUsage =
            cpu.UsagePercent;

        if (!string.IsNullOrWhiteSpace(
                cpu.Name))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Processor",

                    Value =
                        cpu.Name,

                    Status =
                        "observed",

                    Meaning =
                        "Processor identified by the installed RigMD Agent."
                });
        }

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

        if (cpu.FrequencyMhz > 0)
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "CPU Frequency",

                    Value =
                        $"{cpu.FrequencyMhz:0} MHz",

                    Status =
                        "observed",

                    Meaning =
                        "Current processor frequency reported by the installed Agent."
                });
        }

        if (
            cpu.Cores > 0 ||
            cpu.Threads > 0)
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "CPU Configuration",

                    Value =
                        $"{cpu.Cores} cores / {cpu.Threads} threads",

                    Status =
                        "observed",

                    Meaning =
                        "Processor core and logical thread configuration detected by the Agent."
                });
        }
    }

    private static void AddMemoryEvidence(
        ICollection<AutomaticDiagnosisProof> proof,
        HardwareProfileDto hardware)
    {
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
    }

    private static void AddStorageEvidence(
        ICollection<AutomaticDiagnosisProof> proof,
        HardwareProfileDto hardware)
    {
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

            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Storage Capacity",

                    Value =
                        $"{primaryDisk.UsedGb:0.0} GB of {primaryDisk.TotalGb:0.0} GB",

                    Status =
                        "observed",

                    Meaning =
                        "Used and total storage capacity reported by the Agent."
                });
        }

        if (!string.IsNullOrWhiteSpace(
                hardware.PrimaryStorageType))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Storage Type",

                    Value =
                        hardware.PrimaryStorageType,

                    Status =
                        "observed",

                    Meaning =
                        "Primary storage technology detected by the Agent."
                });
        }

        var primaryStorage =
            hardware.StorageDrives?
                .FirstOrDefault();

        if (
            primaryStorage != null &&
            !string.IsNullOrWhiteSpace(
                primaryStorage.Model))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Storage Device",

                    Value =
                        primaryStorage.Model,

                    Status =
                        "observed",

                    Meaning =
                        "Physical storage device identified by the Agent."
                });
        }
    }

    private static void AddNetworkEvidence(
        ICollection<AutomaticDiagnosisProof> proof,
        HardwareProfileDto hardware)
    {
        var network =
            hardware.Network;

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "Active Network Adapter",

                Value =
                    network.HasActiveAdapter
                        ? network.AdapterName
                        : "Not detected",

                Status =
                    network.HasActiveAdapter
                        ? "normal"
                        : "issue",

                Meaning =
                    network.HasActiveAdapter
                        ? "The Agent detected an active Windows network adapter."
                        : "The Agent did not detect an active network adapter."
            });

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "IPv4 Configuration",

                Value =
                    network.HasIpv4Address
                        ? "Available"
                        : "Unavailable",

                Status =
                    network.HasIpv4Address
                        ? "normal"
                        : "issue",

                Meaning =
                    network.HasIpv4Address
                        ? "The active adapter has an IPv4 address."
                        : "The active adapter does not currently have an IPv4 address."
            });

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "Default Gateway",

                Value =
                    network.HasDefaultGateway
                        ? "Available"
                        : "Unavailable",

                Status =
                    network.HasDefaultGateway
                        ? "normal"
                        : "issue",

                Meaning =
                    network.HasDefaultGateway
                        ? "A default IPv4 gateway is configured."
                        : "No default IPv4 gateway was detected."
            });

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "DNS Servers",

                Value =
                    network.HasDnsServers
                        ? "Configured"
                        : "Not detected",

                Status =
                    network.HasDnsServers
                        ? "normal"
                        : "issue",

                Meaning =
                    network.HasDnsServers
                        ? "The active adapter has DNS servers configured."
                        : "The Agent did not detect DNS servers for the active adapter."
            });

        proof.Add(
            new AutomaticDiagnosisProof
            {
                Label =
                    "DNS Resolution",

                Value =
                    network.DnsResolutionSucceeded
                        ? "Succeeded"
                        : "Failed",

                Status =
                    network.DnsResolutionSucceeded
                        ? "normal"
                        : "issue",

                Meaning =
                    string.IsNullOrWhiteSpace(
                        network.DnsResolutionMessage)
                        ? "DNS resolution test completed."
                        : network.DnsResolutionMessage
            });
    }

    private static void AddGpuEvidence(
        ICollection<AutomaticDiagnosisProof> proof,
        HardwareProfileDto hardware)
    {
        var gpu =
            hardware.Gpu;

        if (!string.IsNullOrWhiteSpace(
                gpu.Name))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Graphics Processor",

                    Value =
                        gpu.Name,

                    Status =
                        "observed",

                    Meaning =
                        "Graphics processor identified by the installed Agent."
                });
        }

        if (!string.IsNullOrWhiteSpace(
                gpu.Type))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "GPU Type",

                    Value =
                        gpu.Type,

                    Status =
                        "observed",

                    Meaning =
                        "Graphics processor type reported by the Agent."
                });
        }

        if (!string.IsNullOrWhiteSpace(
                gpu.Driver))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "GPU Driver",

                    Value =
                        gpu.Driver,

                    Status =
                        "observed",

                    Meaning =
                        "Installed graphics driver version reported by Windows."
                });
        }

        if (gpu.VramGb > 0)
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Video Memory",

                    Value =
                        $"{gpu.VramGb:0.0} GB",

                    Status =
                        "observed",

                    Meaning =
                        "Graphics memory capacity reported by the Agent."
                });
        }
    }

    private static void AddOperatingSystemEvidence(
        ICollection<AutomaticDiagnosisProof> proof,
        HardwareProfileDto hardware)
    {
        if (!string.IsNullOrWhiteSpace(
                hardware.OsVersion))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Operating System",

                    Value =
                        hardware.OsVersion,

                    Status =
                        "observed",

                    Meaning =
                        "Windows version reported by the installed Agent."
                });
        }

        if (!string.IsNullOrWhiteSpace(
                hardware.DeviceName))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Device Name",

                    Value =
                        hardware.DeviceName,

                    Status =
                        "observed",

                    Meaning =
                        "Windows device identity associated with this Agent."
                });
        }

        if (!string.IsNullOrWhiteSpace(
                hardware.ChipsetDriver))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "Chipset / Platform",

                    Value =
                        hardware.ChipsetDriver,

                    Status =
                        "observed",

                    Meaning =
                        "Platform or chipset information reported by the Agent."
                });
        }

        if (!string.IsNullOrWhiteSpace(
                hardware.SystemAge))
        {
            proof.Add(
                new AutomaticDiagnosisProof
                {
                    Label =
                        "System Age",

                    Value =
                        hardware.SystemAge,

                    Status =
                        "observed",

                    Meaning =
                        "Estimated Windows system age reported by the Agent."
                });
        }
    }

    // =======================================================
    // SCOPE RULES
    // =======================================================

    private static bool ShouldEvaluateCpu(
        AutomaticDiagnosisInput input)
    {
        if (IsFullMode(input))
        {
            return true;
        }

        if (
            IsComponentSelected(
                input,
                "cpu",
                "processor"))
        {
            return true;
        }

        return
            IsScenario(
                input,
                "slow-system") ||
            IsScenario(
                input,
                "stuttering-freezing");
    }

    private static bool ShouldEvaluateMemory(
        AutomaticDiagnosisInput input)
    {
        if (IsFullMode(input))
        {
            return true;
        }

        if (
            IsComponentSelected(
                input,
                "memory",
                "ram"))
        {
            return true;
        }

        return
            IsScenario(
                input,
                "slow-system") ||
            IsScenario(
                input,
                "stuttering-freezing");
    }

    private static bool ShouldEvaluateStorage(
        AutomaticDiagnosisInput input)
    {
        if (IsFullMode(input))
        {
            return true;
        }

        if (
            IsComponentSelected(
                input,
                "storage",
                "disk",
                "drive"))
        {
            return true;
        }

        return IsScenario(
            input,
            "storage-problem");
    }

    private static bool ShouldEvaluateNetwork(
        AutomaticDiagnosisInput input)
    {
        if (
            IsComponentSelected(
                input,
                "network",
                "network-adapter",
                "network_adapter"))
        {
            return true;
        }

        return IsScenario(
            input,
            "network-problem");
    }

    private static bool IsFullMode(
        AutomaticDiagnosisInput input)
    {
        return string.Equals(
            input.Mode,
            "full",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComponentSelected(
        AutomaticDiagnosisInput input,
        params string[] componentIds)
    {
        return input.ComponentIds.Any(
            selected =>
                componentIds.Any(
                    expected =>
                        string.Equals(
                            selected,
                            expected,
                            StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsScenario(
        AutomaticDiagnosisInput input,
        string scenarioId)
    {
        return string.Equals(
            input.ScenarioId,
            scenarioId,
            StringComparison.OrdinalIgnoreCase);
    }

    // =======================================================
    // USER GUIDANCE
    // =======================================================

    private static string GetMonitoringRecommendation(
        AutomaticDiagnosisInput input)
    {
        if (
            IsComponentSelected(
                input,
                "cpu",
                "processor"))
        {
            return
                "Continue monitoring processor utilization and run another CPU diagnosis if sustained high usage or performance problems occur.";
        }

        if (
            IsComponentSelected(
                input,
                "memory",
                "ram"))
        {
            return
                "Continue monitoring memory utilization and run another diagnosis if memory usage becomes consistently elevated.";
        }

        if (
            IsComponentSelected(
                input,
                "storage",
                "disk",
                "drive"))
        {
            return
                "Continue monitoring available storage space and run another diagnosis if storage utilization increases significantly.";
        }

        if (
            IsComponentSelected(
                input,
                "gpu",
                "graphics"))
        {
            return
                "Continue monitoring graphics behavior and run another diagnosis if display instability, driver problems, crashes, or graphics performance issues occur.";
        }

        if (
            IsComponentSelected(
                input,
                "os",
                "operating-system",
                "operating_system"))
        {
            return
                "Continue monitoring Windows behavior and run another diagnosis if operating system errors or instability occur.";
        }

        if (
            IsComponentSelected(
                input,
                "network",
                "network-adapter",
                "network_adapter") ||
            IsScenario(
                input,
                "network-problem"))
        {
            return
                "The current network evidence does not verify a DNS resolution problem. Continue monitoring connectivity and repeat the network diagnosis if websites or network names fail to resolve.";
        }

        return
            "Continue monitoring the system and run another diagnosis if the problem occurs again.";
    }
}