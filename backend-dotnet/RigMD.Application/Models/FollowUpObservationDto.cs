using System.Text.Json.Serialization;

namespace RigMD.Application.Models;

public sealed class FollowUpObservationDto
{
    [JsonPropertyName("observation_id")]
    public string ObservationId { get; set; } = string.Empty;
    [JsonPropertyName("checked_at")]
    public string CheckedAt { get; set; } = string.Empty;
    [JsonPropertyName("recorded_at")]
    public string RecordedAt { get; set; } = string.Empty;
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("proof")]
    public object[] Proof { get; set; } = Array.Empty<object>();
    [JsonPropertyName("source")]
    public string Source { get; set; } = "recheck";
}
