using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace RigMD.QuotaChecks;

public sealed record QuotaConcurrencyResult(int Requests, int Allowed, int Denied);

public sealed class QuotaCheckException(string message) : Exception(message);

public sealed class QuotaConcurrencyTester(HttpClient client)
{
    public const string StagingProjectReference = "qwytztbzvjgmqybdmrch";
    public static Uri Endpoint => new($"https://{StagingProjectReference}.supabase.co/rest/v1/rpc/consume_rigmd_ai_quota");

    public async Task<QuotaConcurrencyResult> RunAsync(CancellationToken cancellationToken = default)
    {
        Reply? probe = null;
        var probeUser = Guid.NewGuid();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            probe = await CallAsync(probeUser, cancellationToken);
            if (!probe.Allowed) throw new QuotaCheckException("Staging quota is already exhausted. No test passed; do not reset production counters.");
            if (probe.ServerTime.Second <= 25) break;
            // Leave enough room for every result to belong to one fixed minute.
            await Task.Delay(TimeSpan.FromSeconds(62 - probe.ServerTime.Second), cancellationToken);
        }
        if (probe == null || probe.ServerTime.Second > 25)
            throw new QuotaCheckException("Could not obtain a clean staging time window. No test passed; retry later.");

        var user = Guid.NewGuid();
        var timer = Stopwatch.StartNew();
        // Start every request before awaiting any result; one HttpClient uses multiple connections.
        var pending = Enumerable.Range(0, 13).Select(_ => CallAsync(user, cancellationToken)).ToArray();
        var replies = await Task.WhenAll(pending);
        var followUps = await Task.WhenAll(CallAsync(user, cancellationToken), CallAsync(Guid.NewGuid(), cancellationToken));

        if (timer.Elapsed > TimeSpan.FromSeconds(25) ||
            replies.Concat(followUps).Any(reply => Minute(reply.ServerTime) != Minute(probe.ServerTime)))
            throw new QuotaCheckException("Inconclusive: the test was too slow or crossed a minute boundary. No pass recorded; retry later.");

        var allowed = replies.Count(reply => reply.Allowed);
        var denied = replies.Length - allowed;
        if (allowed != 12 || denied != 1 || replies.Any(reply => !reply.Allowed && reply.RetryAfter > 60))
            throw new QuotaCheckException($"FAIL: expected 12 allowed and 1 denied in one minute; observed {allowed} allowed and {denied} denied.");
        if (followUps[0].Allowed || followUps[0].RetryAfter > 60)
            throw new QuotaCheckException("FAIL: a follow-up for the exhausted dummy account was not correctly denied.");
        if (!followUps[1].Allowed)
            throw new QuotaCheckException("FAIL: a separate dummy account was denied. Check staging's existing global usage.");
        return new QuotaConcurrencyResult(replies.Length, allowed, denied);
    }

    private static DateTimeOffset Minute(DateTimeOffset time) =>
        new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, TimeSpan.Zero);

    private async Task<Reply> CallAsync(Guid user, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { p_user_id = user })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new QuotaCheckException($"Staging quota RPC returned HTTP {(int)response.StatusCode}. No test passed. Check the staging key, migration, and permissions.");
        if (response.Headers.Date is not { } date)
            throw new QuotaCheckException("Staging response has no server date; cannot safely verify a single quota window.");

        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int length;
        while ((length = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + length > 4096) throw new QuotaCheckException("Staging response exceeded the test's size limit.");
            buffer.Write(chunk, 0, length);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            var rows = document.RootElement;
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1)
                throw new QuotaCheckException("Staging returned an unexpected quota result shape.");
            var row = rows[0];
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("allowed", out var allowed) ||
                allowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !row.TryGetProperty("retry_after_seconds", out var retry) || retry.ValueKind != JsonValueKind.Number || !retry.TryGetInt32(out var seconds))
                throw new QuotaCheckException("Staging returned invalid quota fields.");
            var accepted = allowed.GetBoolean();
            if ((accepted && seconds != 0) || (!accepted && seconds is < 1 or > 86400))
                throw new QuotaCheckException("Staging returned an invalid retry delay.");
            return new Reply(accepted, seconds, date.ToUniversalTime());
        }
        catch (JsonException)
        {
            throw new QuotaCheckException("Staging did not return valid quota JSON. Raw response content is not displayed.");
        }
    }

    private sealed record Reply(bool Allowed, int RetryAfter, DateTimeOffset ServerTime);
}
