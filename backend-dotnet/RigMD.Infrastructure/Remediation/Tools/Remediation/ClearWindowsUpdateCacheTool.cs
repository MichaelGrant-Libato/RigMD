using System.Text.Json;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class ClearWindowsUpdateCacheTool : IRigMdAgentTool, IApprovalBoundAgentTool
{
    private readonly ILoggerFactory _loggers;
    private readonly Func<string> _root;
    private readonly Func<bool> _isAdmin;
    private readonly IUpdateServiceControl _service;
    private readonly TimeProvider _clock;
    private UpdateCachePlan? _approved;
    public ClearWindowsUpdateCacheTool(ILoggerFactory loggers) : this(loggers,
        () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
        ToolArgumentHelper.IsCurrentProcessElevated, new WindowsUpdateServiceControl(), TimeProvider.System) { }
    internal ClearWindowsUpdateCacheTool(ILoggerFactory loggers, Func<string> root, Func<bool> isAdmin, IUpdateServiceControl service, TimeProvider clock)
    { _loggers = loggers; _root = root; _isAdmin = isAdmin; _service = service; _clock = clock; }
    public string Name => "clear_windows_update_cache";
    public string DisplayName => "Clear Windows Update Download Cache";
    public string Description => "Permanently remove only unchanged update-download files in the approved snapshot, with Windows Update stopped. Restore and verify its original service state. Installed updates are not removed.";
    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;
    public AgentToolFunctionDeclaration GetFunctionDeclaration() => new() { Name = Name, Description = Description, Parameters = new(), Required = [] };
    private static void Validate(JsonElement args)
    { if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Any()) throw new InvalidOperationException("This action accepts an empty argument object only. Paths and service names cannot be supplied."); }
    private void Check(UpdateCachePlan plan)
    {
        if (!plan.Files.IsCurrent(_clock.GetUtcNow()) || plan.Files.Roots.Length != 1 ||
            !plan.Files.Roots[0].Path.Equals(TempCleanupFiles.NormalizeRoot(_root()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The approved update-cache snapshot changed, expired or was already used.");
        if (_service.ReadState() != plan.OriginalState) throw new InvalidOperationException("Windows Update service state changed. Review a fresh preview.");
    }
    public bool TryBindApprovalContext(object context)
    {
        if (context is not UpdateCachePlan plan || !plan.Files.IsCurrent(_clock.GetUtcNow())) return false;
        _approved = plan; return true;
    }
    public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Validate(arguments);
            if (!_isAdmin()) return Task.FromResult(Blocked("Reopen RigMD as administrator, then review a fresh update-cache preview."));
            var plan = _approved;
            if (plan != null) Check(plan);
            else
            {
                var initial = _service.ReadState();
                if (initial is not (UpdateServiceState.Running or UpdateServiceState.Stopped))
                    return Task.FromResult(Blocked("Windows Update is changing state or cannot be verified. Wait and review again."));
                plan = new(TempCleanupPlan.Capture(new(false, 0), [_root()], _clock.GetUtcNow(), cancellationToken), initial);
            }
            var count = plan.Files.Files.Length;
            var preview = new ToolDryRunPreview { ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier,
                CanExecute = count > 0, RequiresAdmin = true, IsRunningAsAdmin = true, RequiresUserConfirmation = true, ApprovalContext = plan,
                AffectedItemsCount = count, EstimatedBytesAffected = plan.Files.Files.Sum(file => file.Identity.Bytes),
                AffectedTargets = [plan.Files.Roots[0].Path, $"Windows Update service (currently {plan.OriginalState})"],
                WhatWillHappen = count == 0 ? "No eligible update-cache files were captured. No cleanup or service change is offered."
                    : $"Permanently delete up to {count} unchanged files included in this preview. Windows Update must be confirmed stopped before deletion, and its original {plan.OriginalState} state must be restored and verified. New files and changed or locked files are left alone. Installed updates are not removed." };
            preview.Warnings.Add("Do not run this during a Windows Update installation. Deletion bypasses the Recycle Bin; RigMD cannot undo it. Windows may download these packages again.");
            if (plan.Files.Partial) preview.Warnings.Add("This is a bounded, partial snapshot. Other download-cache files may remain.");
            return Task.FromResult(preview);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        { return Task.FromResult(Blocked(error is InvalidOperationException ? error.Message : "Update files or the service could not be verified. No cleanup is offered.")); }
    }
    public async Task<AgentToolExecutionResult> ExecuteAsync(JsonElement arguments, Action<string>? progressReporter = null, CancellationToken cancellationToken = default)
    {
        var plan = Interlocked.Exchange(ref _approved, null);
        ExecutionResult result;
        try
        {
            Validate(arguments);
            if (!_isAdmin() || plan == null) throw new InvalidOperationException("Administrator access and a matching approved snapshot are required. No service was changed.");
            Check(plan);
            result = await new ClearWindowsUpdateCacheAction(_loggers.CreateLogger<ClearWindowsUpdateCacheAction>()).ExecutePlanAsync(plan, _service, _clock, cancellationToken, progressReporter);
        }
        catch (Exception error)
        { result = new() { Success = false, Summary = error is InvalidOperationException ? error.Message : "The update-cache request could not be verified. No cleanup was started." }; }
        return new() { ToolName = Name, Success = result.Success, Summary = result.Summary, Proof = result.Proof, OutputLog = result.OutputLog,
            DataJson = JsonSerializer.Serialize(new { success = result.Success, summary = result.Summary, proof = result.Proof }, ToolArgumentHelper.JsonOptions) };
    }
    private ToolDryRunPreview Blocked(string reason) => new() { ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier,
        CanExecute = false, RequiresAdmin = true, RequiresUserConfirmation = true, WhatWillHappen = reason };
}
