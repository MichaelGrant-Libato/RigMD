namespace RigMD.Application.Models;

public sealed class AutomaticDiagnosisInput
{
    public string AgentId { get; init; } = string.Empty;

    public Guid CommandId { get; init; }

    public string Mode { get; init; } = "full";

    public IReadOnlyList<string> ComponentIds { get; init; } =
        Array.Empty<string>();

    public string? ScenarioId { get; init; }

    public DateTimeOffset CapturedAt { get; init; }
    public DateTimeOffset? EventsSince { get; init; }

    public HardwareProfileDto Hardware { get; init; } = null!;
}

public sealed class AutomaticDiagnosisResult
{
    public ComponentStatus ComponentStatus { get; init; } = ComponentStatus.Present;

    public IReadOnlyList<string> TargetScope { get; init; } =
        Array.Empty<string>();

    public string PrimaryResult { get; init; } = string.Empty;

    public string? IncidentalWarning { get; init; }

    public string DiagnosedCategory { get; init; } = string.Empty;

    public string ActionCategory { get; init; } = string.Empty;

    public string ConfidenceLabel { get; init; } = string.Empty;

    public string Explanation { get; init; } = string.Empty;

    public string RecommendedNextStep { get; init; } = string.Empty;

    public IReadOnlyList<AutomaticDiagnosisProof> Proof { get; init; } =
        Array.Empty<AutomaticDiagnosisProof>();

    public AutomaticVerificationTarget? VerificationTarget { get; init; }
}

public sealed class AutomaticDiagnosisProof
{
    [System.Text.Json.Serialization.JsonPropertyName("window_kind")]
    public string? WindowKind { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("window_start")]
    public DateTimeOffset? WindowStart { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("window_end")]
    public DateTimeOffset? WindowEnd { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("evidence_kind")]
    public string EvidenceKind { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("rule_version")]
    public string RuleVersion { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("metric_id")]
    public string MetricId { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("captured_at")]
    public DateTimeOffset? CapturedAt { get; set; }

    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Meaning { get; init; } = string.Empty;
}

public sealed class AutomaticVerificationTarget
{
    public string Target { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
