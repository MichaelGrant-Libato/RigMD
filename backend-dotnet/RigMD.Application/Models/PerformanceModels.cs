namespace RigMD.Application.Models;

public sealed record PerformanceSample(
    Guid Id, string Operation, string Outcome, double DurationMs,
    int? RecordCount = null, int? UnavailableCount = null, string Mode = "normal", string Engine = "unknown",
    int? HttpStatus = null, double? ApiWorkingSetMb = null, double? ApiCpuTimeMs = null);

public static class PerformanceRules
{
    public const int RetainedRecords = 2000;
    public static bool IsClientOperation(string operation) => operation is
        "startup.desktop" or "startup.api_start.desktop" or "startup.api_ready.desktop" or
        "startup.webview.desktop" or "startup.dashboard.desktop" or "startup.summary.desktop" or
        "scan.ui" or "history.ui" or "report.ui" or "pdf.desktop" or "pdf_request.ui" or "text.ui";
    public static bool IsOperation(string operation) => IsClientOperation(operation) || operation is
        "scan.backend" or "history.backend" or "recheck.backend" or "action_request.backend";
    public static bool IsValid(PerformanceSample sample) => sample.Id != Guid.Empty && IsOperation(sample.Operation)
        && sample.Outcome is "completed" or "failed" or "canceled" or "incomplete"
        && double.IsFinite(sample.DurationMs) && sample.DurationMs is >= 0 and <= 86_400_000
        && (sample.RecordCount is null or >= 0 and <= 100_000)
        && (sample.UnavailableCount is null or >= 0 and <= 100_000)
        && sample.Mode is "normal" or "full" or "component" or "scenario" or "history_50" or "essential_home"
        && (sample.Mode != "essential_home" || sample.Operation.StartsWith("startup.", StringComparison.Ordinal))
        && sample.Engine is "unknown" or "local" or "gemini";
}
