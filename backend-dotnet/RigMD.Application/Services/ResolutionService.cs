using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

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
    private const double BatteryDrainCpuThreshold = 90;
    private const double SevereResourceThreshold = 85;
    private const double ElevatedMemoryThreshold = MemoryPressureRules.HighUsagePercent;
    private const double ElevatedStorageThreshold = 80;
    private const double ElevatedTempThresholdCelsius = 85;

    public ResolutionResultDto CheckResolution(
        string diagnosedCategory,
        HardwareProfileDto hardware,
        string? scenarioId = null,
        string? componentIds = null,
        DateTimeOffset? sessionCreatedAt = null,
        string? diagnosisMode = null,
        object[]? originalProof = null)
    {
        var components = ParseComponentIds(componentIds);
        var normalizedScenario = DiagnosticScopeMapper.NormalizeScenarioId(scenarioId);
        var checkComponentCondition = !string.IsNullOrWhiteSpace(normalizedScenario) || components.Count > 0 ||
            string.Equals(diagnosisMode, "full", StringComparison.OrdinalIgnoreCase);
        var result = checkComponentCondition
            ? CheckComponentCondition(hardware, components, string.Equals(diagnosisMode, "full", StringComparison.OrdinalIgnoreCase), normalizedScenario, sessionCreatedAt, diagnosedCategory, originalProof)
            : EvaluateResolution(diagnosedCategory, hardware, scenarioId, componentIds, sessionCreatedAt);
        var missingCpu = hardware.Cpu.UsageAvailable == false || !double.IsFinite(hardware.Cpu.UsagePercent) || hardware.Cpu.UsagePercent is < 0 or > 100;
        var missingMemory = hardware.Ram.UsageAvailable == false || !MemoryPressureRules.IsValidUsage(hardware.Ram.UsagePercent);
        var missingStorage = !hardware.AllDisks.Any() && !hardware.StorageDrives.Any(d => d.UsagePercent.HasValue);
        var missing = false;
        var active = false;
        var proof = new List<object>();
        foreach (var item in result.resolution_proof)
        {
            var reading = JsonSerializer.SerializeToElement(item);
            var label = reading.TryGetProperty("label", out var name) ? name.GetString() ?? "" : "";
            if ((missingCpu && label is "Processor activity" or "Processor load") ||
                (missingMemory && label is "Memory use" or "Memory pressure") ||
                (missingStorage && label is "Main storage use" or "Storage use"))
            {
                missing = true;
                proof.Add(new { label, value = "Unavailable", status = "unknown",
                    meaning = "The measurement could not be obtained. It is not zero or a normal reading." });
            }
            else
            {
                var status = reading.TryGetProperty("status", out var state) ? state.GetString() : "";
                active |= status is "high" or "elevated" or "still needs attention";
                proof.Add(item);
            }
        }
        if (missing)
        {
            result.resolution_proof = proof.ToArray();
            var thermalOnly = (diagnosedCategory ?? "").Contains("thermal", StringComparison.OrdinalIgnoreCase) ||
                DiagnosticScopeMapper.NormalizeScenarioId(scenarioId) == "overheating-loud-fan";
            if (!thermalOnly && !active)
            {
                result.resolution_status = "needs_recheck";
                result.resolution_summary = "Required resource readings were unavailable. A normal or fixed result could not be confirmed.";
            }
        }
        result.resolution_proof = result.resolution_proof.Select(item =>
        {
            var reading = JsonSerializer.SerializeToElement(item);
            string Text(string key) => reading.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? "" : "";
            var label = Text("label");
            var metric = Text("metric_id");
            if (string.IsNullOrEmpty(metric)) metric = label switch
            {
                "Processor activity" or "Processor load" => "cpu_usage",
                "Memory use" or "Memory pressure" => "memory_usage",
                "CPU temperature" or "Processor temperature" => "cpu_temperature",
                "GPU temperature" or "Graphics temperature" => "gpu_temperature",
                "Main storage use" or "Storage use" => "storage_usage",
                "Storage health warning" => "storage_health",
                _ => ""
            };
            var capturedAt = Text("captured_at");
            return (object)new { metric_id = metric, captured_at = string.IsNullOrEmpty(capturedAt) ? result.resolution_checked_at : capturedAt,
                evidence_kind = string.IsNullOrEmpty(Text("evidence_kind")) ? ReadingEvidence.Kind(metric, Text("status")) : Text("evidence_kind"),
                rule_version = string.IsNullOrEmpty(Text("rule_version")) ? ReadingEvidence.RuleVersion : Text("rule_version"),
                window_kind = Text("window_kind"), window_start = Text("window_start"), window_end = Text("window_end"),
                label, value = Text("value"), status = Text("status"), meaning = Text("meaning") };
        }).ToArray();
        var comparison = CompareOriginalWarnings(originalProof ?? [], result.resolution_proof);
        if (!string.IsNullOrEmpty(comparison)) result.resolution_summary = comparison + " " + result.resolution_summary;
        return result;
    }

    private static string CompareOriginalWarnings(object[] original, object[] latest)
    {
        static string Field(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object &&
            row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        static bool Warning(string status) => status is "high" or "elevated" or "still needs attention";
        var before = original.Select(item => JsonSerializer.SerializeToElement(item)).ToArray();
        var after = latest.Select(item => JsonSerializer.SerializeToElement(item)).ToArray();
        var targets = before.Where(row => Field(row, "metric_id").Length > 0 && Warning(Field(row, "status"))).ToArray();
        if (targets.Length == 0) return "";
        var messages = new List<string>();
        foreach (var target in targets)
        {
            var matches = after.Where(row => Field(row, "metric_id") == Field(target, "metric_id")).ToArray();
            if (Field(target, "metric_id") is "application_crash_history" or "system_crash_history")
            {
                var followUp = matches.FirstOrDefault();
                var since = Field(followUp, "window_kind") == "since_diagnosis" || Field(followUp, "label").Contains("since diagnosis", StringComparison.OrdinalIgnoreCase);
                messages.Add(since && Field(followUp, "status") is "normal" or "normal now" && Field(followUp, "value") == "0 found"
                    ? "No new matching crash records were found during the follow-up period. Earlier saved records remain in this report; this does not prove a repair."
                    : since && matches.Any(row => Warning(Field(row, "status")))
                        ? "Matching crash records were found during the follow-up period. Review the affected app or stop code; a cause is not established."
                        : "The crash-record checks cover different or unverified periods. Their counts do not establish improvement.");
                continue;
            }
            var state = matches.Any(row => Warning(Field(row, "status"))) ? "still detected"
                : matches.Length > 0 && matches.All(row => Field(row, "status") is "normal" or "normal now")
                    ? "no longer detected in this reading" : "could not be verified";
            messages.Add($"Original warning - {Field(target, "label")}: {state}.");
        }
        var added = after.Where(row => Warning(Field(row, "status")) &&
            before.Any(old => Field(old, "metric_id") == Field(row, "metric_id")) &&
            !targets.Any(old => Field(old, "metric_id") == Field(row, "metric_id"))).ToArray();
        if (added.Length > 0) messages.Add($"Other warnings now detected: {string.Join(", ", added.Select(row => Field(row, "label")))}.");
        return string.Join(" ", messages);
    }

    private static ResolutionResultDto CheckComponentCondition(HardwareProfileDto hardware, HashSet<string> components, bool full,
        string? scenario = null, DateTimeOffset? sessionCreatedAt = null, string? originalCategory = null, object[]? originalProof = null)
    {
        // Reuse the initial check's scope and rules instead of rechecking only its highest-priority finding.
        var checkedAt = DateTimeOffset.UtcNow;
        var condition = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = !string.IsNullOrWhiteSpace(scenario) ? "scenario" : full ? "full" : "component", ComponentIds = components.ToArray(), ScenarioId = scenario,
            Hardware = hardware, CapturedAt = checkedAt,
            EventsSince = scenario is "app-crashes" or "blue-screen-crash" ? sessionCreatedAt : null
        });
        if (condition.ConfidenceLabel == "NotApplicable")
            return new ResolutionResultDto
            {
                resolution_status = "not_applicable", resolution_checked_at = checkedAt.ToString("o"),
                resolution_summary = condition.PrimaryResult + ". No repair or repeated sensor check is indicated.",
                resolution_proof = condition.Proof.Select(p => (object)new
                {
                    label = p.Label, value = p.Value, status = "info", meaning = p.Meaning,
                    evidence_kind = "reported_status", rule_version = ReadingEvidence.RuleVersion
                }).ToArray()
            };
        var warnings = condition.Proof.Where(p => p.Status is "high" or "elevated" or "still needs attention").ToArray();
        var unavailable = condition.Proof.Where(p => p.Status == "unknown").ToArray();
        var crashCheck = scenario is "app-crashes" or "blue-screen-crash";
        var originalWarnings = (originalProof ?? []).Select(item => JsonSerializer.SerializeToElement(item)).Where(row =>
            row.TryGetProperty("status", out var status) && status.GetString() is "high" or "elevated" or "still needs attention")
            .Select(row => row.TryGetProperty("metric_id", out var metric) ? metric.GetString() : null).ToHashSet();
        var requiredUnavailable = crashCheck ? unavailable.Where(p =>
            p.MetricId is "application_crash_history" or "system_crash_history" or "event_query_coverage" ||
            originalWarnings.Contains(p.MetricId) ||
            p.MetricId == "memory_usage" && (originalCategory ?? "").Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            p.MetricId == "cpu_usage" && (originalCategory ?? "").Contains("processor", StringComparison.OrdinalIgnoreCase)).ToArray() : unavailable;
        var incomplete = requiredUnavailable.Length > 0 || condition.Proof.Count == 0 ||
            condition.ComponentStatus == ComponentStatus.NotPresent ||
            !crashCheck && condition.DiagnosedCategory.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
        var active = warnings.Length > 0 || (!incomplete && condition.ActionCategory is "Troubleshoot" or "Escalate" or "Repair");
        var summary = active
            ? warnings.Length > 0
                ? $"Readings needing attention: {string.Join(", ", warnings.Select(p => p.Label))}."
                : condition.PrimaryResult
            : crashCheck && !incomplete
                ? sessionCreatedAt.HasValue
                    ? "No new matching crash records were found since the original check. This observation does not confirm that a recurring crash is fixed."
                    : "No matching crash records were found in the queried period. This observation does not confirm that a recurring crash is fixed."
            : incomplete
                ? "The available readings do not show a warning, but the selected condition could not be fully checked."
                : "The checked readings are within their configured limits at this time.";
        if (unavailable.Length > 0)
            summary += $" Could not verify: {string.Join(", ", unavailable.Select(p => p.Label))}.";
        summary += " This is a current-readings check, not a hardware-health test or confirmation of a repair. Workload can change without user action.";
        return new ResolutionResultDto
        {
            resolution_status = active ? "still_active" : incomplete ? "needs_recheck" : "resolved",
            resolution_checked_at = checkedAt.ToString("o"), resolution_summary = summary,
            resolution_proof = condition.Proof.Select(p => (object)new
            {
                metric_id = p.MetricId, captured_at = (p.CapturedAt ?? checkedAt).ToString("o"),
                evidence_kind = p.EvidenceKind, rule_version = p.RuleVersion,
                window_kind = p.WindowKind, window_start = p.WindowStart?.ToString("o"), window_end = p.WindowEnd?.ToString("o"),
                label = p.Label, value = p.Value, status = p.Status, meaning = p.Meaning
            }).ToArray()
        };
    }

    private ResolutionResultDto EvaluateResolution(
        string diagnosedCategory,
        HardwareProfileDto hardware,
        string? scenarioId = null,
        string? componentIds = null,
        DateTimeOffset? sessionCreatedAt = null)
    {
        var normalizedScenario = DiagnosticScopeMapper.NormalizeScenarioId(scenarioId);
        if (!string.IsNullOrWhiteSpace(normalizedScenario))
        {
            switch (normalizedScenario)
            {
                case "overheating-loud-fan":
                    return CheckThermalResolution(hardware);
                case "driver-error":
                    return CheckDriverAndDisplayResolution(hardware);
                case "no-display":
                    return CheckDriverAndDisplayResolution(hardware, graphicsOnly: true, includeMonitors: true);
                case "network-problem":
                case "network-issue":
                    return CheckNetworkResolution(hardware);
                case "rapid-battery-drain":
                    return CheckBatteryResolution(hardware);
                case "storage-problem":
                case "disk-full":
                    return CheckStorageUsageResolution(
                        hardware,
                        storageMustBeBelow: ElevatedStorageThreshold,
                        issueName: "high storage use");
                case "app-crashes":
                    return CheckCrashStabilityResolution(hardware, sessionCreatedAt, CrashResolutionKind.Application);
                case "blue-screen-crash":
                    return CheckCrashStabilityResolution(hardware, sessionCreatedAt, CrashResolutionKind.System);
                case "slow-boot":
                    return CheckBootStartupResolution(hardware);
                case "slow-system":
                case "stuttering-freezing":
                    return CheckOsPerformanceResolution(hardware, CpuLoadThresholds.Responsiveness,
                        MemoryPressureRules.ResponsivenessUsagePercent);
            }
        }

        var parsedComponents = ParseComponentIds(componentIds);
        if (parsedComponents.Count > 0)
        {
            if (parsedComponents.Contains("network") && parsedComponents.Count == 1)
            {
                return CheckNetworkResolution(hardware);
            }

            if (parsedComponents.Contains("battery") && parsedComponents.Count == 1)
            {
                return CheckBatteryResolution(hardware);
            }

            if (parsedComponents.Contains("storage") && parsedComponents.Count == 1)
            {
                return hardware.StorageDrives.Any(d => d.IsFailingSmart)
                    ? CheckStorageHealthResolution(hardware)
                    : CheckStorageUsageResolution(
                        hardware,
                        storageMustBeBelow: ElevatedStorageThreshold,
                        issueName: "high storage use");
            }

            if (parsedComponents.All(c => c is "drivers" or "gpu" or "display"))
            {
                return CheckDriverAndDisplayResolution(hardware,
                    graphicsOnly: !parsedComponents.Contains("drivers"),
                    includeMonitors: parsedComponents.Contains("display"));
            }

            if (parsedComponents.Contains("cpu") && parsedComponents.Count == 1)
            {
                return (diagnosedCategory ?? string.Empty).Contains("thermal", StringComparison.OrdinalIgnoreCase)
                    ? CheckThermalResolution(hardware)
                    : CheckCpuResolution(hardware);
            }

            if (parsedComponents.Contains("memory") && parsedComponents.Count == 1)
            {
                return CheckMemoryPressureResolution(hardware, explicitMemoryScan: true);
            }
        }

        var category =
            (diagnosedCategory ?? string.Empty).ToLowerInvariant();

        if (category.Contains("severe system resource exhaustion"))
        {
            return CheckSevereResourceResolution(hardware);
        }

        if (category.Contains("thermal"))
        {
            return CheckThermalResolution(hardware);
        }

        if (category.Contains("elevated cpu") || category.Contains("cpu & thermal") || category.Contains("cpu load"))
        {
            return CheckCpuResolution(hardware);
        }

        if (category.Contains("application crash"))
        {
            return CheckCrashStabilityResolution(hardware, sessionCreatedAt, CrashResolutionKind.Application);
        }

        if (category.Contains("system crash") || category.Contains("stop error"))
        {
            return CheckCrashStabilityResolution(hardware, sessionCreatedAt, CrashResolutionKind.System);
        }

        if (category.Contains("boot and startup") || category.Contains("boot or startup"))
        {
            return CheckBootStartupResolution(hardware);
        }

        if (category.Contains("os performance"))
        {
            return CheckOsPerformanceResolution(hardware);
        }

        if (category.Contains("high memory pressure") || category.Contains("critical memory pressure") || category.Contains("memory resource pressure") || category.Contains("memory pressure / high ram"))
        {
            return CheckMemoryPressureResolution(hardware);
        }

        if (category.Contains("elevated memory pressure"))
        {
            return CheckMemoryPressureResolution(hardware, explicitMemoryScan: true);
        }

        if (category.Contains("elevated memory usage") || category.Contains("memory"))
        {
            return CheckMemoryPressureResolution(hardware);
        }

        if (category.Contains("storage health"))
        {
            return CheckStorageHealthResolution(hardware);
        }

        if (category.Contains("low available storage") || category.Contains("storage capacity"))
        {
            return CheckStorageUsageResolution(
                hardware,
                storageMustBeBelow: ElevatedStorageThreshold,
                issueName: "low storage space");
        }

        if (category.Contains("elevated storage") || category.Contains("storage"))
        {
            return CheckStorageUsageResolution(
                hardware,
                storageMustBeBelow: ElevatedStorageThreshold,
                issueName: "high storage use");
        }

        if (category.Contains("network"))
        {
            return CheckNetworkResolution(hardware);
        }

        if (category.Contains("battery") || category.Contains("power"))
        {
            return CheckBatteryResolution(hardware);
        }

        if (category.Contains("driver") || category.Contains("display"))
        {
            var graphicsOnly = parsedComponents.Overlaps(new[] { "gpu", "display" }) &&
                !parsedComponents.Overlaps(new[] { "drivers", "peripherals", "audio" });
            return CheckDriverAndDisplayResolution(hardware, graphicsOnly,
                includeMonitors: parsedComponents.Contains("display"));
        }

        return CheckSevereResourceResolution(hardware);
    }

    private static HashSet<string> ParseComponentIds(string? rawComponents)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawComponents))
        {
            return set;
        }

        var trimmed = rawComponents.Trim();
        if (trimmed.StartsWith("["))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null)
                {
                    foreach (var item in parsed)
                    {
                        var norm = DiagnosticScopeMapper.NormalizeComponentId(item);
                        if (!string.IsNullOrWhiteSpace(norm))
                        {
                            set.Add(norm);
                        }
                    }
                }
                return set;
            }
            catch
            {
                // Fall back to comma split
            }
        }

        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var norm = DiagnosticScopeMapper.NormalizeComponentId(part);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                set.Add(norm);
            }
        }

        return set;
    }

    private static ResolutionResultDto CheckDriverAndDisplayResolution(
        HardwareProfileDto hardware,
        bool graphicsOnly = false,
        bool includeMonitors = false)
    {
        var errCount = DeviceErrorAttribution.Select(hardware.DeviceErrors, graphicsOnly, includeMonitors).Count;
        var gpuTemp = hardware.Gpu.TemperatureCelsius;
        var gpuTempOk = !gpuTemp.HasValue || gpuTemp.Value < ElevatedTempThresholdCelsius;
        var driversOk = errCount == 0;
        var displaysOk = !includeMonitors || hardware.ConnectedDisplays > 0;
        var resolved = driversOk && gpuTempOk && displaysOk;
        var errorScope = graphicsOnly ? "selected graphics hardware" : "all hardware";
        var graphicsErrors = DeviceErrorAttribution.Select(hardware.DeviceErrors, graphicsOnly: true, includeMonitors: includeMonitors).Count;
        var graphicsOk = graphicsErrors == 0 && gpuTempOk && displaysOk;

        var gpuSummaryValue = gpuTemp.HasValue
            ? $"{hardware.Gpu.Name} ({gpuTemp.Value:0.#}°C, Driver {hardware.Gpu.Driver})"
            : $"{hardware.Gpu.Name} (Driver {hardware.Gpu.Driver})";

        var unresolvedSummary = !displaysOk
            ? "Fresh scan does not detect a connected display; the display issue cannot be marked resolved."
            : !driversOk && !gpuTempOk
                ? $"Fresh scan still shows {errCount} device error code(s) for {errorScope} and elevated graphics adapter temperature."
                : !driversOk
                    ? $"Fresh scan still shows {errCount} active Device Manager error(s) for {errorScope}."
                    : "Fresh scan still shows elevated graphics adapter temperature. Device Manager is not reporting active driver errors in the checked scope.";

        return CreateResult(
            resolved,
            resolved
                ? $"Fresh scan shows zero PnP device error codes for {errorScope} and no elevated graphics temperature reading."
                : unresolvedSummary,
            new object[]
            {
                Proof(
                    graphicsOnly ? "Graphics Device Errors" : "Device Manager Errors",
                    errCount == 0 ? "0 active errors" : $"{errCount} active error(s)",
                    driversOk,
                    $"No active device error codes were reported for {errorScope}.",
                    $"One or more devices in {errorScope} still report a non-zero ConfigManagerErrorCode."),
                Proof(
                    "Graphics adapter",
                    gpuSummaryValue,
                    graphicsOk,
                    $"No active graphics-class error or elevated temperature was reported; {hardware.ConnectedDisplays} connected display(s) detected.",
                    "A graphics-class device error, elevated temperature, or missing display still requires review.")
            });
    }

    private static ResolutionResultDto CheckBatteryResolution(
        HardwareProfileDto hardware)
    {
        var battery = hardware.Battery;
        if (battery == null || !battery.HasBattery)
        {
            return CreateResult(
                true,
                "This desktop device runs on direct AC power and has no battery warning.",
                new object[]
                {
                    Proof(
                        "Power / Battery",
                        $"AC Power ({hardware.ActivePowerPlan})",
                        true,
                        "Device is operating normally on AC power.",
                        "Battery or power status needs review.")
                });
        }

        var chargePercent = battery.ChargePercent > 0
            ? battery.ChargePercent
            : battery.EstimatedChargeRemaining;
        var cpuOk = hardware.Cpu.UsagePercent < BatteryDrainCpuThreshold;
        var chargeOk = chargePercent > 15 || battery.IsCharging;
        var resolved = cpuOk && chargeOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows battery charge and background processor load within normal limits."
                : "Fresh scan still shows low battery charge or high background processor drain.",
            new object[]
            {
                Proof(
                    "Battery status",
                    $"{chargePercent}% ({battery.StatusDescription})",
                    chargeOk,
                    "Battery charge level and power state are healthy.",
                    "Battery charge is low while discharging."),
                Proof(
                    "Background processor load",
                    $"{hardware.Cpu.UsagePercent:0.##}% ({hardware.ActivePowerPlan})",
                    cpuOk,
                    "Processor activity is low enough to prevent rapid battery drain.",
                    "High background processor load is still contributing to battery drain.")
            });
    }

    private enum CrashResolutionKind
    {
        Application,
        System
    }

    private static ResolutionResultDto CheckCrashStabilityResolution(
        HardwareProfileDto hardware,
        DateTimeOffset? sessionCreatedAt,
        CrashResolutionKind kind)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsageAvailable == false ? double.NaN : hardware.Ram.UsagePercent;
        var errCount = hardware.DeviceErrors?.Count ?? 0;

        var sinceUtc = (sessionCreatedAt ?? DateTimeOffset.UtcNow.AddHours(-24)).ToUniversalTime();
        var stabilityEvents = hardware.StabilityEvents ?? new StabilityEventSnapshotDto();
        var relevantEvents = kind == CrashResolutionKind.Application
            ? stabilityEvents.ApplicationCrashEvents
            : stabilityEvents.SystemCrashEvents;
        var eventsSinceDiagnosis = relevantEvents
            .Where(ev => !ev.TimeUtc.HasValue || ev.TimeUtc.Value.ToUniversalTime() >= sinceUtc)
            .OrderByDescending(ev => ev.TimeUtc ?? DateTimeOffset.MinValue)
            .ToList();

        var eventLogAvailable = string.IsNullOrWhiteSpace(stabilityEvents.QueryWarning);
        var eventEvidenceOk = eventLogAvailable && eventsSinceDiagnosis.Count == 0;
        var cpuThreshold = kind == CrashResolutionKind.Application
            ? CpuLoadThresholds.Elevated : CpuLoadThresholds.SystemStabilityRisk;
        var cpuOk = cpuUsage < cpuThreshold;
        var validRamUsage = MemoryPressureRules.IsValidUsage(ramUsage);
        var ramOk = kind == CrashResolutionKind.System || (validRamUsage && ramUsage < ElevatedMemoryThreshold);
        var driversOk = errCount == 0;
        var supportingSignalsOk = cpuOk && ramOk && driversOk;
        var resolved = eventEvidenceOk && supportingSignalsOk;

        var issueLabel = kind == CrashResolutionKind.Application
            ? "application crash"
            : "system crash";
        var eventProofLabel = kind == CrashResolutionKind.Application
            ? "Application crash events since diagnosis"
            : "System crash events since diagnosis";

        var proofItems = new List<object>
        {
            Proof(
                eventProofLabel,
                BuildEventProofValue(eventsSinceDiagnosis, eventLogAvailable, stabilityEvents.QueryWarning),
                eventEvidenceOk,
                $"No new {issueLabel} events were found in Windows Event Logs after this diagnosis.",
                eventLogAvailable
                    ? $"Windows Event Logs still show {eventsSinceDiagnosis.Count} {issueLabel} event(s) after this diagnosis."
                    : "RigMD could not confirm Windows Event Log evidence, so the saved crash check should not be marked fixed yet.")
        };

        if (kind == CrashResolutionKind.Application)
            proofItems.Add(validRamUsage ? Proof(
                "Memory pressure", $"{ramUsage:0.##}%", ramOk,
                "Memory use is below this application-stability check's pressure threshold.",
                "Memory use is still high enough to contribute to app instability or freezes.")
                : new { label = "Memory pressure", value = "Unavailable", status = "unknown",
                    meaning = "An invalid RAM reading cannot verify application-stability memory pressure." });

        if (kind == CrashResolutionKind.System || errCount > 0)
        {
            proofItems.Add(
                Proof(
                    "Device driver warnings",
                    errCount == 0 ? "0 active errors" : $"{errCount} active error(s)",
                    driversOk,
                    "Device Manager is not reporting active driver error codes.",
                    "One or more devices still report an active driver error code."));
        }

        if (kind == CrashResolutionKind.System || !cpuOk)
        {
            proofItems.Add(
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    cpuOk,
                    "Processor activity is within normal operating limits.",
                    "Processor activity is still elevated and may contribute to instability."));
        }

        var result = CreateResult(
            resolved,
            resolved
                ? $"Fresh scan found no new {issueLabel} events after this diagnosis, and supporting telemetry is stable."
                : BuildCrashResolutionSummary(
                    issueLabel,
                    eventLogAvailable,
                    eventsSinceDiagnosis.Count,
                    supportingSignalsOk),
            proofItems.ToArray());
        if (kind == CrashResolutionKind.Application && !validRamUsage && eventEvidenceOk && cpuOk && driversOk)
        {
            result.resolution_status = "needs_recheck";
            result.resolution_summary = "Fresh scan could not verify RAM pressure because the memory reading is unavailable.";
        }
        return result;
    }

    private static ResolutionResultDto CheckBootStartupResolution(
        HardwareProfileDto hardware)
    {
        var primaryDisk = GetPrimaryDisk(hardware);
        var storageUsage = primaryDisk?.UsagePercent ?? 0;
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;

        var storageOk = primaryDisk == null || storageUsage < ElevatedStorageThreshold;
        var cpuOk = cpuUsage < CpuLoadThresholds.Startup;
        var ramOk = ramUsage < ElevatedMemoryThreshold;
        var resolved = storageOk && cpuOk && ramOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows boot storage, processor, and memory operating normally. No active issue is detected for this saved check."
                : "Fresh scan still shows elevated boot storage or startup resource usage.",
            new object[]
            {
                Proof(
                    "Main storage use",
                    primaryDisk == null ? "Not available" : $"{storageUsage:0.##}%",
                    storageOk,
                    "Boot drive free space is within a healthy range for Windows startup.",
                    "Boot drive space is still tight and may slow down startup caching."),
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    cpuOk,
                    "Processor startup load is within normal limits.",
                    "Processor load is still elevated."),
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    ramOk,
                    "Memory use is within normal limits.",
                    "Memory use is still above RigMD's high-use range.")
            });
    }

    private static ResolutionResultDto CheckSevereResourceResolution(
        HardwareProfileDto hardware)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;
        var primaryDisk = GetPrimaryDisk(hardware);
        var storageUsage = primaryDisk?.UsagePercent ?? 0;

        var cpuOk = cpuUsage < CpuLoadThresholds.Elevated;
        var ramOk = ramUsage < MemoryPressureRules.HighUsagePercent;
        var storageOk = storageUsage < ElevatedStorageThreshold;
        var resolved = cpuOk && ramOk && storageOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan no longer shows the combined CPU, memory, and storage pressure. No active issue is detected for this saved check."
                : "Fresh scan still shows heavy pressure on at least one major part of the device.",
            new object[]
            {
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    cpuOk,
                    "Processor activity is back under RigMD's severe-pressure range.",
                    "Processor activity is still inside RigMD's severe-pressure range."),
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    ramOk,
                    "Memory use is back under RigMD's severe-pressure range.",
                    "Memory use is still inside RigMD's severe-pressure range."),
                Proof(
                    "Storage use",
                    primaryDisk == null ? "Not available" : $"{storageUsage:0.##}%",
                    storageOk,
                    "Storage use is back under RigMD's severe-pressure range.",
                    "Storage use is still inside RigMD's severe-pressure range.")
            });
    }

    private static ResolutionResultDto CheckThermalResolution(
        HardwareProfileDto hardware)
    {
        var tempC = hardware.Cpu.TemperatureCelsius;
        var tempOk = !tempC.HasValue || tempC.Value < ElevatedTempThresholdCelsius;
        var throttleOk = !hardware.Cpu.IsThermallyThrottling;
        var cpuLoadOk = hardware.Cpu.UsagePercent < CpuLoadThresholds.Elevated;
        var gpuTemp = hardware.Gpu.TemperatureCelsius;
        var gpuTempOk = !gpuTemp.HasValue || gpuTemp.Value < ElevatedTempThresholdCelsius;
        var resolved = tempOk && throttleOk && gpuTempOk;

        if (resolved && !tempC.HasValue)
        {
            return new ResolutionResultDto
            {
                resolution_status = "needs_recheck",
                resolution_summary = "CPU temperature is unavailable. Low processor activity and no reported throttling do not verify that the thermal issue is resolved.",
                resolution_proof = new object[]
                {
                    new { label = "Processor temperature", value = "Unavailable", status = "unknown",
                        meaning = "No identified CPU temperature sensor reading is available; no temperature was estimated." }
                }
            };
        }

        var tempDisplay = tempC.HasValue
            ? $"{tempC.Value:0.#}°C ({hardware.Cpu.UsagePercent:0.#}% load)"
            : $"Unavailable ({hardware.Cpu.UsagePercent:0.#}% load)";

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows measured processor temperature and any available graphics temperature below RigMD's thermal threshold, with no processor thermal throttling reported. No active issue is detected for this saved check."
                : "Fresh scan still shows elevated CPU or GPU temperature, or reported processor thermal throttling.",
            new List<object>
            {
                !tempC.HasValue ? (object)new
                {
                    label = "Processor temperature", value = tempDisplay, status = "unknown",
                    meaning = "Processor load does not measure CPU temperature."
                } : Proof(
                    "Processor temperature",
                    tempDisplay,
                    tempOk,
                    "Processor temperature is below RigMD's 85 C thermal threshold.",
                    "Processor temperature is still elevated (>= 85°C)."),
                Proof(
                    "Processor throttling",
                    hardware.Cpu.IsThermallyThrottling ? "Reported" : "Not reported",
                    throttleOk,
                    "No processor thermal throttling was reported; this is not a temperature measurement.",
                    "Processor thermal throttling was reported."),
                Proof(
                    "Processor activity",
                    $"{hardware.Cpu.UsagePercent:0.##}%",
                    cpuLoadOk,
                    "Processor activity is below the high-use range; it is not a temperature measurement.",
                    "Processor activity is high; temperature is assessed separately from workload.")
            }
            .Concat(gpuTemp.HasValue
                ? new object[]
                {
                    Proof(
                        "Graphics temperature",
                        $"{gpuTemp.Value:0.#}°C",
                        gpuTempOk,
                        "Graphics temperature is below RigMD's 85 C thermal threshold.",
                        "Graphics temperature is still elevated and may affect fan noise.")
                }
                : Array.Empty<object>())
            .ToArray());
    }

    private static ResolutionResultDto CheckCpuResolution(
        HardwareProfileDto hardware)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        if (hardware.Cpu.UsageAvailable == false || !double.IsFinite(cpuUsage) || cpuUsage is < 0 or > 100)
            return new ResolutionResultDto
            {
                resolution_status = "needs_recheck", resolution_summary = "Processor activity could not be measured.",
                resolution_proof = new object[] { new { label = "Processor activity", value = "Unavailable", status = "unknown",
                    meaning = "A failed activity measurement is not zero activity." } }
            };
        var resolved = cpuUsage < CpuLoadThresholds.Elevated;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows processor activity below RigMD's elevated-load threshold. No active issue is detected for this saved check."
                : "Fresh scan still shows elevated processor activity.",
            new object[]
            {
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    resolved,
                    "Processor activity is back below RigMD's high-use range.",
                    "Processor activity is still above RigMD's high-use range.")
            });
    }

    private static ResolutionResultDto CheckOsPerformanceResolution(
        HardwareProfileDto hardware,
        double cpuMustBeBelow = CpuLoadThresholds.Elevated,
        double memoryMustBeBelow = MemoryPressureRules.HighUsagePercent)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;
        var primaryDisk = GetPrimaryDisk(hardware);
        var storageUsage = primaryDisk?.UsagePercent ?? 0;

        var memoryWarning = hardware.ProcessInsights?.MemoryLeakWarning;

        var cpuOk = cpuUsage < cpuMustBeBelow;
        var ramOk = MemoryPressureRules.IsValidUsage(ramUsage) && ramUsage < memoryMustBeBelow;
        var storageOk = primaryDisk == null || storageUsage < ElevatedStorageThreshold;
        var warningOk = string.IsNullOrWhiteSpace(memoryWarning);

        var resolved = cpuOk && ramOk && storageOk && warningOk;

        var proofItems = new List<object>
        {
            Proof(
                "Processor activity",
                $"{cpuUsage:0.##}%",
                cpuOk,
                "Processor activity is within normal operating limits.",
                "Processor activity is still elevated and may slow down system responsiveness."),
            MemoryPressureRules.IsValidUsage(ramUsage) ? Proof(
                "Memory use", $"{ramUsage:0.##}%", ramOk,
                "Memory use is below this performance check's pressure threshold.",
                "Memory use is still above this performance check's pressure threshold.")
                : new { label = "Memory use", value = "Unavailable", status = "unknown",
                    meaning = "An invalid RAM reading cannot verify memory pressure." },
            Proof(
                "Main storage use",
                primaryDisk == null ? "Not available" : $"{storageUsage:0.##}%",
                storageOk,
                "Main storage free space is within a healthy range.",
                "Main storage space is still tight and may affect Windows responsiveness.")
        };

        if (!warningOk)
        {
            proofItems.Add(
                Proof(
                    "Unusual app memory behavior",
                    memoryWarning!,
                    false,
                    "RigMD no longer sees one app using unusually high memory.",
                    "RigMD still sees app memory behavior that needs attention."));
        }

        var result = CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows processor, memory, and storage operating within normal limits. No active issue is detected for this saved check."
                : "Fresh scan still shows system resource pressure or app memory behavior that may affect performance.",
            proofItems.ToArray());
        if (!MemoryPressureRules.IsValidUsage(ramUsage) && cpuOk && storageOk && warningOk)
        {
            result.resolution_status = "needs_recheck";
            result.resolution_summary = "Fresh scan could not verify RAM pressure because the memory reading is unavailable.";
        }
        return result;
    }

    private static ResolutionResultDto CheckMemoryPressureResolution(
        HardwareProfileDto hardware, bool explicitMemoryScan = false)
    {
        var ramUsage = hardware.Ram.UsagePercent;
        var processes = hardware.ProcessInsights;
        var state = MemoryPressureRules.Evaluate(ramUsage, processes?.BrowserMemoryMb,
            processes?.MemoryLeakWarning, explicitMemoryScan, processes != null);
        var validUsage = MemoryPressureRules.IsValidUsage(ramUsage);
        var proof = new List<object>
        {
            new
            {
                label = "Memory use", value = validUsage ? $"{ramUsage:0.##}%" : "Unavailable",
                status = !validUsage ? "unknown" : ramUsage >= MemoryPressureRules.HighUsagePercent ? "high" : "normal",
                meaning = "RigMD flags RAM use at 82% for workload review in every check. This does not establish paging, a memory leak or a physical memory fault."
            }
        };
        if (explicitMemoryScan)
        {
            var browserMb = processes?.BrowserMemoryMb;
            var validBrowser = browserMb is >= 0 && double.IsFinite(browserMb.Value);
            proof.Add(new
            {
                label = "Browser workload", value = validBrowser ? $"{browserMb:0.##} MB" : "Unavailable",
                status = !validBrowser ? "unknown" : "info",
                meaning = "Browser working memory is context only. Its size or process count alone does not establish RAM pressure or a memory leak."
            });
        }
        if (!string.IsNullOrWhiteSpace(processes?.MemoryLeakWarning))
            proof.Add(new
            {
                label = "Application memory warning", value = processes.MemoryLeakWarning, status = "high",
                meaning = "Unusual application memory behavior remains reported by the process inspection."
            });

        return new ResolutionResultDto
        {
            resolution_status = state == MemoryPressureState.Unknown ? "needs_recheck"
                : state == MemoryPressureState.Active ? "still_active" : "resolved",
            resolution_summary = state == MemoryPressureState.Unknown
                ? "Fresh scan could not verify memory pressure because required RAM or process data is unavailable."
                : state == MemoryPressureState.Active
                    ? "Fresh scan still shows memory pressure or an application memory warning."
                    : "Fresh scan no longer shows memory pressure from active apps. No active issue is detected for this saved check.",
            resolution_proof = proof.ToArray()
        };
    }

    private static ResolutionResultDto CheckStorageHealthResolution(
        HardwareProfileDto hardware)
    {
        var failingDrives = hardware.StorageDrives.Count(drive => drive.IsFailingSmart);
        var resolved = failingDrives == 0;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan no longer shows a Windows storage-health warning. No active issue is detected for this saved check."
                : "Fresh scan still shows a storage-health warning. Back up important files and get the drive checked.",
            new object[]
            {
                Proof(
                    "Storage health warning",
                    resolved ? "Not observed" : $"{failingDrives} drive(s) affected",
                    resolved,
                    "Windows is no longer reporting a storage-health warning.",
                    "Windows is still reporting a storage-health warning. Back up important files and get the drive checked.")
            });
    }

    private static ResolutionResultDto CheckStorageUsageResolution(
        HardwareProfileDto hardware,
        double storageMustBeBelow,
        string issueName)
    {
        var primaryDisk = GetPrimaryDisk(hardware);
        var usagePercent = primaryDisk?.UsagePercent ?? 0;
        var resolved = primaryDisk != null && usagePercent < storageMustBeBelow;
        var smartOk = !hardware.StorageDrives.Any(drive => drive.IsFailingSmart);

        return CreateResult(
            resolved,
            resolved
                ? $"Fresh scan no longer shows {issueName}. No active issue is detected for this saved check."
                : primaryDisk == null
                    ? "Fresh scan could not confirm the main storage usage yet."
                    : $"Fresh scan still shows {issueName}.",
            new object[]
            {
                Proof(
                    "Main storage use",
                    primaryDisk == null ? "Not available" : $"{usagePercent:0.##}%",
                    resolved,
                    "Storage use is back below RigMD's high-use range.",
                    primaryDisk == null
                        ? "RigMD could not read the main storage usage yet."
                        : "Storage use is still above RigMD's high-use range."),
                Proof(
                    "Storage health",
                    smartOk ? "S.M.A.R.T. healthy" : "S.M.A.R.T. warning active",
                    smartOk,
                    "Drive health telemetry is not reporting a storage-health warning.",
                    "Drive health telemetry is still reporting a storage-health warning.")
            });
    }

    private static ResolutionResultDto CheckNetworkResolution(
        HardwareProfileDto hardware)
    {
        var network = hardware.Network;
        var pingOk = !network.PingLatencyMs.HasValue || network.PingLatencyMs.Value < 120;
        var lossOk = !network.PacketLossPercent.HasValue || network.PacketLossPercent.Value < 5;
        var signalOk = !network.IsWifi || network.WifiSignalStrength <= 0 || network.WifiSignalStrength > 45;
        var resolved =
            network.HasActiveAdapter &&
            network.HasIpv4Address &&
            network.HasDefaultGateway &&
            network.HasDnsServers &&
            network.DnsResolutionSucceeded &&
            pingOk &&
            lossOk &&
            signalOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows the internet name check is working again, with network adapter, latency, and packet loss within normal limits. No active issue is detected for this saved check."
                : "Fresh scan still shows network evidence that may affect connectivity.",
            new List<object>
            {
                Proof(
                    "Network adapter",
                    network.HasActiveAdapter ? "Found" : "Not found",
                    network.HasActiveAdapter,
                    "Windows can see an active network connection.",
                    "Windows still cannot confirm an active network connection."),
                Proof(
                    "Internet name check",
                    network.DnsResolutionSucceeded ? "Succeeded" : "Failed",
                    network.DnsResolutionSucceeded,
                    "Website names can be turned into reachable addresses again.",
                    "Website names still cannot be turned into reachable addresses."),
                Proof(
                    "Latency",
                    network.PingLatencyMs.HasValue ? $"{network.PingLatencyMs.Value} ms" : "Not available",
                    pingOk,
                    "Network latency is within RigMD's normal range.",
                    "Network latency is still high enough to affect responsiveness."),
                Proof(
                    "Packet loss",
                    network.PacketLossPercent.HasValue ? $"{network.PacketLossPercent.Value:0.##}%" : "Not available",
                    lossOk,
                    "Packet loss is within RigMD's normal range.",
                    "Packet loss is still high enough to cause connection drops.")
            }
            .Concat(network.IsWifi
                ? new object[]
                {
                    Proof(
                        "Wi-Fi signal",
                        network.WifiSignalStrength > 0 ? $"{network.WifiSignalStrength}%" : "Not available",
                        signalOk,
                        "Wi-Fi signal strength is within a usable range.",
                        "Wi-Fi signal strength is still weak and may cause intermittent issues.")
                }
                : Array.Empty<object>())
            .ToArray());
    }

    private static DiskVolumeDto? GetPrimaryDisk(HardwareProfileDto hardware)
    {
        return hardware.AllDisks?
            .FirstOrDefault(disk =>
                disk.Drive.Equals("C:\\", StringComparison.OrdinalIgnoreCase) ||
                disk.Mountpoint.Equals("C:\\", StringComparison.OrdinalIgnoreCase))
            ?? hardware.AllDisks?.FirstOrDefault();
    }

    private static string BuildEventProofValue(
        IReadOnlyList<WindowsEventSummaryDto> eventsSinceDiagnosis,
        bool eventLogAvailable,
        string? queryWarning)
    {
        if (!eventLogAvailable)
        {
            return string.IsNullOrWhiteSpace(queryWarning)
                ? "Event log unavailable"
                : $"Event log unavailable: {queryWarning}";
        }

        if (eventsSinceDiagnosis.Count == 0)
        {
            return "0 found";
        }

        var latest = eventsSinceDiagnosis[0];
        var when = latest.TimeUtc.HasValue
            ? latest.TimeUtc.Value.ToLocalTime().ToString("MMM dd, h:mm tt")
            : "time unknown";

        return $"{eventsSinceDiagnosis.Count} found; latest {latest.Provider} {latest.EventId} at {when}";
    }

    private static string BuildCrashResolutionSummary(
        string issueLabel,
        bool eventLogAvailable,
        int eventCount,
        bool supportingSignalsOk)
    {
        if (!eventLogAvailable)
        {
            return $"Fresh scan could not confirm Windows Event Log evidence for the saved {issueLabel} check.";
        }

        if (eventCount > 0)
        {
            return $"Fresh scan found {eventCount} new {issueLabel} event(s) after this diagnosis.";
        }

        return supportingSignalsOk
            ? $"Fresh scan found no new {issueLabel} events after this diagnosis."
            : $"Fresh scan found no new {issueLabel} events after this diagnosis, but supporting risk signals still need attention.";
    }

    private static ResolutionResultDto CreateResult(
        bool resolved,
        string summary,
        object[] proof)
    {
        return new ResolutionResultDto
        {
            resolution_status = resolved ? "resolved" : "still_active",
            resolution_checked_at = DateTime.UtcNow.ToString("o"),
            resolution_summary = summary,
            resolution_proof = proof
        };
    }

    private static object Proof(
        string label,
        string value,
        bool resolved,
        string normalMeaning,
        string activeMeaning)
    {
        return new
        {
            label,
            value,
            status = resolved ? "normal now" : "still needs attention",
            meaning = resolved ? normalMeaning : activeMeaning
        };
    }
}
