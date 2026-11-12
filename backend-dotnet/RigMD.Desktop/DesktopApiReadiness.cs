using System.Net.Http;
using System.Text.RegularExpressions;

namespace RigMD.Desktop;

internal static class DesktopApiReadiness
{
    internal static async Task WaitAsync(HttpClient client, Uri origin, Func<bool> processExited,
        TimeSpan? timeout = null, TimeSpan? retryDelay = null, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        try
        {
            while (true)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (processExited())
                    throw new InvalidOperationException("The local API process exited before startup completed.");
                try
                {
                    using var response = await client.GetAsync(origin, budget.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var html = await response.Content.ReadAsStringAsync(budget.Token);
                        var css = Regex.Match(html, "<link[^>]+href=\"([^\"]+\\.css)\"", RegexOptions.IgnoreCase);
                        if (css.Success && Uri.TryCreate(origin, css.Groups[1].Value, out var cssUri) &&
                            cssUri.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority))
                        {
                            using var stylesheet = await client.GetAsync(cssUri, budget.Token);
                            if (stylesheet.IsSuccessStatusCode &&
                                string.Equals(stylesheet.Content.Headers.ContentType?.MediaType, "text/css", StringComparison.OrdinalIgnoreCase))
                                return;
                        }
                    }
                }
                catch (HttpRequestException) { /* The listener is still starting. */ }
                catch (TaskCanceledException) when (!budget.IsCancellationRequested) { /* Per-request timeout; retry within the startup budget. */ }
                await Task.Delay(retryDelay ?? TimeSpan.FromMilliseconds(100), budget.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The local API and frontend assets did not become ready within the startup timeout.");
        }
    }
}
