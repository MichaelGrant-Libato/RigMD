using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RigMD.Application.Models;

public class DiagnosticSessionDto
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("symptom_type")]
    public string SymptomType { get; set; } = string.Empty;

    [JsonPropertyName("affected_activity")]
    public string AffectedActivity { get; set; } = string.Empty;

    [JsonPropertyName("frequency")]
    public string Frequency { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("recent_changes")]
    public string RecentChanges { get; set; } = string.Empty;

    [JsonPropertyName("system_state")]
    public string SystemState { get; set; } = string.Empty;

    [JsonPropertyName("warning_signs")]
    public string WarningSigns { get; set; } = string.Empty;

    [JsonPropertyName("diagnosed_category")]
    public string DiagnosedCategory { get; set; } = string.Empty;

    [JsonPropertyName("action_category")]
    public string ActionCategory { get; set; } = string.Empty;

    [JsonPropertyName("confidence_label")]
    public string ConfidenceLabel { get; set; } = string.Empty;

    [JsonPropertyName("ai_explanation")]
    public string AiExplanation { get; set; } = string.Empty;

    [JsonPropertyName("is_recurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("days_ago")]
    public int? DaysAgo { get; set; }

    [JsonPropertyName("display_date")]
    public string? DisplayDate { get; set; }

    [JsonPropertyName("display_time")]
    public string? DisplayTime { get; set; }

    [JsonPropertyName("session_code")]
    public string? SessionCode { get; set; }

    [JsonPropertyName("recommended_next_step")]
    public string? RecommendedNextStep { get; set; }

    [JsonPropertyName("resolution_status")]
    public string ResolutionStatus { get; set; } = string.Empty;

    [JsonPropertyName("resolution_checked_at")]
    public string? ResolutionCheckedAt { get; set; }

    [JsonPropertyName("resolution_summary")]
    public string ResolutionSummary { get; set; } = string.Empty;

    [JsonPropertyName("resolution_proof")]
    public object[] ResolutionProof { get; set; } = Array.Empty<object>();

    [JsonPropertyName("last_action_status")]
    public string LastActionStatus { get; set; } = string.Empty;

    [JsonPropertyName("last_action_summary")]
    public string LastActionSummary { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("remediation_history")]
    public List<RemediationRunDto> RemediationHistory { get; set; } = new();
}

/// <summary>
/// Lightweight projection of a RemediationRun for API responses.
/// </summary>
public class RemediationRunDto
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("attempts")]
    public List<ActionAttemptDto> Attempts { get; set; } = new();
}

/// <summary>
/// Lightweight projection of an ActionAttempt for API responses.
/// </summary>
public class ActionAttemptDto
{
    [JsonPropertyName("action_code")]
    public string ActionCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_status")]
    public string? VerificationStatus { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
}
