namespace RigMD.Application.Models;

public class RecurringSessionDto
{
    public Guid Id { get; set; }

    public string SymptomType { get; set; } =
        string.Empty;

    public string DiagnosedCategory { get; set; } =
        string.Empty;

    public string ActionCategory { get; set; } =
        string.Empty;

    public string ConfidenceLabel { get; set; } =
        string.Empty;

    public string Severity { get; set; } =
        string.Empty;

    public string Frequency { get; set; } =
        string.Empty;

    public string Duration { get; set; } =
        "N/A";

    public string WarningSigns { get; set; } =
        string.Empty;

    public DateTime? CreatedAt { get; set; }
}

public class RecurringMetricsDto
{
    public int recurring_issues { get; set; }

    public int worsening_trends { get; set; }

    public int action_escalated { get; set; }

    public int total_occurrences { get; set; }
}

public class PatternOccurrenceDto
{
    public string id { get; set; } =
        string.Empty;

    public string session_code { get; set; } =
        string.Empty;

    public string display_date { get; set; } =
        string.Empty;

    public string short_date { get; set; } =
        string.Empty;

    public string display_time { get; set; } =
        string.Empty;

    public string symptom { get; set; } =
        string.Empty;

    public string probable_cause { get; set; } =
        string.Empty;

    public string action_category { get; set; } =
        string.Empty;

    public string confidence_label { get; set; } =
        string.Empty;

    public string severity { get; set; } =
        string.Empty;

    public string frequency { get; set; } =
        string.Empty;

    public string duration { get; set; } =
        "N/A";

    public string[] warning_signs { get; set; } =
        Array.Empty<string>();

    public string status { get; set; } =
        string.Empty;
}

public class PatternTimelineDotDto
{
    public string date { get; set; } =
        string.Empty;

    public string full_date { get; set; } =
        string.Empty;

    public string session_id { get; set; } =
        string.Empty;
}

public class RecurringPatternDto
{
    public string id { get; set; } =
        string.Empty;

    public string pattern_key { get; set; } =
        string.Empty;

    public string symptom { get; set; } =
        string.Empty;

    public string probable_cause { get; set; } =
        string.Empty;

    public int occurrence_count { get; set; }

    public string first_detected { get; set; } =
        string.Empty;

    public string latest_detected { get; set; } =
        string.Empty;

    public string previous_action { get; set; } =
        string.Empty;

    public string updated_action { get; set; } =
        string.Empty;

    public string status { get; set; } =
        string.Empty;

    public bool action_escalated { get; set; }

    public string recommended_next_step { get; set; } =
        string.Empty;

    public List<PatternOccurrenceDto> occurrences { get; set; } =
        new();

    public List<PatternTimelineDotDto> timeline { get; set; } =
        new();
}

public class RecurringTimelineRowDto
{
    public string pattern_id { get; set; } =
        string.Empty;

    public string date { get; set; } =
        string.Empty;

    public string symptom { get; set; } =
        string.Empty;

    public string probable_cause { get; set; } =
        string.Empty;

    public string action_category { get; set; } =
        string.Empty;

    public string confidence_label { get; set; } =
        string.Empty;

    public string status { get; set; } =
        string.Empty;
}

public class RecurringResponseDto
{
    public RecurringMetricsDto metrics { get; set; } =
        new();

    public List<RecurringPatternDto> patterns { get; set; } =
        new();

    public List<RecurringTimelineRowDto> timeline { get; set; } =
        new();

    public string? database_warning { get; set; }
}