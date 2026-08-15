using RigMD.Application.Models;

namespace RigMD.Application.Services;

public class RecurringPatternService
{
    private static readonly Dictionary<string, int>
        ActionRank =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Monitor"] = 1,
                ["Maintain"] = 2,
                ["Troubleshoot"] = 3,
                ["Escalate"] = 4
            };

    public RecurringResponseDto BuildPatterns(
        IReadOnlyList<RecurringSessionDto> sessions)
    {
        if (sessions.Count == 0)
        {
            return EmptyResponse();
        }

        var symptomGroups =
            sessions
                .Where(
                    session =>
                        !string.IsNullOrWhiteSpace(
                            session.SymptomType
                        )
                )
                .GroupBy(
                    session =>
                        session.SymptomType
                )
                .ToList();

        var causeGroups =
            sessions
                .Where(
                    session =>
                        !string.IsNullOrWhiteSpace(
                            session.DiagnosedCategory
                        )
                )
                .GroupBy(
                    session =>
                        session.DiagnosedCategory
                )
                .ToList();

        var patterns =
            new List<RecurringPatternDto>();

        var usedDisplayPairs =
            new HashSet<string>();

        // =====================================================
        // GROUP BY SYMPTOM
        // =====================================================

        foreach (var symptomGroup in symptomGroups)
        {
            var groupedSessions =
                symptomGroup.ToList();

            if (groupedSessions.Count < 2)
                continue;

            var primaryCause =
                GetMostCommon(
                    groupedSessions
                        .Select(
                            session =>
                                session.DiagnosedCategory
                        )
                )
                ?? "Unspecified Cause";

            var displayPair =
                $"{symptomGroup.Key}|{primaryCause}";

            var pattern =
                CreatePattern(
                    $"symptom:{symptomGroup.Key}",
                    groupedSessions,
                    patterns.Count
                );

            if (pattern == null)
                continue;

            patterns.Add(pattern);

            usedDisplayPairs.Add(
                displayPair
            );
        }

        // =====================================================
        // GROUP BY CAUSE
        // =====================================================

        foreach (var causeGroup in causeGroups)
        {
            var groupedSessions =
                causeGroup.ToList();

            if (groupedSessions.Count < 2)
                continue;

            var primarySymptom =
                GetMostCommon(
                    groupedSessions
                        .Select(
                            session =>
                                session.SymptomType
                        )
                )
                ?? "Repeated Symptom";

            var displayPair =
                $"{primarySymptom}|{causeGroup.Key}";

            if (
                usedDisplayPairs.Contains(
                    displayPair
                )
            )
            {
                continue;
            }

            var pattern =
                CreatePattern(
                    $"cause:{causeGroup.Key}",
                    groupedSessions,
                    patterns.Count
                );

            if (pattern == null)
                continue;

            patterns.Add(pattern);

            usedDisplayPairs.Add(
                displayPair
            );
        }

        // Python sorts by occurrence_count descending
        // AFTER the RP IDs are created.
        patterns =
            patterns
                .OrderByDescending(
                    pattern =>
                        pattern.occurrence_count
                )
                .ToList();

        var timeline =
            new List<RecurringTimelineRowDto>();

        foreach (var pattern in patterns)
        {
            foreach (
                var occurrence
                in pattern.occurrences
            )
            {
                timeline.Add(
                    new RecurringTimelineRowDto
                    {
                        pattern_id =
                            pattern.id,

                        date =
                            occurrence.display_date,

                        symptom =
                            occurrence.symptom,

                        probable_cause =
                            occurrence.probable_cause,

                        action_category =
                            occurrence.action_category,

                        confidence_label =
                            occurrence.confidence_label,

                        status =
                            pattern.status
                    }
                );
            }
        }

        return new RecurringResponseDto
        {
            metrics =
                new RecurringMetricsDto
                {
                    recurring_issues =
                        patterns.Count,

                    worsening_trends =
                        patterns.Count(
                            pattern =>
                                pattern.status
                                    == "Worsening"
                        ),

                    action_escalated =
                        patterns.Count(
                            pattern =>
                                pattern.action_escalated
                        ),

                    total_occurrences =
                        patterns.Sum(
                            pattern =>
                                pattern.occurrence_count
                        )
                },

            patterns =
                patterns,

            timeline =
                timeline
        };
    }

    // =========================================================
    // CREATE PATTERN
    // =========================================================

    private RecurringPatternDto? CreatePattern(
        string patternKey,
        List<RecurringSessionDto> sessions,
        int index)
    {
        if (sessions.Count < 2)
            return null;

        var sortedSessions =
            sessions
                .OrderBy(
                    session =>
                        session.CreatedAt
                        ?? DateTime.MinValue
                )
                .ToList();

        var firstSession =
            sortedSessions.First();

        var latestSession =
            sortedSessions.Last();

        var symptomName =
            GetMostCommon(
                sortedSessions
                    .Select(
                        session =>
                            session.SymptomType
                    )
            )
            ?? "Repeated Symptom";

        var probableCause =
            GetMostCommon(
                sortedSessions
                    .Select(
                        session =>
                            session.DiagnosedCategory
                    )
            )
            ?? "Unspecified Cause";

        var previousAction =
            NormalizeActionCategory(
                firstSession.ActionCategory
            );

        var updatedAction =
            NormalizeActionCategory(
                latestSession.ActionCategory
            );

        var status =
            GetPatternStatus(
                previousAction,
                updatedAction
            );

        var actionEscalated =
            GetActionRank(updatedAction)
            >
            GetActionRank(previousAction);

        var occurrences =
            sortedSessions
                .Select(
                    (session, occurrenceIndex) =>
                        SessionToOccurrence(
                            session,
                            occurrenceIndex,
                            status
                        )
                )
                .ToList();

        return new RecurringPatternDto
        {
            id =
                $"RP-{index + 1:000}",

            pattern_key =
                patternKey,

            symptom =
                symptomName,

            probable_cause =
                probableCause,

            occurrence_count =
                sortedSessions.Count,

            first_detected =
                FormatDate(
                    firstSession.CreatedAt
                ),

            latest_detected =
                FormatDate(
                    latestSession.CreatedAt
                ),

            previous_action =
                previousAction,

            updated_action =
                updatedAction,

            status =
                status,

            action_escalated =
                actionEscalated,

            recommended_next_step =
                GetRecommendedNextStep(
                    status,
                    updatedAction,
                    probableCause
                ),

            occurrences =
                occurrences,

            timeline =
                occurrences
                    .Select(
                        occurrence =>
                            new PatternTimelineDotDto
                            {
                                date =
                                    occurrence.short_date,

                                full_date =
                                    occurrence.display_date,

                                session_id =
                                    occurrence.id
                            }
                    )
                    .ToList()
        };
    }

    // =========================================================
    // OCCURRENCE
    // =========================================================

    private PatternOccurrenceDto
        SessionToOccurrence(
            RecurringSessionDto session,
            int index,
            string status)
    {
        return new PatternOccurrenceDto
        {
            id =
                session.Id.ToString(),

            session_code =
                $"S-{index + 1:000}",

            display_date =
                FormatDate(
                    session.CreatedAt
                ),

            short_date =
                FormatShortDate(
                    session.CreatedAt
                ),

            display_time =
                FormatTime(
                    session.CreatedAt
                ),

            symptom =
                session.SymptomType,

            probable_cause =
                session.DiagnosedCategory,

            action_category =
                NormalizeActionCategory(
                    session.ActionCategory
                ),

            confidence_label =
                NormalizeConfidenceLabel(
                    session.ConfidenceLabel
                ),

            severity =
                session.Severity,

            frequency =
                session.Frequency,

            duration =
                string.IsNullOrWhiteSpace(
                    session.Duration
                )
                    ? "N/A"
                    : session.Duration,

            warning_signs =
                SplitWarningSigns(
                    session.WarningSigns
                ),

            status =
                status
        };
    }

    // =========================================================
    // ACTION NORMALIZATION
    // =========================================================

    public string NormalizeActionCategory(
        string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "Monitor";

        var value =
            action.ToLowerInvariant();

        if (
            value.Contains("escalate") ||
            value.Contains("professional")
        )
        {
            return "Escalate";
        }

        if (
            value.Contains("troubleshoot")
        )
        {
            return "Troubleshoot";
        }

        if (
            value.Contains("maintain")
        )
        {
            return "Maintain";
        }

        if (
            value.Contains("monitor")
        )
        {
            return "Monitor";
        }

        return action;
    }

    private static string
        NormalizeConfidenceLabel(
            string? confidence)
    {
        if (
            string.IsNullOrWhiteSpace(
                confidence
            )
        )
        {
            return "Low Confidence";
        }

        var value =
            confidence.ToLowerInvariant();

        if (value.Contains("high"))
            return "High Confidence";

        if (
            value.Contains("moderate") ||
            value.Contains("medium")
        )
        {
            return "Moderate";
        }

        if (value.Contains("low"))
            return "Low Confidence";

        return confidence;
    }

    // =========================================================
    // STATUS
    // =========================================================

    private string GetPatternStatus(
        string previousAction,
        string updatedAction)
    {
        var previousRank =
            GetActionRank(
                previousAction
            );

        var updatedRank =
            GetActionRank(
                updatedAction
            );

        if (updatedRank > previousRank)
            return "Worsening";

        if (updatedRank < previousRank)
            return "Improving";

        return "Stable";
    }

    private int GetActionRank(
        string? action)
    {
        var normalized =
            NormalizeActionCategory(
                action
            );

        return ActionRank.TryGetValue(
            normalized,
            out var rank
        )
            ? rank
            : 1;
    }

    // =========================================================
    // RECOMMENDED NEXT STEP
    // =========================================================

    private string GetRecommendedNextStep(
        string status,
        string updatedAction,
        string probableCause)
    {
        var action =
            NormalizeActionCategory(
                updatedAction
            );

        if (
            status == "Worsening" ||
            action == "Escalate"
        )
        {
            return
                $"The repeated pattern related to {probableCause} is getting worse. " +
                "Prepare a diagnostic report and consider professional inspection if the issue continues.";
        }

        if (action == "Troubleshoot")
        {
            return
                $"Start troubleshooting steps for {probableCause}. " +
                "Review recent driver, storage, startup, or software changes connected to this symptom.";
        }

        if (action == "Maintain")
        {
            return
                $"Continue maintenance actions related to {probableCause}. " +
                "Monitor if the same symptom appears again in the next diagnostic session.";
        }

        return
            $"Keep monitoring this pattern related to {probableCause}. " +
            "Run another diagnostic if the symptom becomes more frequent or severe.";
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private static string?
        GetMostCommon(
            IEnumerable<string> values)
    {
        var filtered =
            values
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value
                        )
                )
                .ToList();

        if (filtered.Count == 0)
            return null;

        return filtered
            .GroupBy(value => value)
            .OrderByDescending(
                group =>
                    group.Count()
            )
            .First()
            .Key;
    }

    private static string[]
        SplitWarningSigns(
            string? rawWarningSigns)
    {
        if (
            string.IsNullOrWhiteSpace(
                rawWarningSigns
            )
        )
        {
            return Array.Empty<string>();
        }

        return rawWarningSigns
            .Replace(";", "\n")
            .Replace(",", "\n")
            .Split(
                '\n',
                StringSplitOptions
                    .RemoveEmptyEntries |
                StringSplitOptions
                    .TrimEntries
            );
    }

    private static string FormatDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString(
                "MMM dd, yyyy"
            )
            : "No date";
    }

    private static string FormatShortDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString(
                "MMM dd"
            )
            : "N/A";
    }

    private static string FormatTime(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString(
                "hh:mm tt"
            )
            : "No time";
    }

    public static RecurringResponseDto
        EmptyResponse(
            string? warning = null)
    {
        return new RecurringResponseDto
        {
            metrics =
                new RecurringMetricsDto
                {
                    recurring_issues = 0,
                    worsening_trends = 0,
                    action_escalated = 0,
                    total_occurrences = 0
                },

            patterns =
                new List<RecurringPatternDto>(),

            timeline =
                new List<RecurringTimelineRowDto>(),

            database_warning =
                warning
        };
    }
}