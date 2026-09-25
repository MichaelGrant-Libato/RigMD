using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Models;

namespace RigMD.Application.Services.Autonomy;

/// <summary>
/// Central mapping table for Scoped "Specific Parts" Scanning and Scenario-to-Tool ("Check a Problem") workflows.
/// Enforces strict tool filtering for component checks and deterministic Tier-0 tool selection for scenarios.
/// </summary>
public static class DiagnosticScopeMapper
{
    public static string NormalizeComponentId(string raw)
    {
        var key = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "ram" or "memory (ram)" or "memory" => "memory",
            "processor" or "processor (cpu)" or "cpu" => "cpu",
            "graphics" or "gpu / graphics" or "video" or "gpu" => "gpu",
            "disk" or "ssd" or "hdd" or "storage / ssd / hdd" or "storage" => "storage",
            "operating system" or "windows" or "os" => "os",
            "driver" or "device manager" or "drivers" => "drivers",
            "power" or "battery / power" or "battery" => "battery",
            "internet" or "wifi" or "network / internet" or "dns" or "network" => "network",
            "monitor" or "screen" or "display" => "display",
            _ => key
        };
    }

    public static string GetComponentDisplayName(string componentId)
    {
        return NormalizeComponentId(componentId) switch
        {
            "cpu" => "Processor (CPU)",
            "memory" => "Memory (RAM)",
            "gpu" => "GPU / Graphics",
            "storage" => "Storage / SSD / HDD",
            "os" => "Operating System",
            "drivers" => "Drivers",
            "battery" => "Battery / Power",
            "network" => "Network / Internet",
            "display" => "Display",
            _ => componentId
        };
    }

    public static string NormalizeScenarioId(string? rawScenarioId)
    {
        var key = (rawScenarioId ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
        return key switch
        {
            "slow-system" => "slow-system",
            "slow-boot" => "slow-boot",
            "blue-screen" or "blue-screen-crash" or "bsod" => "blue-screen-crash",
            "overheating-fan" or "overheating-loud-fan" or "overheating" => "overheating-loud-fan",
            "network-problem" => "network-problem",
            "storage-problem" => "storage-problem",
            "driver-error" or "driver-problem" => "driver-error",
            "no-display" or "display-problem" => "no-display",
            "app-crashes" or "application-crashes" => "app-crashes",
            "stuttering-freezing" => "stuttering-freezing",
            _ => key
        };
    }

    /// <summary>
    /// Returns the strictly allowed Tier-0 diagnostic tools and Tier-1/Tier-2 remediation tools
    /// for a "Check Specific Parts" (component mode) session.
    /// </summary>
    public static (HashSet<string> AllowedTier0Tools, HashSet<string> AllowedRemediationTools) GetAllowedToolsForComponents(
        IEnumerable<string> componentIds)
    {
        var tier0 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remediation = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in componentIds ?? Enumerable.Empty<string>())
        {
            switch (NormalizeComponentId(raw))
            {
                case "cpu":
                    tier0.Add("inspect_cpu_and_thermals");
                    remediation.Add("terminate_processes");
                    break;

                case "memory":
                    tier0.Add("inspect_memory_and_processes");
                    remediation.Add("terminate_processes");
                    remediation.Add("clear_browser_cache");
                    remediation.Add("clear_temp_files");
                    break;

                case "storage":
                    tier0.Add("inspect_storage_health");
                    remediation.Add("clear_temp_files");
                    remediation.Add("clear_browser_cache");
                    remediation.Add("clear_windows_update_cache");
                    break;

                case "gpu":
                case "display":
                    tier0.Add("inspect_gpu_and_displays");
                    tier0.Add("inspect_gpu_status");
                    remediation.Add("restart_windows_explorer");
                    break;

                case "drivers":
                    tier0.Add("inspect_gpu_and_displays");
                    tier0.Add("query_windows_event_logs");
                    remediation.Add("run_system_file_checker");
                    break;

                case "network":
                    tier0.Add("inspect_network_connectivity");
                    tier0.Add("inspect_dns");
                    remediation.Add("flush_dns_cache");
                    break;

                case "battery":
                    tier0.Add("inspect_battery_and_power");
                    remediation.Add("terminate_processes");
                    break;

                case "os":
                    tier0.Add("query_windows_event_logs");
                    tier0.Add("query_startup_apps");
                    tier0.Add("inspect_memory_and_processes");
                    remediation.Add("run_system_file_checker");
                    remediation.Add("clear_temp_files");
                    remediation.Add("clear_windows_update_cache");
                    remediation.Add("restart_windows_explorer");
                    break;
            }
        }

        return (tier0, remediation);
    }

    /// <summary>
    /// Explicit Scenario-to-Tool Mapping Table for "Check a Problem" workflows.
    /// Ensures the ReAct loop executes the exact Tier-0 tools relevant to the selected scenario.
    /// </summary>
    public static IReadOnlyList<ReActToolCallRequest> GetRequiredToolsForScenario(string? scenarioId)
    {
        var normalized = NormalizeScenarioId(scenarioId);

        return normalized switch
        {
            "slow-system" or "stuttering-freezing" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "inspect_cpu_and_thermals",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'slow_system': Inspecting CPU utilization, clock speed, and thermal state."
                },
                new()
                {
                    ToolName = "inspect_memory_and_processes",
                    ArgumentsJson = "{\"topN\":10,\"sortBy\":\"memory\"}",
                    Thought = "Scenario 'slow_system': Inspecting physical RAM utilization and top memory-consuming processes."
                },
                new()
                {
                    ToolName = "inspect_storage_health",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'slow_system': Inspecting storage volume free space, S.M.A.R.T. status, and reclaimable caches."
                }
            },

            "slow-boot" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "inspect_storage_health",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'slow_boot': Inspecting boot drive S.M.A.R.T. health, free space, and temporary startup caches."
                },
                new()
                {
                    ToolName = "query_startup_apps",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'slow_boot': Enumerating Windows startup applications and autorun entries via WMI/Registry."
                },
                new()
                {
                    ToolName = "query_windows_event_logs",
                    ArgumentsJson = "{\"logName\":\"System\",\"eventFocus\":\"Boot\",\"maxEvents\":12,\"hoursBack\":72}",
                    Thought = "Scenario 'slow_boot': Querying Windows Event Logs (Boot/Diagnostics-Performance Event 100 & System startup events)."
                }
            },

            "blue-screen-crash" or "app-crashes" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "query_windows_event_logs",
                    ArgumentsJson = "{\"logName\":\"System\",\"eventFocus\":\"BugCheck\",\"maxEvents\":15,\"hoursBack\":168}",
                    Thought = "Scenario 'blue_screen': Querying Windows Event Logs for BugCheck (Event 1001), Kernel-Power (Event 41), and critical stop errors."
                },
                new()
                {
                    ToolName = "inspect_gpu_and_displays",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'blue_screen': Inspecting GPU driver version, VRAM state, and display adapter status."
                },
                new()
                {
                    ToolName = "inspect_memory_and_processes",
                    ArgumentsJson = "{\"topN\":10,\"sortBy\":\"memory\"}",
                    Thought = "Scenario 'blue_screen': Inspecting physical RAM pressure, committed memory, and active workloads."
                },
                new()
                {
                    ToolName = "inspect_cpu_and_thermals",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'blue_screen': Checking CPU load and package temperature for thermal instability."
                }
            },

            "overheating-loud-fan" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "inspect_cpu_and_thermals",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'overheating_fan': Reading CPU package temperature, thermal throttling flags, and processor load."
                },
                new()
                {
                    ToolName = "inspect_gpu_and_displays",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'overheating_fan': Reading GPU core temperature, VRAM usage, and graphics load."
                },
                new()
                {
                    ToolName = "inspect_memory_and_processes",
                    ArgumentsJson = "{\"topN\":10,\"sortBy\":\"cpu\"}",
                    Thought = "Scenario 'overheating_fan': Identifying background processes driving sustained heat and fan activity."
                }
            },

            "network-problem" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "inspect_network_connectivity",
                    ArgumentsJson = "{\"targetHost\":\"one.one.one.one\"}",
                    Thought = "Scenario 'network_problem': Probing network adapter status, gateway, and live ICMP ping latency."
                },
                new()
                {
                    ToolName = "inspect_dns",
                    ArgumentsJson = "{\"targetHost\":\"cloudflare.com\"}",
                    Thought = "Scenario 'network_problem': Testing live DNS name resolution against external hostname."
                }
            },

            "storage-problem" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "inspect_storage_health",
                    ArgumentsJson = "{}",
                    Thought = "Scenario 'storage_problem': Inspecting physical drive S.M.A.R.T. health, volume free space, and reclaimable caches."
                }
            },

            "driver-error" or "no-display" => new List<ReActToolCallRequest>
            {
                new()
                {
                    ToolName = "inspect_gpu_and_displays",
                    ArgumentsJson = "{}",
                    Thought = $"Scenario '{normalized}': Inspecting GPU driver version, VRAM, and connected display status."
                },
                new()
                {
                    ToolName = "query_windows_event_logs",
                    ArgumentsJson = "{\"logName\":\"System\",\"maxEvents\":12,\"hoursBack\":72}",
                    Thought = $"Scenario '{normalized}': Querying Windows System Event Log for driver or display errors."
                }
            },

            _ => Array.Empty<ReActToolCallRequest>()
        };
    }
}
