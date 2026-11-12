namespace RigMD.Application.Services;

public static class RepairSupportPolicy
{
    public static RigMD.Domain.Entities.DiagnosticOutput? SelectStorageFinding(
        RigMD.Domain.Entities.DiagnosticOutput diagnostic, string metricId, string toolName)
    {
        if (RequiresHardwareInspection(diagnostic.DiagnosedCategory) || (ReviewOnlyReason(diagnostic.DiagnosedCategory) != null && WorkloadGuidanceReason(diagnostic.DiagnosedCategory) == null) ||
            toolName is not ("clear_temp_files" or "clear_browser_cache" or "clear_windows_update_cache" or "uninstall_installed_app" or "recycle_selected_files" or "empty_recycle_bin") ||
            !System.Text.RegularExpressions.Regex.IsMatch(metricId, @"^storage_usage(?:_[A-Za-z]:\\?)?$")) return null;
        var scope = SessionScope(diagnostic);
        var allowed = scope.Mode.ToLowerInvariant() switch
        {
            "full" => true,
            "component" => scope.Components.Any(part => Autonomy.DiagnosticScopeMapper.NormalizeComponentId(part) == "storage"),
            "scenario" => Autonomy.DiagnosticScopeMapper.NormalizeScenarioId(scope.Scenario) == "storage-problem",
            _ => false
        };
        if (!allowed) return null;
        string? Answer(string key) => diagnostic.Session?.Answers.FirstOrDefault(answer => answer.QuestionKey == key)?.AnswerValue;
        var json = Answer("resolution_proof");
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") json = Answer("proof");
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            string Text(System.Text.Json.JsonElement row, string key) => row.ValueKind == System.Text.Json.JsonValueKind.Object &&
                row.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : "";
            if (document.RootElement.EnumerateArray().Any(row => Text(row, "metric_id") == "storage_health" &&
                Text(row, "status") is "high" or "elevated" or "warning" or "critical" or "still needs attention")) return null;
            var rows = document.RootElement.EnumerateArray().Where(row => Text(row, "metric_id") == metricId).ToArray();
            if (toolName is "uninstall_installed_app" or "recycle_selected_files" or "empty_recycle_bin" && (rows.Length != 1 || Text(rows[0], "evidence_kind") != "measurement")) return null;
            if (rows.Length != 1 || Text(rows[0], "status") is not ("high" or "elevated" or "warning" or "critical" or "still needs attention") ||
                Text(rows[0], "evidence_kind") is "unavailable" or "missing_follow_up") return null;
            var percent = System.Text.RegularExpressions.Regex.Match(Text(rows[0], "value"), @"^(\d+(?:\.\d+)?)%");
            if (!percent.Success || !double.TryParse(percent.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var usage) || usage < 80 || usage > 100) return null;
            return new()
            {
                Id = diagnostic.Id, CreatedAt = diagnostic.CreatedAt, DiagnosticSessionId = diagnostic.DiagnosticSessionId,
                Session = diagnostic.Session, DiagnosedCategory = "Elevated Storage Utilization",
                ActionCategory = diagnostic.ActionCategory, ConfidenceLabel = diagnostic.ConfidenceLabel,
                AiExplanation = diagnostic.AiExplanation, ReasoningFactors = diagnostic.ReasoningFactors, WarningSigns = diagnostic.WarningSigns
            };
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public sealed record SelectedAppReviewBlock(string Code, string Message, double? CurrentMemoryUsagePercent = null);

    public static string? SelectedAppBlockReason(RigMD.Domain.Entities.DiagnosticOutput diagnostic, RigMD.Application.Models.HardwareProfileDto hardware)
        => SelectedAppBlockDetails(diagnostic, hardware)?.Message;

    public static SelectedAppReviewBlock? SelectedAppBlockDetails(RigMD.Domain.Entities.DiagnosticOutput diagnostic, RigMD.Application.Models.HardwareProfileDto hardware)
    {
        if (RequiresHardwareInspection(diagnostic.DiagnosedCategory))
            return new("hardware_inspection_required", HardwareNotice);
        var scope = SessionScope(diagnostic);
        if (!Autonomy.DiagnosticScopeMapper.AllowsMemoryAction(scope.Mode, scope.Components, scope.Scenario, diagnostic.DiagnosedCategory))
            return new("outside_memory_scope", "App closing is outside this check's scope. Open a separate memory check and review its current result before choosing apps.");
        if (!HasSavedMemoryWarning(diagnostic))
            return new("saved_memory_warning_missing", "This check has no verified memory warning. Run a memory check before choosing apps to close.");
        var usage = hardware.Ram.UsageAvailable == false ? (double?)null : hardware.Ram.UsagePercent;
        var current = MemoryPressureRules.Evaluate(usage,
            hardware.ProcessInsights.BrowserMemoryMb, hardware.ProcessInsights.MemoryLeakWarning, true);
        if (current == MemoryPressureState.Active) return null;
        if (current == MemoryPressureState.Unknown)
            return new("memory_reading_unavailable", "A current memory reading is unavailable. No apps were closed. Refresh the check before choosing an action.");
        var percent = usage!.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return new("memory_warning_cleared",
            $"Current memory use is {percent}%, below the {MemoryPressureRules.HighUsagePercent:0}% review threshold. The warning is no longer detected in this reading. No apps were closed.", usage);
    }

    public static string? ScopeBlockReason(RigMD.Domain.Entities.DiagnosticOutput diagnostic, string toolName)
    {
        var scope = SessionScope(diagnostic);
        if (string.IsNullOrWhiteSpace(scope.Mode)) return null;
        var allowed = Autonomy.DiagnosticScopeMapper.GetAllowedRemediationTools(scope.Mode, scope.Components, scope.Scenario, diagnostic.DiagnosedCategory);
        return allowed.Contains(toolName, StringComparer.OrdinalIgnoreCase) ? null
            : "This action is outside the selected check's scope. Open the relevant check and review a new action preview.";
    }

    private static (string Mode, string[] Components, string? Scenario) SessionScope(RigMD.Domain.Entities.DiagnosticOutput diagnostic)
    {
        string Answer(string key) => diagnostic.Session?.Answers.FirstOrDefault(a => a.QuestionKey.Equals(key, StringComparison.OrdinalIgnoreCase))?.AnswerValue ?? "";
        var raw = Answer("component_ids").Trim();
        string[] components = [];
        if (raw.StartsWith('['))
        {
            try { components = System.Text.Json.JsonSerializer.Deserialize<string[]>(raw) ?? []; }
            catch (System.Text.Json.JsonException) { }
        }
        else components = raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (Answer("diagnosis_mode").Trim(), components, Answer("scenario_id"));
    }

    private static bool HasSavedMemoryWarning(RigMD.Domain.Entities.DiagnosticOutput diagnostic)
    {
        string? Answer(string key) => diagnostic.Session?.Answers.FirstOrDefault(a => a.QuestionKey == key)?.AnswerValue;
        try
        {
            var json = Answer("resolution_proof");
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") json = Answer("proof");
            if (string.IsNullOrWhiteSpace(json)) return false;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            return doc.RootElement.EnumerateArray().Any(row =>
            {
                string Text(string key) => row.ValueKind == System.Text.Json.JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
                    value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : "";
                if (Text("status") is not ("high" or "elevated" or "still needs attention")) return false;
                if (Text("metric_id") == "memory_app_warning") return !string.IsNullOrWhiteSpace(Text("value")) && Text("evidence_kind") == "reported_status" &&
                    !System.Text.RegularExpressions.Regex.IsMatch(Text("value").Trim(), @"^(unavailable|not available|none|no warning|false)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (Text("metric_id") != "memory_usage") return false;
                var match = System.Text.RegularExpressions.Regex.Match(Text("value"), @"^(\d+(?:\.\d+)?)%");
                return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent) && MemoryPressureRules.IsValidUsage(percent) &&
                    percent >= MemoryPressureRules.HighUsagePercent;
            });
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    public static string? ToolMismatchReason(string? category, string toolName) =>
        ((category ?? "").Contains("memory", StringComparison.OrdinalIgnoreCase) ||
         System.Text.RegularExpressions.Regex.IsMatch(category ?? "", @"\bram\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) &&
        toolName is not "terminate_processes" and not "close_selected_app"
            ? "Storage cleanup and Windows repair are not remedies for high working-memory use. Save work and review specific unused apps before approving a normal close."
            : null;

    public static string? WorkloadGuidanceReason(string? diagnosedCategory)
    {
        var category = (diagnosedCategory ?? "").ToLowerInvariant();
        if (category.Contains("thermal") || category.Contains("overheat") || category.Contains("fan"))
            return "Review airflow, ventilation and the measured temperatures. RigMD does not automatically close apps or repair cooling hardware from this finding. If memory use also needs attention, run a separate memory check before reviewing apps.";
        if (category.Contains("battery") || category.Contains("power"))
            return "Review charging, the power connection and Windows power settings. RigMD cannot confirm battery recovery from a snapshot and does not automatically close apps from this finding. Use a separate memory check before considering app closing.";
        if (category.Contains("elevated cpu") || category.Contains("processor load"))
            return "Review the current workload in Task Manager and save work before closing apps yourself. Processor activity alone does not authorize app closing in RigMD. Run a separate memory check if memory use also needs attention.";
        return null;
    }

    public static string? ReviewOnlyReason(string? diagnosedCategory)
    {
        var category = (diagnosedCategory ?? "").ToLowerInvariant();
        if (category.Contains("crash") || category.Contains("stop error") || category.Contains("stability risk"))
            return "Review the affected app, event time and error details in Reliability Monitor. Crash records alone do not identify a repairable cause. RigMD will not recommend Windows repair, cleanup or app closing as a fix for these records. If the problem returns, collect the matching record and follow the app or device maker's guidance.";
        if (category.Contains("unavailable") || category.Contains("could not verify") || category.Contains("no active issue") || category.Contains("component not present"))
            return "No automatic repair is supported by this result. Review the unavailable measurements or select a relevant check if symptoms continue. Missing readings are not evidence that Windows repair or cleanup is needed.";
        return WorkloadGuidanceReason(diagnosedCategory);
    }
    public const string HardwareNotice = "A drive-health warning was reported. Back up important files and arrange a hardware inspection. RigMD cannot repair physical damage. Cleanup and Windows repair tools are not a repair for this warning; the warning alone does not prove the exact fault.";

    public static bool RequiresHardwareInspection(string? diagnosedCategory)
    {
        var category = (diagnosedCategory ?? "").ToLowerInvariant();
        return category.Contains("storage health") || category.Contains("drive health") ||
            category.Contains("s.m.a.r.t") || category.Contains("smart failure");
    }
}
