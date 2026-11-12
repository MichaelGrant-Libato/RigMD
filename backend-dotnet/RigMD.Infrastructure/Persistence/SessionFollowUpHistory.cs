using System.Globalization;
using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Infrastructure.Persistence;

// Separate immutable rows use the existing answers table so upgrades do not rebuild user databases.
internal static class SessionFollowUpHistory
{
    internal const string Key = "resolution_observation_v1";

    internal static void PreserveLegacy(RigMdDbContext db, DiagnosticSession session)
    {
        if (session.Answers.Any(answer => answer.QuestionKey == Key)) return;
        var legacy = ReadLegacy(session);
        if (legacy != null) Add(db, session, legacy);
    }

    internal static void Append(RigMdDbContext db, DiagnosticSession session, string status, string checkedAt,
        string summary, object[] proof, string deviceId)
    {
        PreserveLegacy(db, session);
        Add(db, session, new FollowUpObservationDto
        {
            ObservationId = Guid.NewGuid().ToString(), CheckedAt = checkedAt,
            RecordedAt = DateTimeOffset.UtcNow.ToString("O"), DeviceId = deviceId,
            Status = status, Summary = summary, Proof = proof
        });
    }

    private static void Add(RigMdDbContext db, DiagnosticSession session, FollowUpObservationDto observation)
    {
        var answer = new SessionAnswer
        {
            DiagnosticSessionId = session.Id, Session = session,
            QuestionKey = Key, AnswerValue = JsonSerializer.Serialize(observation)
        };
        session.Answers.Add(answer);
        db.SessionAnswers.Add(answer);
    }

    internal static (List<FollowUpObservationDto> Items, string Status) Read(DiagnosticSession session)
    {
        var items = new List<FollowUpObservationDto>();
        var unavailable = false;
        foreach (var answer in session.Answers.Where(answer => answer.QuestionKey == Key))
        {
            try
            {
                var item = JsonSerializer.Deserialize<FollowUpObservationDto>(answer.AnswerValue);
                if (item == null || string.IsNullOrWhiteSpace(item.ObservationId) ||
                    string.IsNullOrWhiteSpace(item.CheckedAt))
                { unavailable = true; continue; }
                unavailable |= item.Source == null || item.Status == null || item.Summary == null || item.Proof == null;
                item.Source ??= "unknown";
                item.Status ??= "not_recorded";
                item.Summary ??= string.Empty;
                var rawProof = item.Proof ?? Array.Empty<object>();
                item.Proof = DiagnosticSessionRepository.ReadSavedProof(JsonSerializer.Serialize(rawProof));
                unavailable |= rawProof.Length != item.Proof.Length;
                items.Add(item);
            }
            catch (JsonException) { unavailable = true; }
        }
        if (!session.Answers.Any(answer => answer.QuestionKey == Key))
        {
            var legacy = ReadLegacy(session);
            if (legacy != null) items.Add(legacy);
        }
        items = items.DistinctBy(item => item.ObservationId).OrderByDescending(item => ParseTime(item.CheckedAt))
            .ThenByDescending(item => ParseTime(item.RecordedAt))
            .ThenBy(item => item.ObservationId, StringComparer.Ordinal).ToList();
        return (items, unavailable ? "partially_unavailable"
            : items.Any(item => item.Source.StartsWith("legacy_", StringComparison.Ordinal)) ? "legacy_latest_only"
            : items.Count > 0 ? "complete" : "not_recorded");
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at : DateTimeOffset.MinValue;

    private static FollowUpObservationDto? ReadLegacy(DiagnosticSession session)
    {
        string Answer(string key) => session.Answers.FirstOrDefault(a => a.QuestionKey == key)?.AnswerValue ?? "";
        var checkedAt = Answer("resolution_checked_at");
        var status = Answer("resolution_status");
        var summary = Answer("resolution_summary");
        var proofJson = Answer("resolution_proof");
        var source = "legacy_latest";
        if (string.IsNullOrWhiteSpace(checkedAt))
        {
            try
            {
                using var prior = JsonDocument.Parse(Answer("pre_action_readings"));
                if (prior.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!prior.RootElement.TryGetProperty("checked_at", out var time) || time.ValueKind != JsonValueKind.String ||
                    !prior.RootElement.TryGetProperty("proof", out var proof) || proof.ValueKind != JsonValueKind.Array) return null;
                checkedAt = time.GetString() ?? "";
                proofJson = proof.GetRawText();
                status = "not_recorded";
                summary = "Saved readings before an earlier action. The earlier outcome summary was not retained.";
                source = "legacy_pre_action";
            }
            catch (JsonException) { return null; }
        }
        if (string.IsNullOrWhiteSpace(checkedAt)) return null;
        object[] readings;
        readings = DiagnosticSessionRepository.ReadSavedProof(proofJson);
        return new FollowUpObservationDto
        {
            ObservationId = "legacy-" + session.Id.ToString("N"), CheckedAt = checkedAt,
            DeviceId = Answer("resolution_device_id"), Status = status,
            Summary = summary, Proof = readings, Source = source
        };
    }
}
