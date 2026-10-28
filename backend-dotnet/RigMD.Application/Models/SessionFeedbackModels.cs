using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RigMD.Application.Models;

public sealed class SaveSessionFeedbackRequest
{
    public const int MaxNoteLength = 500;
    public static bool IsKnownOutcome(string? value) => value is "still_happening" or "not_happening" or "not_sure";

    [Required, RegularExpression("^(still_happening|not_happening|not_sure)$")]
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [StringLength(MaxNoteLength)]
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed record SessionFeedbackDto(
    [property: JsonPropertyName("feedback_id")] Guid FeedbackId,
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
