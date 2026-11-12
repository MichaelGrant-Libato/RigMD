using System.Net;
using System.Net.Http;
using RigMD.Desktop;

namespace RigMD.Tests.Desktop;

public sealed class DesktopApiReadinessTests
{
    private static readonly Uri Origin = new("http://localhost:5273");
    private static HttpResponseMessage Html(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Css() => new(HttpStatusCode.OK) { Content = new StringContent("body{}", System.Text.Encoding.UTF8, "text/css") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static Task Wait(HttpClient client, Func<bool>? exited = null, CancellationToken token = default) =>
        DesktopApiReadiness.WaitAsync(client, Origin, exited ?? (() => false), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1), token);

    [Fact]
    public async Task ReadyRequiresBothHtmlAndItsStylesheet()
    {
        var paths = new List<string>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(paths.Count == 1 ? Html("<link href=\"/assets/app.css\">") : Css());
        }));
        await Wait(client);
        Assert.Equal(new[] { "/", "/assets/app.css" }, paths);
    }

    [Fact]
    public async Task RefusedConnectionIsRetriedUntilAssetsAreReady()
    {
        var attempts = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (++attempts == 1) throw new HttpRequestException("Not listening yet");
            return Task.FromResult(request.RequestUri!.AbsolutePath == "/" ? Html("<link href=\"/app.css\">") : Css());
        }));
        await Wait(client);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData("<p>Assets not ready</p>")]
    [InlineData("<link href=\"http://example.com/app.css\">")]
    [InlineData("<link href=\"http://localhost:5000/app.css\">")]
    public async Task MissingOrExternalStylesheetCannotMakeStartupReady(string html)
    {
        var attempts = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(Origin.Authority, request.RequestUri!.Authority);
            attempts++;
            return Task.FromResult(attempts == 1 ? Html(html) : request.RequestUri.AbsolutePath == "/" ? Html("<link href=\"/app.css\">") : Css());
        }));
        await Wait(client);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedStylesheetOrFallbackHtmlIsNotReady(bool failed)
    {
        var cssAttempts = 0;
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/" ? Html("<link href=\"/app.css\">") :
            ++cssAttempts > 1 ? Css() : failed ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Html("fallback"))));
        await Wait(client);
        Assert.Equal(2, cssAttempts);
    }

    [Fact]
    public async Task ExitedProcessStopsWithoutSendingRequests()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected request")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Wait(client, () => true));
    }

    [Fact]
    public async Task StartupBudgetAlsoCancelsAnOutstandingRequest()
    {
        using var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Css(); }));
        await Assert.ThrowsAsync<TimeoutException>(() => DesktopApiReadiness.WaitAsync(client, Origin, () => false, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var client = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected request")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(client, token: cancellation.Token));
    }
}
