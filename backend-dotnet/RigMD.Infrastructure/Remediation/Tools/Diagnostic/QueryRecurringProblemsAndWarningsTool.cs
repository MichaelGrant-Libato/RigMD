using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class QueryRecurringProblemsAndWarningsTool : IRigMdAgentTool
{
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly RecurringPatternService _recurringPatternService;
    private readonly WarningSignService _warningSignService;

    public QueryRecurringProblemsAndWarningsTool(
        IDiagnosticSessionRepository sessionRepository,
        RecurringPatternService recurringPatternService,
        WarningSignService warningSignService)
    {
        _sessionRepository = sessionRepository;
        _recurringPatternService = recurringPatternService;
        _warningSignService = warningSignService;
    }

    public string Name => "query_recurring_problems_and_warnings";

    public string DisplayName => "Query Repeated Problems & Active Warning Signs";

    public string Description =>
        "Analyzes historical diagnostic patterns ('Repeated Problems') and observed early warning signs ('Alerts') across the PC's history to identify chronic recurring hardware/OS issues and escalation recommendations.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>(),
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = false,
            WhatWillHappen = "Analyzes recurring diagnostic patterns and active warning signs without modifying system state."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Analyzing Repeated Problems and Active Warning Signs across PC history...");

        var recurringSessions = await _sessionRepository.GetRecurringSessionsAsync();
        var recurringResponse = _recurringPatternService.BuildPatterns(recurringSessions);

        var observedTexts = await _sessionRepository.GetObservedWarningTextsAsync();
        var warningsResponse = _warningSignService.BuildReference(
            observedTexts,
            category: "all",
            search: string.Empty,
            observedOnly: true);

        var patterns = (recurringResponse.patterns ?? new List<RecurringPatternDto>())
            .Take(8)
            .Select(p => new
            {
                id = p.id,
                symptom = p.symptom,
                primaryCause = p.probable_cause,
                occurrences = p.occurrence_count,
                highestAction = p.updated_action,
                status = p.status,
                escalationNeeded = p.action_escalated,
                patternInsight = p.recommended_next_step
            })
            .ToList();

        var activeWarnings = (warningsResponse.warning_signs ?? new List<WarningSignRowDto>())
            .Take(8)
            .Select(w => new
            {
                id = w.id,
                warningSign = w.warning_sign,
                category = w.category,
                action = w.action,
                observedCount = w.observed_count,
                meaning = w.meaning
            })
            .ToList();

        var mostCommonCause = patterns.FirstOrDefault()?.primaryCause;
        var mostRepeatedSymptom = patterns.FirstOrDefault()?.symptom;
        var escalationCount = recurringResponse.metrics?.action_escalated ?? 0;

        var payload = new
        {
            recurringPatternsCount = patterns.Count,
            worseningTrendsCount = recurringResponse.metrics?.worsening_trends ?? 0,
            escalationCount,
            mostRepeatedSymptom,
            mostCommonCause,
            recurringPatterns = patterns,
            activeWarningSignsCount = activeWarnings.Count,
            activeWarningSigns = activeWarnings
        };

        var patternSummary = patterns.Count > 0
            ? $"{patterns.Count} recurring pattern(s) detected (most common cause: {mostCommonCause ?? "N/A"}, escalations: {escalationCount})"
            : "0 recurring problem patterns detected";

        var warningSummary = activeWarnings.Count > 0
            ? $"{activeWarnings.Count} active warning sign(s) observed ({string.Join(", ", activeWarnings.Take(3).Select(w => $"{w.warningSign} [{w.observedCount}x]"))})"
            : "0 active warning signs observed";

        var summary = $"{patternSummary} | {warningSummary}.";

        return new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        };
    }
}
