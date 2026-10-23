namespace RigMD.Application.Services;

public static class RepairSupportPolicy
{
    public static string? SelectedAppBlockReason(RigMD.Domain.Entities.DiagnosticOutput diagnostic, RigMD.Application.Models.HardwareProfileDto hardware)
    {
        if (RequiresHardwareInspection(diagnostic.DiagnosedCategory)) return HardwareNotice;
        var scope = SessionScope(diagnostic);
        if (!Autonomy.DiagnosticScopeMapper.AllowsMemoryAction(scope.Mode, scope.Components, scope.Scenario, diagnostic.DiagnosedCategory))
            return "App closing is outside this check's scope. Open a separate memory check and review its current result before choosing apps.";
        if (!HasSavedMemoryWarning(diagnostic))
            return "This check has no verified memory warning. Run a memory check before choosing apps to close.";
        var current = MemoryPressureRules.Evaluate(hardware.Ram.UsageAvailable == false ? null : hardware.Ram.UsagePercent,
            hardware.ProcessInsights.BrowserMemoryMb, hardware.ProcessInsights.MemoryLeakWarning, true);
        return current == MemoryPressureState.Active ? null
            : "Memory use is no longer showing the warning, or a fresh reading could not be obtained. No apps were closed. Check the current status before choosing an action.";
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

    public static string? ReviewOnlyReason(string? diagnosedCategory)
    {
        var category = (diagnosedCategory ?? "").ToLowerInvariant();
        if (category.Contains("crash") || category.Contains("stop error") || category.Contains("stability risk"))
            return "Review the affected app, event time and error details in Reliability Monitor. Crash records alone do not identify a repairable cause. RigMD will not recommend Windows repair, cleanup or app closing as a fix for these records. If the problem returns, collect the matching record and follow the app or device maker's guidance.";
        if (category.Contains("unavailable") || category.Contains("could not verify") || category.Contains("no active issue") || category.Contains("component not present"))
            return "No automatic repair is supported by this result. Review the unavailable measurements or select a relevant check if symptoms continue. Missing readings are not evidence that Windows repair or cleanup is needed.";
        return null;
    }
    public const string HardwareNotice = "A drive-health warning was reported. Back up important files and arrange a hardware inspection. RigMD cannot repair physical damage. Cleanup and Windows repair tools are not a repair for this warning; the warning alone does not prove the exact fault.";

    public static bool RequiresHardwareInspection(string? diagnosedCategory)
    {
        var category = (diagnosedCategory ?? "").ToLowerInvariant();
        return category.Contains("storage health") || category.Contains("drive health") ||
            category.Contains("s.m.a.r.t") || category.Contains("smart failure");
    }
}
