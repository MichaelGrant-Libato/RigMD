using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Domain.Rules;
using RigMD.Infrastructure.Ai;

namespace RigMD.Tests.Infrastructure;

public class CloudAiSessionTests
{
    private const string UserId = "89520f7c-2c44-4fef-9baa-e70f7cfa2de9";
    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Gemini:ProxyUrl"] = "https://www.rig-md.com/api/gemini", ["Gemini:ApiKey"] = "fake-legacy-key-must-not-be-used" }).Build();
    private static object Tokens(string token = "fake.access.signature", string id = UserId, string? confirmed = "2026-10-01T00:00:00Z") => new
    {
        access_token = token, refresh_token = "fake-refresh-token", expires_in = 300,
        user = new { id, email = "user@example.com", role = "authenticated", email_confirmed_at = confirmed, is_anonymous = false }
    };
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Override { get; set; }
        public List<(string Url, string? Bearer, string Body)> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.Parameter,
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (Override != null) return Override(request);
            return request.RequestUri.AbsolutePath switch
            {
                "/api/cloud-ai-config" => Json(new { supabaseUrl = "https://test-project.supabase.co", publishableKey = "sb_publishable_fake" }),
                "/auth/v1/token" => Json(Tokens()),
                _ => Json(new { })
            };
        }
    }

    [Fact]
    public async Task SignedOutSession_MakesNoNetworkRequests()
    {
        var handler = new Handler();
        var session = new CloudAiSession(new HttpClient(handler), Config, new Clock());
        Assert.Null(await session.GetAccessTokenAsync());
        Assert.False(session.Status.SignedIn);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task SignIn_UsesVerifiedAccount_AndNeverReturnsTokensInStatus()
    {
        var handler = new Handler();
        var session = new CloudAiSession(new HttpClient(handler), Config, new Clock());
        Assert.True((await session.SignInAsync("user@example.com", "fake-password")).SignedIn);
        Assert.Equal("fake.access.signature", await session.GetAccessTokenAsync());
        Assert.Equal(2, handler.Calls.Count);
        Assert.DoesNotContain("fake-password", handler.Calls[1].Url);
        Assert.Contains("fake-password", handler.Calls[1].Body);
        Assert.DoesNotContain("fake.access", JsonSerializer.Serialize(session.Status));
        Assert.DoesNotContain("fake-refresh", JsonSerializer.Serialize(session.Status));
        Assert.False(new CloudAiSession(new HttpClient(handler), Config, new Clock()).Status.SignedIn);
    }

    [Fact]
    public async Task Refresh_IsSerialized_AndRotatesBeforeExpiry()
    {
        var handler = new Handler();
        var clock = new Clock();
        var session = new CloudAiSession(new HttpClient(handler), Config, clock);
        await session.SignInAsync("user@example.com", "fake-password");
        clock.Now = clock.Now.AddSeconds(250);
        handler.Override = _ => Json(Tokens("rotated.access.signature"));
        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.GetAccessTokenAsync()));
        Assert.All(tokens, token => Assert.Equal("rotated.access.signature", token));
        Assert.Equal(3, handler.Calls.Count);
        Assert.Contains("grant_type=refresh_token", handler.Calls.Last().Url);
        Assert.Contains("fake-refresh-token", handler.Calls.Last().Body);
    }

    [Fact]
    public async Task SignOut_DiscardsSession_AndRequestsOnlyLocalScopeRevocation()
    {
        var handler = new Handler();
        var session = new CloudAiSession(new HttpClient(handler), Config, new Clock());
        await session.SignInAsync("user@example.com", "fake-password");
        await session.SignOutAsync();
        Assert.False(session.Status.SignedIn);
        Assert.Null(await session.GetAccessTokenAsync());
        Assert.EndsWith("/auth/v1/logout?scope=local", handler.Calls.Last().Url);
        Assert.Equal("fake.access.signature", handler.Calls.Last().Bearer);
    }

    [Fact]
    public async Task RejectedOldToken_CannotSignOutANewAccountSession()
    {
        var handler = new Handler();
        var session = new CloudAiSession(new HttpClient(handler), Config, new Clock());
        await session.SignInAsync("user@example.com", "fake-password");
        await session.RejectAccessTokenAsync("another.old.token");
        Assert.True(session.Status.SignedIn);
        await session.RejectAccessTokenAsync("fake.access.signature");
        Assert.False(session.Status.SignedIn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredSession_WithRejectedRefreshOrDifferentUser_FailsClosed(bool differentUser)
    {
        var handler = new Handler();
        var clock = new Clock();
        var session = new CloudAiSession(new HttpClient(handler), Config, clock);
        await session.SignInAsync("user@example.com", "fake-password");
        clock.Now = clock.Now.AddMinutes(6);
        handler.Override = _ => differentUser ? Json(Tokens(id: Guid.NewGuid().ToString())) : Json(new { }, HttpStatusCode.BadRequest);
        Assert.Null(await session.GetAccessTokenAsync());
        Assert.False(session.Status.SignedIn);
    }

    [Fact]
    public async Task ExpiredSession_AuthOutage_ReturnsNoTokenButCanRetryLater()
    {
        var handler = new Handler();
        var clock = new Clock();
        var session = new CloudAiSession(new HttpClient(handler), Config, clock);
        await session.SignInAsync("user@example.com", "fake-password");
        clock.Now = clock.Now.AddMinutes(6);
        handler.Override = _ => Json(new { }, HttpStatusCode.ServiceUnavailable);
        Assert.Null(await session.GetAccessTokenAsync());
        Assert.True(session.Status.SignedIn);
        handler.Override = _ => Json(Tokens("new.access.signature"));
        Assert.Equal("new.access.signature", await session.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData("http://test-project.supabase.co", "sb_publishable_fake")]
    [InlineData("https://evil.example", "sb_publishable_fake")]
    [InlineData("https://test-project.supabase.co", "sb_secret_must_not_ship")]
    public async Task InvalidPublicConfiguration_NeverSendsAPassword(string url, string key)
    {
        var handler = new Handler { Override = _ => Json(new { supabaseUrl = url, publishableKey = key }) };
        var session = new CloudAiSession(new HttpClient(handler), Config, new Clock());
        await Assert.ThrowsAsync<CloudAiSignInException>(() => session.SignInAsync("user@example.com", "fake-password"));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task UnverifiedAccount_AndMalformedResponses_DoNotCreateASession()
    {
        foreach (var value in new object[] { Tokens(confirmed: null), new[] { "invalid" }, new { expires_in = "bad" } })
        {
            var handler = new Handler();
            handler.Override = req => req.RequestUri!.AbsolutePath == "/api/cloud-ai-config"
                ? Json(new { supabaseUrl = "https://test-project.supabase.co", publishableKey = "sb_publishable_fake" }) : Json(value);
            var session = new CloudAiSession(new HttpClient(handler), Config, new Clock());
            await Assert.ThrowsAsync<CloudAiSignInException>(() => session.SignInAsync("user@example.com", "fake-password"));
            Assert.False(session.Status.SignedIn);
        }
    }

    [Theory]
    [InlineData("https://evil.example/api/gemini")]
    [InlineData("http://www.rig-md.com/api/gemini")]
    [InlineData("https://www.rig-md.com:9999/api/gemini")]
    [InlineData("https://www.rig-md.com/api/gemini?target=evil")]
    public void AccountToken_CannotBeSentToAnUntrustedProxy(string url) => Assert.Null(RigMdAgentRuntimeSettingsStore.GetTrustedCloudProxyUri(url));

    [Fact]
    public void LegacyApexProxy_IsCanonicalizedBeforeSendingAnyCredentials() =>
        Assert.Equal("www.rig-md.com", RigMdAgentRuntimeSettingsStore.GetTrustedCloudProxyUri("https://rig-md.com/api/gemini")?.Host);

    [Fact]
    public async Task LegacyGoogleKey_WithoutASignedInAccount_CannotBypassCloudSignIn()
    {
        var handler = new Handler { Override = _ => throw new InvalidOperationException("Unexpected network call") };
        var client = new HttpClient(handler);
        var explainer = new GeminiAiExplainer(client, Config, new OfflineAiExplainer(), NullLogger<GeminiAiExplainer>.Instance);
        Assert.NotEmpty(await explainer.GenerateExplanationAsync(new DiagnosticResult { DiagnosedCategory = "Network connectivity issue" }, new DiagnosticSymptomPayload()));
        var react = new GeminiReActLlmClient(client, Config, NullLogger<GeminiReActLlmClient>.Instance);
        Assert.NotNull(await react.DecideNextTurnAsync(new ReActConversationContext { DiagnosedCategory = "Memory Pressure" },
            new[] { new AgentToolFunctionDeclaration { Name = "inspect_memory_and_processes", Description = "Read RAM" } }));
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(true, 401)]
    [InlineData(true, 429)]
    [InlineData(true, 504)]
    [InlineData(false, 401)]
    [InlineData(false, 429)]
    [InlineData(false, 503)]
    [InlineData(false, 504)]
    public async Task CloudCall_AttachesAccountToken_AndStopsRetryingOnAccessOrQuotaFailure(bool explanation, int status)
    {
        var handler = new Handler { Override = _ => Json(new { }, (HttpStatusCode)status) };
        var cloudSession = new FakeAccountSession();
        var client = new HttpClient(handler);
        if (explanation)
        {
            var explainer = new GeminiAiExplainer(client, Config, new OfflineAiExplainer(), NullLogger<GeminiAiExplainer>.Instance, cloudSession);
            Assert.NotEmpty(await explainer.GenerateExplanationAsync(new DiagnosticResult { DiagnosedCategory = "Network connectivity issue" }, new DiagnosticSymptomPayload()));
        }
        else
        {
            var react = new GeminiReActLlmClient(client, Config, NullLogger<GeminiReActLlmClient>.Instance, cloudSession);
            Assert.NotNull(await react.DecideNextTurnAsync(new ReActConversationContext { DiagnosedCategory = "Memory Pressure" },
                new[] { new AgentToolFunctionDeclaration { Name = "inspect_memory_and_processes", Description = "Read RAM" } }));
        }
        // Respect the installed preference; tests must not rewrite the user's settings file.
        var enabled = RigMdAgentRuntimeSettingsStore.GetTrustedCloudProxyUri(RigMdAgentRuntimeSettingsStore.ResolveEffectiveProxyUrl(Config["Gemini:ProxyUrl"])) != null;
        Assert.Equal(enabled ? 1 : 0, handler.Calls.Count);
        if (enabled)
        {
            Assert.Equal("fake.account.signature", handler.Calls[0].Bearer);
            Assert.StartsWith("https://www.rig-md.com/api/gemini?model=", handler.Calls[0].Url);
            Assert.DoesNotContain("fake-legacy-key", handler.Calls[0].Url);
            Assert.Equal(status == 401 ? 1 : 0, cloudSession.Rejections);
        }
    }

    private sealed class FakeAccountSession : ICloudAiSession
    {
        public int Rejections { get; private set; }
        public CloudAiAccountStatus Status => new(true, "user@example.com");
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("fake.account.signature");
        public Task RejectAccessTokenAsync(string token) { Rejections++; return Task.CompletedTask; }
        public Task<CloudAiAccountStatus> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
