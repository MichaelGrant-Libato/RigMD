using System.Text.Json;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class ClearBrowserCacheTool : IRigMdAgentTool, IApprovalBoundAgentTool
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<string> _localAppData;
    private readonly TimeProvider _clock;
    private BrowserCachePlan? _approved;

    public ClearBrowserCacheTool(ILoggerFactory loggerFactory) : this(loggerFactory,
        () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TimeProvider.System) { }
    internal ClearBrowserCacheTool(ILoggerFactory loggerFactory, Func<string> localAppData, TimeProvider clock)
    { _loggerFactory = loggerFactory; _localAppData = localAppData; _clock = clock; }

    public string Name => "clear_browser_cache";
    public string DisplayName => "Clear Web Browser Disk Caches";
    public string Description => "Permanently delete only browser-cache files captured in the approved preview for the selected browsers. This does not use the Recycle Bin and RigMD cannot undo it. Profile settings, passwords, cookies and history are not cleanup targets.";
    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;
    public AgentToolFunctionDeclaration GetFunctionDeclaration() => new()
    {
        Name = Name, Description = Description,
        Parameters = new()
        {
            ["browsers"] = new() { Type = "array", ItemsType = "string",
                Description = "Select exact identifiers: chrome, edge, firefox. Default is all three. Empty, duplicate and unsupported selections are rejected." }
        }
    };

    public bool TryBindApprovalContext(object context)
    {
        if (context is not BrowserCachePlan plan || !plan.Snapshot.IsCurrent(_clock.GetUtcNow()) || plan.Snapshot.Files.Length == 0) return false;
        _approved = plan; return true;
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var spec = BrowserCacheSpecification.Parse(arguments);
            var basePath = BrowserCachePlan.NormalizeBase(_localAppData());
            var plan = _approved;
            if (plan != null)
            {
                if (!plan.Snapshot.IsCurrent(_clock.GetUtcNow()) || plan.Specification != spec ||
                    !string.Equals(plan.LocalAppData, basePath, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Blocked("The approved browser-cache scope changed or expired. Review a fresh preview."));
            }
            else plan = BrowserCachePlan.Capture(spec, basePath, _clock.GetUtcNow(), cancellationToken);
            var count = plan.Snapshot.Files.Length;
            var preview = new ToolDryRunPreview
            {
                ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier, CanExecute = count > 0,
                RequiresUserConfirmation = true, RequiresAdmin = false, IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
                ApprovalContext = plan, AffectedItemsCount = count,
                EstimatedBytesAffected = plan.Snapshot.Files.Sum(file => file.Identity.Bytes),
                AffectedTargets = spec.Browsers.Select(browser => browser switch
                {
                    "chrome" => "Google Chrome - Default profile cache",
                    "edge" => "Microsoft Edge - Default profile cache",
                    _ => "Mozilla Firefox - local profile cache2 folders"
                }).ToList(),
                Warnings = plan.Warnings.ToList(),
                WhatWillHappen = count == 0 ? "No eligible browser-cache files were captured. Nothing can be deleted from this preview."
                    : $"Permanently delete up to {count} unchanged browser-cache files recorded in this preview, only for {string.Join(", ", spec.Browsers)}. New, changed, linked, locked or unverifiable files are left alone. No apps are closed. This does not use the Recycle Bin and RigMD cannot undo it."
            };
            preview.Warnings.Add("This review covers Chrome and Edge Default-profile Cache folders and Firefox local-profile cache2 folders, not every browser or profile. Active browsers may change or lock cache files; affected files will be skipped. File sizes are not guaranteed recovered drive space.");
            return Task.FromResult(preview);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return Task.FromResult(Blocked(error is InvalidOperationException ? error.Message : "Browser-cache folders could not be verified. Nothing will be deleted.")); }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(JsonElement arguments, Action<string>? progressReporter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Interlocked.Exchange(ref _approved, null);
        ExecutionResult result;
        try
        {
            var spec = BrowserCacheSpecification.Parse(arguments);
            if (plan == null || plan.Specification != spec ||
                !string.Equals(plan.LocalAppData, BrowserCachePlan.NormalizeBase(_localAppData()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A matching server-approved browser-cache snapshot is required. Nothing was deleted.");
            result = await new ClearBrowserCacheAction(_loggerFactory.CreateLogger<ClearBrowserCacheAction>())
                .ExecutePlanAsync(plan, _clock.GetUtcNow(), cancellationToken, progressReporter);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { result = new() { Success = false, Summary = error.Message }; }
        return new()
        {
            ToolName = Name, Success = result.Success, Summary = result.Summary, Proof = result.Proof, OutputLog = result.OutputLog,
            DataJson = JsonSerializer.Serialize(new { success = result.Success, summary = result.Summary, proof = result.Proof }, ToolArgumentHelper.JsonOptions)
        };
    }

    private ToolDryRunPreview Blocked(string reason) => new()
    { ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier, CanExecute = false, RequiresUserConfirmation = true, WhatWillHappen = reason };
}
