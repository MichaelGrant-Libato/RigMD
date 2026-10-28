using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigMD.Api.Middleware;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public class LocalApiRateLimitingTests
{
    [Fact]
    public async Task SettingsNavigationAndSignOut_DoNotConsumeTheScanAllowance()
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 12; i++)
        {
            using var settings = await host.Client.GetAsync("/api/autonomy/settings");
            using var tools = await host.Client.GetAsync("/api/autonomy/tools");
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
            Assert.Equal(HttpStatusCode.OK, tools.StatusCode);
        }
        for (var i = 0; i < 8; i++)
        {
            using var saved = await host.Client.PostAsync("/api/autonomy/settings", null);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }
        using var signOut = await host.Client.PostAsync("/api/cloud-ai/sign-out", null);
        using var scan = await host.Client.PostAsync("/api/diagnosis/local-scan", null);
        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
    }

    [Theory]
    [InlineData("/api/diagnosis/local-scan", "/api/autonomy/preview")]
    [InlineData("/api/autonomy/preview", "/api/diagnosis/local-scan")]
    [InlineData("/api/cloud-ai/sign-in", "/api/cloud-ai/sign-out")]
    [InlineData("/api/autonomy/settings", "/api/diagnosis/local-scan")]
    public async Task MutatingGroups_StayLimited_WithoutBlockingOtherWork(string limitedPath, string independentPath)
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 8; i++)
        {
            using var allowed = await host.Client.PostAsync(limitedPath, null);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        host.Client.DefaultRequestHeaders.Add("X-Client-ID", "spoofed-new-client");
        using var rejected = await host.Client.PostAsync(limitedPath, null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retry = rejected.Headers.RetryAfter?.Delta?.TotalSeconds;
        Assert.NotNull(retry);
        Assert.InRange(retry.Value, 1, 30);
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("local_rate_limit", error.RootElement.GetProperty("code").GetString());
        Assert.Equal(retry.Value, error.RootElement.GetProperty("retryAfterSeconds").GetDouble());
        using var independent = await host.Client.PostAsync(independentPath, null);
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        using var settings = await host.Client.GetAsync("/api/autonomy/settings");
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await host.Client.PostAsync(independentPath, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task ReadFloods_AreLimited_WithoutBlockingScanOrSignOut()
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 45; i++)
        {
            using var allowed = await host.Client.GetAsync("/api/autonomy/settings");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var rejected = await host.Client.GetAsync("/api/autonomy/tools");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var scan = await host.Client.PostAsync("/api/diagnosis/local-scan", null);
        using var signOut = await host.Client.PostAsync("/api/cloud-ai/sign-out", null);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
    }

    [Fact]
    public async Task ScanAliasesAndQueryChanges_ShareTheSameAllowance()
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 8; i++)
        {
            var path = i % 2 == 0 ? "/api/diagnosis/local-scan" : "/API/DIAGNOSIS/SUBMIT/";
            using var allowed = await host.Client.PostAsync($"{path}?client={i}", null);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var rejected = await host.Client.PostAsync("/api/diagnosis/analyze", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ExpensiveReadAndRemediationAlias_StillShareTheActionAllowance()
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 8; i++)
        {
            using var allowed = await host.Client.GetAsync("/api/autonomy/memory-apps");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var action = await host.Client.PostAsync("/api/autonomy/preview", null);
        using var alias = await host.Client.PostAsync("/api/remediation/open-target", null);
        using var scan = await host.Client.PostAsync("/api/diagnosis/local-scan", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, action.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, alias.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task OpeningSavedChecksAndSavingFeedback_DoNotSpendActionPermits(string method)
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 12; i++)
        {
            var id = Guid.NewGuid();
            var path = $"/api/autonomy/{id}/remediation-eligibility";
            if (i % 2 == 0) path = path.ToUpperInvariant() + "/";
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var result = await host.Client.SendAsync(request);
            using var feedback = await host.Client.GetAsync($"/api/diagnosis/sessions/{id}/feedback");
            using var saved = await host.Client.PostAsync($"/api/diagnosis/sessions/{id}/feedback", null);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(HttpStatusCode.OK, feedback.StatusCode);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }
        for (var i = 0; i < 8; i++)
        {
            using var preview = await host.Client.PostAsync("/api/autonomy/preview", null);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        }
        using var rejected = await host.Client.PostAsync("/api/autonomy/preview", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task EligibilityReadsRemainAvailableAfterActionLimitWithoutExemptingOtherMethodsOrInvalidIds()
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 8; i++)
        {
            using var preview = await host.Client.PostAsync("/api/autonomy/preview", null);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        }
        var path = $"/api/autonomy/{Guid.NewGuid()}/remediation-eligibility";
        using var read = await host.Client.GetAsync(path);
        using var write = await host.Client.PostAsync(path, null);
        using var invalid = await host.Client.GetAsync("/api/autonomy/not-a-guid/remediation-eligibility");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, write.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, invalid.StatusCode);
    }

    [Fact]
    public async Task EligibilityReadFloodsStayLimitedAcrossSessionIdsAndRequireAuthentication()
    {
        await using var host = await TestHost.StartAsync();
        for (var i = 0; i < 45; i++)
        {
            using var allowed = await host.Client.GetAsync($"/api/autonomy/{Guid.NewGuid()}/remediation-eligibility");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        var path = $"/API/AUTONOMY/{Guid.NewGuid()}/REMEDIATION-ELIGIBILITY/";
        using var rejected = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 30);
        using var preview = await host.Client.PostAsync("/api/autonomy/preview", null);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private sealed class TestHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<TestHost> StartAsync()
        {
            const string token = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddSingleton(new LocalApiSession(token));
            builder.Services.AddLocalApiRateLimiting();
            var app = builder.Build();
            app.UseRouting();
            app.UseMiddleware<LocalApiAuthenticationMiddleware>();
            app.UseRateLimiter();
            // Sentinels exercise the real authentication/limiter, never Windows providers or cloud AI.
            app.MapMethods("/api/autonomy/{sessionId}/remediation-eligibility", new[] { "GET", "HEAD", "POST" }, () => Results.Ok());
            app.MapMethods("/api/diagnosis/sessions/{sessionId}/feedback", new[] { "GET", "POST" }, () => Results.Ok());
            foreach (var path in new[] { "/api/autonomy/settings", "/api/autonomy/tools", "/api/autonomy/memory-apps" })
                app.MapGet(path, () => Results.Ok());
            foreach (var path in new[] { "/api/autonomy/settings", "/api/autonomy/preview", "/api/cloud-ai/sign-in",
                "/api/cloud-ai/sign-out", "/api/diagnosis/local-scan", "/api/diagnosis/submit", "/api/diagnosis/analyze",
                "/api/remediation/open-target" })
                app.MapPost(path, () => Results.Ok());
            try
            {
                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return new TestHost(app, client);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await app.StopAsync(timeout.Token); }
            finally { await app.DisposeAsync(); }
        }
    }
}
