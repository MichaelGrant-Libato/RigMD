using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RigMD.QuotaChecks;

namespace RigMD.Tests.Api;

public class QuotaConcurrencyTesterTests
{
    private sealed class StagingHandler : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, int> _counts = new();
        private readonly TaskCompletionSource _batchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid _probe;
        private Guid _batch;
        private int _batchStarted;
        private int _inFlight;
        public string Fault { get; init; } = "";
        public int MaxInFlight { get; private set; }
        public List<(Uri Uri, string Method, string? Key, bool Bearer)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var user = body.RootElement.GetProperty("p_user_id").GetGuid();
            int count;
            bool wait;
            bool batchUser;
            bool otherUser;
            lock (_sync)
            {
                Requests.Add((request.RequestUri!, request.Method.Method, request.Headers.GetValues("apikey").Single(), request.Headers.Authorization != null));
                if (_probe == Guid.Empty) _probe = user;
                else if (_batch == Guid.Empty && user != _probe) _batch = user;
                count = _counts.GetValueOrDefault(user) + 1;
                _counts[user] = count;
                batchUser = user == _batch;
                otherUser = user != _probe && !batchUser;
                wait = batchUser && count <= 13;
                if (wait)
                {
                    MaxInFlight = Math.Max(MaxInFlight, ++_inFlight);
                    if (++_batchStarted == 13) _batchReady.TrySetResult();
                }
            }
            try
            {
                if (wait) await _batchReady.Task.WaitAsync(cancellationToken);
                var status = Fault switch { "http401" => 401, "http503" => 503, "redirect" => 302, _ => 200 };
                var allowed = Fault == "allow-all" || (count <= 12 && !(Fault == "deny-other" && otherUser));
                var content = Fault switch
                {
                    "bad-json" => "secret-test-content: not json",
                    "empty" => "[]",
                    "bad-boolean" => "[{\"allowed\":\"true\",\"retry_after_seconds\":0}]",
                    "bad-retry" => "[{\"allowed\":true,\"retry_after_seconds\":\"secret-test-content\"}]",
                    "nonzero-retry" => "[{\"allowed\":true,\"retry_after_seconds\":10}]",
                    "huge" => new string('x', 5000),
                    _ => JsonSerializer.Serialize(new[] { new { allowed, retry_after_seconds = allowed ? 0 : 45 } })
                };
                var response = new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                };
                if (Fault != "no-date") response.Headers.Date = DateTimeOffset.Parse(
                    Fault == "minute-crossed" && batchUser ? "2026-10-16T10:01:10Z" : "2026-10-16T10:00:10Z");
                return response;
            }
            finally
            {
                if (wait) lock (_sync) _inFlight--;
            }
        }
    }

    private static HttpClient Client(StagingHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://must-not-be-used.example") };
        client.DefaultRequestHeaders.Add("apikey", "sb_secret_fake_test_key_only");
        return client;
    }

    [Fact]
    public async Task TestStartsThirteenOverlappingCalls_UsesOnlyPinnedStaging_AndChecksAnotherAccount()
    {
        var handler = new StagingHandler();
        using var client = Client(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new QuotaConcurrencyTester(client).RunAsync(timeout.Token);
        Assert.Equal(new QuotaConcurrencyResult(13, 12, 1), result);
        Assert.Equal(13, handler.MaxInFlight);
        Assert.Equal(16, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("https", request.Uri.Scheme);
            Assert.Equal("qwytztbzvjgmqybdmrch.supabase.co", request.Uri.Host);
            Assert.Equal("/rest/v1/rpc/consume_rigmd_ai_quota", request.Uri.AbsolutePath);
            Assert.Empty(request.Uri.Query);
            Assert.Equal("POST", request.Method);
            Assert.Equal("sb_secret_fake_test_key_only", request.Key);
            Assert.False(request.Bearer);
        });
    }

    [Theory]
    [InlineData("allow-all", "expected 12 allowed")]
    [InlineData("deny-other", "separate dummy account")]
    [InlineData("minute-crossed", "Inconclusive")]
    public async Task IncorrectOrAmbiguousConcurrencyResults_CannotPass(string fault, string expected)
    {
        var handler = new StagingHandler { Fault = fault };
        using var client = Client(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<QuotaCheckException>(() => new QuotaConcurrencyTester(client).RunAsync(timeout.Token));
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("http401")]
    [InlineData("http503")]
    [InlineData("redirect")]
    [InlineData("bad-json")]
    [InlineData("empty")]
    [InlineData("bad-boolean")]
    [InlineData("bad-retry")]
    [InlineData("nonzero-retry")]
    [InlineData("huge")]
    [InlineData("no-date")]
    public async Task InvalidPreflight_FailsClosedWithoutDisclosingResponseContent_OrRetrying(string fault)
    {
        var handler = new StagingHandler { Fault = fault };
        using var client = Client(handler);
        var error = await Assert.ThrowsAsync<QuotaCheckException>(() => new QuotaConcurrencyTester(client).RunAsync());
        Assert.DoesNotContain("secret-test-content", error.Message);
        Assert.DoesNotContain("sb_secret", error.Message);
        Assert.Single(handler.Requests);
    }
}
