using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class QueryPastChecksAndRemediationsTool : IRigMdAgentTool
{
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly IRemediationRepository _remediationRepository;

    public QueryPastChecksAndRemediationsTool(
        IDiagnosticSessionRepository sessionRepository,
        IRemediationRepository remediationRepository)
    {
        _sessionRepository = sessionRepository;
        _remediationRepository = remediationRepository;
    }

    public string Name => "query_past_checks_and_remediations";

    public string DisplayName => "Query Past Checks & Remediation History";

    public string Description =>
        "Queries the local SQLite database for the PC's historical diagnostic sessions ('Past Checks'), resolution statuses, and past remediation runs, including which repair tools succeeded or failed verification.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["maxSessions"] = new AgentToolParameterProperty
                {
                    Type = "INTEGER",
                    Description = "Maximum number of recent diagnostic sessions to inspect (1 to 15, default 8)."
                }
            },
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
            WhatWillHappen = "Reads historical diagnostic checks and remediation outcomes from local SQLite storage."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        int maxSessions = Math.Clamp(ToolArgumentHelper.GetInt(arguments, "maxSessions", 8), 1, 15);
        progressReporter?.Invoke($"Querying local SQLite memory for the last {maxSessions} Past Checks & Remediation outcomes...");

        var allSessions = await _sessionRepository.GetSessionsAsync();
        var recentSessions = allSessions.Take(maxSessions).ToList();
        var failedActionCodes = await _remediationRepository.GetFailedActionCodesAsync();

        var sessionEntries = new List<object>();
        int totalRemediationRuns = 0;

        foreach (var session in recentSessions)
        {
            IReadOnlyList<RemediationRunDto> remHistory = Array.Empty<RemediationRunDto>();
            try
            {
                if (Guid.TryParse(session.SessionId, out var sessionGuid))
                {
                    remHistory = await _sessionRepository.GetRemediationHistoryAsync(sessionGuid);
                    totalRemediationRuns += remHistory.Count;
                }
            }
            catch
            {
                // Ignore if no remediation history exists for this session
            }

            sessionEntries.Add(new
            {
                sessionId = session.SessionId,
                createdAt = session.CreatedAt,
                symptomType = session.SymptomType,
                diagnosedCategory = session.DiagnosedCategory,
                actionCategory = session.ActionCategory,
                confidenceLabel = session.ConfidenceLabel,
                resolutionStatus = session.ResolutionStatus ?? "Pending",
                resolutionSummary = session.ResolutionSummary,
                remediationRuns = remHistory.Select(r => new
                {
                    runId = r.RunId,
                    status = r.Status,
                    completedAt = r.CompletedAt,
                    actions = r.Attempts.Select(a => new
                    {
                        actionCode = a.ActionCode,
                        verificationStatus = a.VerificationStatus,
                        createdAt = a.CreatedAt
                    }).ToList()
                }).ToList()
            });
        }

        var topHistoricalCategories = allSessions
            .Where(s => !string.IsNullOrWhiteSpace(s.DiagnosedCategory))
            .GroupBy(s => s.DiagnosedCategory)
            .Select(g => new { category = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .Take(5)
            .ToList();

        var payload = new
        {
            totalSavedSessions = allSessions.Count,
            inspectedSessionCount = recentSessions.Count,
            totalRemediationRunsInspected = totalRemediationRuns,
            historicallyFailedActionCodes = failedActionCodes,
            topHistoricalCategories,
            recentSessions = sessionEntries
        };

        var topCatText = topHistoricalCategories.Count > 0
            ? string.Join(", ", topHistoricalCategories.Select(c => $"{c.category} ({c.count}x)"))
            : "None";

        var summary = allSessions.Count == 0
            ? "No prior diagnostic checks found in SQLite history (this is the first recorded check on this PC)."
            : $"Found {allSessions.Count} historical check(s) in SQLite (inspected latest {recentSessions.Count}, with {totalRemediationRuns} remediation run(s)). Top historical categories: {topCatText}.";

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
