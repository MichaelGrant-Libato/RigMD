using System.Diagnostics;
using System.Security.Claims;
using RigMD.Application.Models;
using RigMD.Api.Services;

namespace RigMD.Api.Middleware;

public sealed record PerformanceContext(int? RecordCount = null, int? UnavailableCount = null, string Mode = "normal", string Engine = "unknown");

public sealed class PerformanceRecordingMiddleware(RequestDelegate next)
{
    public const string ContextKey = "RigMD.Performance";
    public static string? Operation(string method, string path)
    {
        path = path.TrimEnd('/').ToLowerInvariant();
        if (method == "GET" && path == "/api/diagnosis/sessions") return "history.backend";
        if (method != "POST") return null;
        if (path == "/api/diagnosis/local-scan") return "scan.backend";
        if (path.Split('/') is ["", "api", "diagnosis", var id, "check-resolution"] && Guid.TryParse(id, out _)) return "recheck.backend";
        return path is "/api/autonomy/execute" or "/api/autonomy/tools/execute" or "/api/autonomy/close-selected-app" ? "action_request.backend" : null;
    }

    public async Task InvokeAsync(HttpContext context, PerformanceRecorder recorder)
    {
        var operation = Operation(context.Request.Method, context.Request.Path.Value ?? "");
        var client = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (operation == null || context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(client)) { await next(context); return; }
        var timer = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        double? cpuBefore = null;
        try { cpuBefore = process.TotalProcessorTime.TotalMilliseconds; } catch { /* Resource sampling is best effort. */ }
        var failed = false;
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            timer.Stop();
            var info = context.Items[ContextKey] as PerformanceContext ?? new();
            var outcome = context.RequestAborted.IsCancellationRequested ? "canceled"
                : failed || context.Response.StatusCode >= 400 ? "failed"
                : info.UnavailableCount > 0 ? "incomplete" : "completed";
            double? memory = null, cpu = null;
            try { process.Refresh(); memory = process.WorkingSet64 / 1048576d;
                if (cpuBefore.HasValue) cpu = Math.Max(0, process.TotalProcessorTime.TotalMilliseconds - cpuBefore.Value); } catch { }
            recorder.TryRecord(client, new(Guid.NewGuid(), operation, outcome, timer.Elapsed.TotalMilliseconds,
                info.RecordCount, info.UnavailableCount, info.Mode, info.Engine, failed ? 500 : context.Response.StatusCode, memory, cpu));
        }
    }
}
