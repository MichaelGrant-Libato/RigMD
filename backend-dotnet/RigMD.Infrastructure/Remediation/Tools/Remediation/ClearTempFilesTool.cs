using System.Text.Json;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class ClearTempFilesTool : IRigMdAgentTool, IApprovalBoundAgentTool
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<string> _userTemp, _windowsTemp;
    private readonly TimeProvider _clock;
    private readonly Func<bool> _isAdmin;
    private TempCleanupPlan? _approved;
    public ClearTempFilesTool(ILoggerFactory loggerFactory) : this(loggerFactory, Path.GetTempPath,
        () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), TimeProvider.System, ToolArgumentHelper.IsCurrentProcessElevated) { }
    internal ClearTempFilesTool(ILoggerFactory loggerFactory, Func<string> userTemp, Func<string> windowsTemp, TimeProvider clock, Func<bool> isAdmin)
    { _loggerFactory = loggerFactory; _userTemp = userTemp; _windowsTemp = windowsTemp; _clock = clock; _isAdmin = isAdmin; }
    public string Name => "clear_temp_files";
    public string DisplayName => "Clear Temporary Files";
    public string Description => "Permanently delete only the temporary files captured in the approved preview. Files are not moved to the Recycle Bin and RigMD cannot restore them. Empty directories are left alone.";
    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;
    public AgentToolFunctionDeclaration GetFunctionDeclaration() => new()
    {
        Name = Name, Description = Description,
        Parameters = new()
        {
            ["includeWindowsTemp"] = new() { Type = "boolean", Description = "Include Windows Temp (default false); administrator access is required when true." },
            ["minAgeMinutes"] = new() { Type = "integer", Description = "Whole minutes from 0 to 525600 (default 0). Both creation and last-write times must be at or before the preview-time cutoff. Future-dated files are excluded." }
        }
    };
    public bool TryBindApprovalContext(object context)
    {
        if (context is not TempCleanupPlan plan || !plan.IsCurrent(_clock.GetUtcNow())) return false;
        _approved = plan; return true;
    }
    public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var spec = TempCleanupSpecification.Parse(arguments);
            if (spec.IncludeWindowsTemp && !_isAdmin()) return Task.FromResult(Blocked("Windows administrator access is required for the selected Windows Temp cleanup."));
            var paths = new List<string> { _userTemp() }; if (spec.IncludeWindowsTemp) paths.Add(_windowsTemp());
            var plan = _approved;
            if (plan != null)
            {
                if (!plan.IsCurrent(_clock.GetUtcNow()) || plan.Specification != spec || !plan.Roots.Select(root => root.Path).SequenceEqual(paths.Select(TempCleanupFiles.NormalizeRoot).Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                    return Task.FromResult(Blocked("The approved cleanup scope changed or expired. Review a fresh preview."));
            }
            else plan = TempCleanupPlan.Capture(spec, paths, _clock.GetUtcNow(), cancellationToken);
            var preview = new ToolDryRunPreview
            {
                ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier, CanExecute = true, RequiresUserConfirmation = true,
                RequiresAdmin = spec.IncludeWindowsTemp, IsRunningAsAdmin = _isAdmin(), ApprovalContext = plan,
                AffectedItemsCount = plan.Files.Length, EstimatedBytesAffected = plan.Files.Sum(file => file.Identity.Bytes),
                AffectedTargets = plan.Roots.Select(root => root.Path).ToList(),
                WhatWillHappen = $"Permanently delete up to {plan.Files.Length} eligible temporary files recorded in this preview, with a minimum age of {spec.MinAgeMinutes} minute(s) at preview time. Files that changed, disappeared, are locked or cannot be verified are skipped. Only unchanged approved files may be deleted; new files are left alone. Skipped files need a new review before they can be considered again. This does not use the Recycle Bin and cannot be undone by RigMD."
            };
            if (plan.Partial) preview.Warnings.Add("This is a partial, bounded snapshot. Some files were skipped or could not be read. Only the files captured here may be removed; further cleanup requires another review.");
            return Task.FromResult(preview);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return Task.FromResult(Blocked(error is InvalidOperationException ? error.Message : "The temporary folders could not be verified. Nothing will be deleted.")); }
    }
    public async Task<AgentToolExecutionResult> ExecuteAsync(JsonElement arguments, Action<string>? progressReporter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Interlocked.Exchange(ref _approved, null);
        ExecutionResult result;
        try
        {
            var spec = TempCleanupSpecification.Parse(arguments);
            var paths = new List<string> { _userTemp() }; if (spec.IncludeWindowsTemp) paths.Add(_windowsTemp());
            if (plan == null || plan.Specification != spec || spec.IncludeWindowsTemp && !_isAdmin() ||
                !plan.Roots.Select(root => root.Path).SequenceEqual(paths.Select(TempCleanupFiles.NormalizeRoot).Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("A matching server-approved snapshot is required. Nothing was deleted.");
            result = await new ClearTempFilesAction(_loggerFactory.CreateLogger<ClearTempFilesAction>()).ExecutePlanAsync(plan, _clock.GetUtcNow(), cancellationToken, progressReporter);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        { result = new() { Success = false, Summary = error.Message }; }
        return new() { ToolName = Name, Success = result.Success, Summary = result.Summary, Proof = result.Proof, OutputLog = result.OutputLog,
            DataJson = JsonSerializer.Serialize(new { success = result.Success, summary = result.Summary, proof = result.Proof }, ToolArgumentHelper.JsonOptions) };
    }
    private ToolDryRunPreview Blocked(string reason) => new() { ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier, CanExecute = false, RequiresUserConfirmation = true, WhatWillHappen = reason };
}
