using System.Text.Json;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class RestartWindowsExplorerTool : IRigMdAgentTool, IApprovalBoundAgentTool
{
    private readonly ILoggerFactory _loggers;
    private readonly IExplorerRestartPlatform _platform;
    private readonly TimeProvider _clock;
    private ExplorerRestartPlan? _approved;
    public RestartWindowsExplorerTool(ILoggerFactory loggers) : this(loggers, new WindowsExplorerRestartPlatform(), TimeProvider.System) { }
    internal RestartWindowsExplorerTool(ILoggerFactory loggers, IExplorerRestartPlatform platform, TimeProvider clock)
    { _loggers = loggers; _platform = platform; _clock = clock; }
    public string Name => "restart_windows_explorer";
    public string DisplayName => "Restart Windows Explorer Shell";
    public string Description => "Restart only the reviewed desktop shell in the current normal Windows user session and verify a replacement shell window. Other sessions are not targeted.";
    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier1_SafeReversible;
    public AgentToolFunctionDeclaration GetFunctionDeclaration() => new() { Name = Name, Description = Description, Parameters = new(), Required = [] };
    private static void Validate(JsonElement args)
    { if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Any()) throw new InvalidOperationException("This action accepts an empty argument object only. Process names and IDs cannot be supplied."); }
    public bool TryBindApprovalContext(object context)
    {
        if (context is not ExplorerRestartPlan plan || !plan.IsCurrent(_clock.GetUtcNow())) return false;
        _approved = plan; return true;
    }
    public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Validate(arguments);
            var context = _platform.Context();
            if (context.Elevated) return Task.FromResult(Blocked("Close RigMD and reopen it normally before reviewing a desktop restart. RigMD will not start an elevated replacement shell."));
            var shell = _platform.ReadShell();
            if (shell == null || !RestartExplorerAction.BelongsTo(shell, context))
                return Task.FromResult(Blocked("The current account's Windows desktop shell could not be verified. No restart is offered."));
            var plan = _approved ?? new(context, shell, _clock.GetUtcNow());
            if (!plan.IsCurrent(_clock.GetUtcNow()) || plan.Context != context || plan.Original != shell)
                return Task.FromResult(Blocked("The approved shell or Windows session changed. Review again."));
            return Task.FromResult(new ToolDryRunPreview { ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier, CanExecute = true,
                RequiresAdmin = false, IsRunningAsAdmin = false, RequiresUserConfirmation = true, ApprovalContext = plan, AffectedItemsCount = 1,
                AffectedTargets = [$"Your Windows desktop shell, PID {shell.Pid}, session {shell.SessionId}"],
                WhatWillHappen = "Stop only the reviewed Explorer desktop shell. Windows must provide a different verified shell process in this same account and session before a restart is reported. Other Windows sessions and separate Explorer processes are not targeted.",
                Warnings = ["Save your work and finish File Explorer copy/move operations first. The desktop and taskbar will disappear briefly; folder windows hosted by this shell may close.",
                    "This interrupts the desktop shell. It does not establish or repair the cause of a display or device problem."] });
        }
        catch (Exception error)
        { return Task.FromResult(Blocked(error is InvalidOperationException ? error.Message : "The desktop shell could not be verified. No restart is offered.")); }
    }
    public async Task<AgentToolExecutionResult> ExecuteAsync(JsonElement arguments, Action<string>? progressReporter = null, CancellationToken cancellationToken = default)
    {
        var plan = Interlocked.Exchange(ref _approved, null);
        ExecutionResult result;
        try
        {
            Validate(arguments);
            if (plan == null) throw new InvalidOperationException("A matching server-approved desktop-shell preview is required. No shell was stopped.");
            result = await new RestartExplorerAction(_loggers.CreateLogger<RestartExplorerAction>()).ExecutePlanAsync(plan, _platform, _clock, cancellationToken, progressReporter);
        }
        catch (Exception error)
        { result = new() { Success = false, Summary = error is InvalidOperationException ? error.Message : "The desktop restart request could not be verified." }; }
        return new() { ToolName = Name, Success = result.Success, Summary = result.Summary, Proof = result.Proof, OutputLog = result.OutputLog,
            DataJson = JsonSerializer.Serialize(new { success = result.Success, summary = result.Summary, proof = result.Proof }, ToolArgumentHelper.JsonOptions) };
    }
    private ToolDryRunPreview Blocked(string reason) => new() { ToolName = Name, DisplayName = DisplayName, SafetyTier = SafetyTier,
        CanExecute = false, RequiresUserConfirmation = true, WhatWillHappen = reason };
}
