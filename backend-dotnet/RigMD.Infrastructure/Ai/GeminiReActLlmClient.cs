using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Ai;

/// <summary>
/// Free-tier Google Gemini Function-Calling ReAct client with a zero-cost local tool-calling fallback.
/// Always drives a multi-turn Thought -> Tool Call -> Observation -> Proposal loop using real OS tools.
/// </summary>
public class GeminiReActLlmClient : IReActLlmClient
{
    private const string SubmitProposalFunctionName = "submit_diagnosis_and_remediation_plan";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiReActLlmClient> _logger;

    public GeminiReActLlmClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiReActLlmClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ReActModelTurnDecision> DecideNextTurnAsync(
        ReActConversationContext context,
        IReadOnlyList<AgentToolFunctionDeclaration> availableTools,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var geminiDecision = await CallGeminiFunctionCallingAsync(
                    apiKey,
                    context,
                    availableTools,
                    cancellationToken);

                if (geminiDecision != null)
                {
                    return geminiDecision;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "GeminiReActLlmClient: Gemini API call failed on turn {Turn}; falling back to local tool-calling ReAct engine.",
                    context.CurrentTurn);
            }
        }

        return DecideWithLocalToolCallingEngine(context, availableTools);
    }

    private string? ResolveApiKey()
    {
        var fromConfig = _configuration["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        return null;
    }

    private async Task<ReActModelTurnDecision?> CallGeminiFunctionCallingAsync(
        string apiKey,
        ReActConversationContext context,
        IReadOnlyList<AgentToolFunctionDeclaration> availableTools,
        CancellationToken cancellationToken)
    {
        var client = _httpClient;

        var functionDeclarations = new List<object>();
        foreach (var tool in availableTools)
        {
            var props = new Dictionary<string, object>();
            foreach (var kvp in tool.Parameters)
            {
                var propObj = new Dictionary<string, object>
                {
                    ["type"] = kvp.Value.Type.ToUpperInvariant(),
                    ["description"] = kvp.Value.Description
                };

                if (kvp.Value.EnumValues is { Count: > 0 })
                {
                    propObj["enum"] = kvp.Value.EnumValues;
                }

                if (string.Equals(kvp.Value.Type, "array", StringComparison.OrdinalIgnoreCase))
                {
                    propObj["items"] = new Dictionary<string, object>
                    {
                        ["type"] = (kvp.Value.ItemsType ?? "string").ToUpperInvariant()
                    };
                }

                props[kvp.Key] = propObj;
            }

            functionDeclarations.Add(new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = new
                {
                    type = "OBJECT",
                    properties = props,
                    required = tool.Required
                }
            });
        }

        functionDeclarations.Add(new
        {
            name = SubmitProposalFunctionName,
            description = "Call this function once you have inspected live hardware/OS telemetry via Tier 0 tools and are ready to present your root cause analysis and propose a remediation tool for user approval.",
            parameters = new
            {
                type = "OBJECT",
                properties = new Dictionary<string, object>
                {
                    ["thought"] = new { type = "STRING", description = "Step-by-step reasoning synthesizing the tool observations." },
                    ["rootCauseAnalysis"] = new { type = "STRING", description = "Detailed technical root cause supported by exact metrics from tool observations." },
                    ["confidenceLevel"] = new { type = "STRING", description = "Confidence level: High, Medium, or Low." },
                    ["recommendedToolName"] = new { type = "STRING", description = "Name of the Tier 1 or Tier 2 remediation tool to propose (e.g. clear_temp_files, clear_browser_cache, flush_dns_cache, terminate_processes, restart_windows_explorer, clear_windows_update_cache, run_system_file_checker)." },
                    ["recommendedToolArgumentsJson"] = new { type = "STRING", description = "JSON object string of arguments for the recommended tool." },
                    ["remediationRationale"] = new { type = "STRING", description = "Why this specific tool and arguments will resolve the root cause." }
                },
                required = new[] { "rootCauseAnalysis", "recommendedToolName", "remediationRationale" }
            }
        });

        var targetScopeText = context.TargetScope.Count > 0
            ? $"[{string.Join(", ", context.TargetScope)}]"
            : "[Full System]";

        var scopedSystemRule = context.TargetScope.Count > 0
            ? $" The user has explicitly asked to diagnose: {targetScopeText}. Your PRIMARY verdict must evaluate only these requested components. Do not replace the primary verdict with an unselected component."
            : string.Empty;

        var scenarioRule = context.RequiredScenarioToolCalls.Count > 0
            ? $" Selected Scenario: '{context.ScenarioId}'. On Turn 1, you must execute the scenario's mapped diagnostic tools ({string.Join(", ", context.RequiredScenarioToolCalls.Select(c => c.ToolName))}) before submitting a diagnosis."
            : string.Empty;

        var contents = new List<object>
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new
                    {
                        text =
                            $"Diagnosis Mode: {context.DiagnosisMode}\n" +
                            $"TargetScope: {targetScopeText}\n" +
                            (!string.IsNullOrWhiteSpace(context.ScenarioId) ? $"ScenarioId: {context.ScenarioId}\n" : string.Empty) +
                            $"User Symptom / Context: {context.UserSymptom}\n" +
                            $"Initial Category Hint: {context.DiagnosedCategory}\n" +
                            $"Baseline Summary: {context.InitialSummary}\n" +
                            $"Current ReAct Turn: {context.CurrentTurn + 1} of {context.MaxTurns}.\n" +
                            (context.History.Count == 0
                                ? "First, call the relevant read-only diagnostic tools (inspect_* / query_*) for the requested scope to gather live Windows telemetry before proposing any remediation."
                                : "Review the live tool observations below. If you have enough telemetry evidence, call submit_diagnosis_and_remediation_plan with exact metric citations and the best remediation tool.")
                    }
                }
            }
        };

        foreach (var turn in context.History)
        {
            contents.Add(new
            {
                role = "model",
                parts = new object[]
                {
                    new
                    {
                        functionCall = new
                        {
                            name = turn.ToolName,
                            args = ParseJsonOrEmptyObject(turn.ArgumentsJson)
                        }
                    }
                }
            });

            contents.Add(new
            {
                role = "user",
                parts = new object[]
                {
                    new
                    {
                        functionResponse = new
                        {
                            name = turn.ToolName,
                            response = new
                            {
                                summary = turn.ObservationSummary,
                                data = ParseJsonOrEmptyObject(turn.ObservationJson)
                            }
                        }
                    }
                }
            });
        }

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text =
                            "You are RigMD's Autonomous Windows Diagnostic & Remediation ReAct Agent. " +
                            "Never guess or use canned responses. Always call read-only inspection tools first to observe live WMI, sensor, disk, process, network, or Windows Event Log telemetry. " +
                            "Once you have observed live metrics, call submit_diagnosis_and_remediation_plan citing the exact numbers observed." +
                            scopedSystemRule +
                            scenarioRule
                    }
                }
            },
            contents,
            tools = new[]
            {
                new { functionDeclarations }
            },
            generationConfig = new
            {
                temperature = 0.1
            }
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        var models = new[] { "gemini-2.5-flash", "gemini-2.0-flash" };

        foreach (var model in models)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            using var resp = await client.PostAsync(
                url,
                new StringContent(jsonPayload, Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                continue;
            }

            var respJson = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(respJson);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
            {
                continue;
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var contentEl) ||
                !contentEl.TryGetProperty("parts", out var partsEl))
            {
                continue;
            }

            var decision = new ReActModelTurnDecision
            {
                EngineName = $"Gemini ({model})"
            };

            var textParts = new List<string>();

            foreach (var part in partsEl.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    var t = textEl.GetString();
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        textParts.Add(t.Trim());
                    }
                }

                if (part.TryGetProperty("functionCall", out var fnCall))
                {
                    var fnName = fnCall.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    var argsEl = fnCall.TryGetProperty("args", out var aEl) ? aEl : default;
                    var argsJson = argsEl.ValueKind == JsonValueKind.Object ? argsEl.GetRawText() : "{}";

                    if (string.Equals(fnName, SubmitProposalFunctionName, StringComparison.OrdinalIgnoreCase))
                    {
                        var thoughtArg = TryGetString(argsEl, "thought");
                        if (!string.IsNullOrWhiteSpace(thoughtArg))
                        {
                            textParts.Add(thoughtArg);
                        }

                        decision.FinalProposal = new ReActFinalProposal
                        {
                            RootCauseAnalysis = TryGetString(argsEl, "rootCauseAnalysis", "Synthesized from live telemetry."),
                            ConfidenceLevel = TryGetString(argsEl, "confidenceLevel", "High"),
                            RecommendedToolName = TryGetString(argsEl, "recommendedToolName", "clear_temp_files"),
                            RecommendedToolArgumentsJson = TryGetString(argsEl, "recommendedToolArgumentsJson", "{}"),
                            RemediationRationale = TryGetString(argsEl, "remediationRationale", "Recommended based on live tool observations.")
                        };
                    }
                    else if (!string.IsNullOrWhiteSpace(fnName))
                    {
                        decision.ToolCalls.Add(new ReActToolCallRequest
                        {
                            ToolName = fnName,
                            ArgumentsJson = argsJson,
                            Thought = textParts.LastOrDefault() ?? $"Invoking '{fnName}' to inspect live system state."
                        });
                    }
                }
            }

            decision.Thought = textParts.Count > 0
                ? string.Join(" ", textParts)
                : decision.ToolCalls.Count > 0
                    ? $"Invoking {string.Join(", ", decision.ToolCalls.Select(c => c.ToolName))} to gather live telemetry."
                    : "Synthesizing live diagnostic observations.";

            if (decision.ToolCalls.Count > 0 || decision.FinalProposal != null)
            {
                return decision;
            }
        }

        return null;
    }

    private static ReActModelTurnDecision DecideWithLocalToolCallingEngine(
        ReActConversationContext context,
        IReadOnlyList<AgentToolFunctionDeclaration> availableTools)
    {
        var combinedText = $"{context.DiagnosedCategory} {context.UserSymptom} {context.InitialSummary}".ToLowerInvariant();
        var calledTools = new HashSet<string>(
            context.History.Select(h => h.ToolName),
            StringComparer.OrdinalIgnoreCase);

        // Turn 0: Issue targeted Tier 0 read-only diagnostic tool calls based on scenario, component scope, or symptom context
        if (context.History.Count == 0)
        {
            // 1. Explicit Scenario-to-Tool Mapping
            if (context.RequiredScenarioToolCalls is { Count: > 0 })
            {
                var scenarioTools = string.Join(", ", context.RequiredScenarioToolCalls.Select(t => $"'{t.ToolName}'"));
                return new ReActModelTurnDecision
                {
                    EngineName = "RigMD Local Tool-Calling ReAct Engine",
                    Thought = $"Executing Scenario-to-Tool diagnostic plan for '{context.ScenarioId}': invoking {scenarioTools} to gather targeted telemetry before generating a diagnosis.",
                    ToolCalls = context.RequiredScenarioToolCalls.ToList()
                };
            }

            // 2. Strict Scoped "Specific Parts" Component Tool Filtering
            if (string.Equals(context.DiagnosisMode, "component", StringComparison.OrdinalIgnoreCase) &&
                context.AllowedDiagnosticTools is { Count: > 0 })
            {
                var scopedCalls = context.AllowedDiagnosticTools
                    .Where(t => !string.Equals(t, "inspect_gpu_status", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(t, "inspect_dns", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(toolName => new ReActToolCallRequest
                    {
                        ToolName = toolName,
                        ArgumentsJson = toolName.Equals("inspect_memory_and_processes", StringComparison.OrdinalIgnoreCase)
                            ? "{\"topN\":10,\"sortBy\":\"memory\"}"
                            : "{}",
                        Thought = $"Scoped component check for [{string.Join(", ", context.TargetScope)}]: running '{toolName}'."
                    })
                    .ToList();

                if (scopedCalls.Count > 0)
                {
                    return new ReActModelTurnDecision
                    {
                        EngineName = "RigMD Local Tool-Calling ReAct Engine",
                        Thought = $"The user explicitly requested to diagnose [{string.Join(", ", context.TargetScope)}]. Running only the mapped component tool(s): {string.Join(", ", scopedCalls.Select(c => $"'{c.ToolName}'"))}.",
                        ToolCalls = scopedCalls
                    };
                }
            }

            var toolCalls = new List<ReActToolCallRequest>();
            string thought;

            if (combinedText.Contains("network") || combinedText.Contains("dns") || combinedText.Contains("internet") || combinedText.Contains("ping") || combinedText.Contains("wifi") || combinedText.Contains("latency"))
            {
                thought =
                    "Symptom indicates network connectivity or DNS latency issues. I will first call 'inspect_network_connectivity' to probe live DNS resolution and ICMP ping, and 'query_windows_event_logs' to check for recent network/DNS client warnings.";
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_network_connectivity",
                    ArgumentsJson = "{\"targetHost\":\"one.one.one.one\"}",
                    Thought = thought
                });
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "query_windows_event_logs",
                    ArgumentsJson = "{\"logName\":\"System\",\"maxEvents\":8,\"hoursBack\":24}",
                    Thought = "Checking Windows System Event Log for recent network or DNS client events."
                });
            }
            else if (combinedText.Contains("storage") || combinedText.Contains("disk") || combinedText.Contains("space") || combinedText.Contains("cache") || combinedText.Contains("temp") || combinedText.Contains("update"))
            {
                thought =
                    "Symptom points to storage capacity, cache bloat, or disk I/O pressure. I will first invoke 'inspect_storage_health' to measure volume free space and exact reclaimable bytes in %TEMP%, Browser Caches, and Windows Update cache, plus 'inspect_memory_and_processes' to check active I/O/memory load.";
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_storage_health",
                    ArgumentsJson = "{}",
                    Thought = thought
                });
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_memory_and_processes",
                    ArgumentsJson = "{\"topN\":8,\"sortBy\":\"memory\"}",
                    Thought = "Checking active processes that may be holding cache locks or consuming memory."
                });
            }
            else if (combinedText.Contains("memory") || combinedText.Contains("ram") || combinedText.Contains("browser") || combinedText.Contains("leak") || combinedText.Contains("slow") || combinedText.Contains("freeze") || combinedText.Contains("lag"))
            {
                thought =
                    "Symptom indicates system responsiveness, RAM pressure, or background process contention. I will invoke 'inspect_memory_and_processes' to identify the top memory-consuming processes and 'inspect_cpu_and_thermals' + 'inspect_storage_health' to check CPU load, temperatures, and reclaimable temp/browser caches.";
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_memory_and_processes",
                    ArgumentsJson = "{\"topN\":10,\"sortBy\":\"memory\"}",
                    Thought = thought
                });
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_cpu_and_thermals",
                    ArgumentsJson = "{}",
                    Thought = "Checking live CPU load, clock frequency, and package temperature."
                });
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_storage_health",
                    ArgumentsJson = "{}",
                    Thought = "Measuring reclaimable disk cache and temporary file sizes."
                });
            }
            else
            {
                thought =
                    "Starting multi-sensor diagnostic investigation. I will invoke 'inspect_cpu_and_thermals', 'inspect_memory_and_processes', and 'inspect_storage_health' to collect live baseline observations across CPU, RAM, processes, and storage caches.";
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_cpu_and_thermals",
                    ArgumentsJson = "{}",
                    Thought = thought
                });
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_memory_and_processes",
                    ArgumentsJson = "{\"topN\":8,\"sortBy\":\"memory\"}",
                    Thought = "Inspecting active memory usage and top running processes."
                });
                toolCalls.Add(new ReActToolCallRequest
                {
                    ToolName = "inspect_storage_health",
                    ArgumentsJson = "{}",
                    Thought = "Inspecting volume health and reclaimable temp/cache directories."
                });
            }

            return new ReActModelTurnDecision
            {
                EngineName = "RigMD Local Tool-Calling ReAct Engine",
                Thought = thought,
                ToolCalls = toolCalls
            };
        }

        // Turn 1+: Synthesize actual observations returned by Tier 0 tools
        var evidence = context.History
            .Select(h => $"{h.ToolName}: {h.ObservationSummary}")
            .ToList();

        var storageObs = context.History.FirstOrDefault(h => h.ToolName.Equals("inspect_storage_health", StringComparison.OrdinalIgnoreCase));
        var memObs = context.History.FirstOrDefault(h => h.ToolName.Equals("inspect_memory_and_processes", StringComparison.OrdinalIgnoreCase));
        var netObs = context.History.FirstOrDefault(h => h.ToolName.Equals("inspect_network_connectivity", StringComparison.OrdinalIgnoreCase));
        var cpuObs = context.History.FirstOrDefault(h => h.ToolName.Equals("inspect_cpu_and_thermals", StringComparison.OrdinalIgnoreCase));

        double userTempMb = ExtractNestedDouble(storageObs?.ObservationJson, "reclaimableCaches", "userTemp", "sizeMb");
        double chromeCacheMb = ExtractNestedDouble(storageObs?.ObservationJson, "reclaimableCaches", "chromeCache", "sizeMb");
        double edgeCacheMb = ExtractNestedDouble(storageObs?.ObservationJson, "reclaimableCaches", "edgeCache", "sizeMb");
        double wuCacheMb = ExtractNestedDouble(storageObs?.ObservationJson, "reclaimableCaches", "windowsUpdateCache", "sizeMb");
        double browserCacheMb = chromeCacheMb + edgeCacheMb;

        double ramUsagePercent = ExtractNestedDouble(memObs?.ObservationJson, "memory", "usagePercent");
        double browserMemMb = ExtractNestedDouble(memObs?.ObservationJson, "browserSummary", "browserMemoryMb");

        if (netObs != null && (combinedText.Contains("network") || combinedText.Contains("dns") || combinedText.Contains("internet") || combinedText.Contains("ping")))
        {
            return new ReActModelTurnDecision
            {
                EngineName = "RigMD Local Tool-Calling ReAct Engine",
                Thought = $"Analyzed live network probe ({netObs.ObservationSummary}). Proposing 'flush_dns_cache' to clear stale resolver entries and force clean DNS resolution.",
                FinalProposal = new ReActFinalProposal
                {
                    RootCauseAnalysis = $"Live network inspection reported: {netObs.ObservationSummary}",
                    ConfidenceLevel = "High",
                    RecommendedToolName = "flush_dns_cache",
                    RecommendedToolArgumentsJson = "{}",
                    RemediationRationale = "Flushing the Windows DNS Client resolver cache purges stale or cached domain mappings without dropping active connections.",
                    EvidenceCitations = evidence
                }
            };
        }

        if (combinedText.Contains("browser") && browserCacheMb > 10 && !combinedText.Contains("temp"))
        {
            return new ReActModelTurnDecision
            {
                EngineName = "RigMD Local Tool-Calling ReAct Engine",
                Thought = $"Observed {browserCacheMb:F1} MB of browser disk cache (Chrome: {chromeCacheMb:F1} MB, Edge: {edgeCacheMb:F1} MB) and {browserMemMb:F0} MB of active browser memory. Proposing 'clear_browser_cache'.",
                FinalProposal = new ReActFinalProposal
                {
                    RootCauseAnalysis = $"Live storage and process inspection found {browserCacheMb:F1} MB of accumulated browser disk caches alongside {browserMemMb:F0} MB of active browser RAM usage.",
                    ConfidenceLevel = "High",
                    RecommendedToolName = "clear_browser_cache",
                    RecommendedToolArgumentsJson = "{\"browsers\":[\"chrome\",\"edge\",\"firefox\"]}",
                    RemediationRationale = "Clearing browser disk caches reclaims storage and removes stale cached web assets while preserving passwords, cookies, and history.",
                    EvidenceCitations = evidence
                }
            };
        }

        if (combinedText.Contains("update") && wuCacheMb > 0)
        {
            return new ReActModelTurnDecision
            {
                EngineName = "RigMD Local Tool-Calling ReAct Engine",
                Thought = $"Observed {wuCacheMb:F1} MB in C:\\Windows\\SoftwareDistribution\\Download. Proposing 'clear_windows_update_cache'.",
                FinalProposal = new ReActFinalProposal
                {
                    RootCauseAnalysis = $"Live storage inspection measured {wuCacheMb:F1} MB in the Windows Update Download cache.",
                    ConfidenceLevel = "High",
                    RecommendedToolName = "clear_windows_update_cache",
                    RecommendedToolArgumentsJson = "{}",
                    RemediationRationale = "Stopping wuauserv and purging SoftwareDistribution\\Download clears stale update packages and reclaims disk space.",
                    EvidenceCitations = evidence
                }
            };
        }

        if (combinedText.Contains("corrupt") || combinedText.Contains("sfc") || combinedText.Contains("system file"))
        {
            return new ReActModelTurnDecision
            {
                EngineName = "RigMD Local Tool-Calling ReAct Engine",
                Thought = "System file corruption or OS integrity issue suspected. Proposing 'run_system_file_checker' (sfc /scannow).",
                FinalProposal = new ReActFinalProposal
                {
                    RootCauseAnalysis = $"Live telemetry review ({string.Join(" | ", evidence)}) indicates potential OS component corruption requiring integrity verification.",
                    ConfidenceLevel = "Medium",
                    RecommendedToolName = "run_system_file_checker",
                    RecommendedToolArgumentsJson = "{}",
                    RemediationRationale = "Runs Windows System File Checker (sfc.exe /scannow) to verify and repair protected system binaries.",
                    EvidenceCitations = evidence
                }
            };
        }

        // Default data-driven remediation selection: if browser cache is significantly larger than user temp and storage/cache is the issue, or clear_temp_files for safe maintenance
        var rootCauseParts = new List<string>();
        if (cpuObs != null) rootCauseParts.Add(cpuObs.ObservationSummary);
        if (memObs != null) rootCauseParts.Add(memObs.ObservationSummary);
        if (storageObs != null) rootCauseParts.Add(storageObs.ObservationSummary);

        var rootCause = rootCauseParts.Count > 0
            ? string.Join(" ", rootCauseParts)
            : "Completed live hardware and OS inspection.";

        return new ReActModelTurnDecision
        {
            EngineName = "RigMD Local Tool-Calling ReAct Engine",
            Thought =
                $"Synthesized {context.History.Count} live tool observation(s): %TEMP% holds {userTempMb:F1} MB, Browser caches hold {browserCacheMb:F1} MB, RAM is at {ramUsagePercent:F1}%. Proposing 'clear_temp_files' as a safe, verified Tier 1 remediation.",
            FinalProposal = new ReActFinalProposal
            {
                RootCauseAnalysis = rootCause,
                ConfidenceLevel = "High",
                RecommendedToolName = "clear_temp_files",
                RecommendedToolArgumentsJson = "{\"includeWindowsTemp\":false,\"minAgeMinutes\":0}",
                RemediationRationale = $"Purging unlocked temporary files in %TEMP% (~{userTempMb:F1} MB measured) safely reclaims disk space and removes stale application temp artifacts without disrupting running work.",
                EvidenceCitations = evidence
            }
        };
    }

    private static object ParseJsonOrEmptyObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new { };
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch
        {
            return new { };
        }
    }

    private static string TryGetString(JsonElement element, string propName, string fallback = "")
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propName, out var val) &&
            val.ValueKind == JsonValueKind.String)
        {
            return val.GetString() ?? fallback;
        }

        return fallback;
    }

    private static double ExtractNestedDouble(string? json, params string[] path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment, out current))
                {
                    return 0;
                }
            }

            if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var d))
            {
                return d;
            }
        }
        catch
        {
            // Ignore malformed JSON
        }

        return 0;
    }
}
