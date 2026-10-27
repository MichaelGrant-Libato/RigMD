using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Services;

namespace RigMD.Infrastructure.Ai;

public sealed class CloudAiSession(HttpClient client, IConfiguration configuration, TimeProvider clock, ICloudAiCredentialStore? credentialStore = null) : ICloudAiSession
{
    private sealed record AuthConfig(Uri Project, string PublicKey);
    private sealed record Session(AuthConfig Config, Guid UserId, string AccessToken, string RefreshToken, string Email, DateTimeOffset ExpiresAt);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Session? _session;
    private SavedCloudAiAccount? _saved = credentialStore?.Read();
    private DateTimeOffset _retryAfter;
    private bool _remembered;
    public CloudAiAccountStatus Status => Volatile.Read(ref _session) is { } session
        ? new(true, session.Email, _remembered) : new(false, null, false, _saved != null);

    public async Task<CloudAiAccountStatus> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            using var response = await SendAsync(HttpMethod.Post, new Uri(config.Project, "/auth/v1/token?grant_type=password"),
                new { email = email.Trim(), password }, config.PublicKey, null, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new CloudAiSignInException(response.StatusCode == HttpStatusCode.TooManyRequests ? 429 :
                    (int)response.StatusCode >= 500 ? 503 : 401,
                    "Sign-in failed. Check your credentials and email verification, or try again later.");
            using var tokenJson = await ReadJsonAsync(response, cancellationToken);
            var session = ParseSession(config, tokenJson.RootElement);
            if (session == null) throw new CloudAiSignInException(401, "A verified RigMD email account is required.");
            Save(session);
            Volatile.Write(ref _session, session);
            return Status;
        }
        catch (CloudAiSignInException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw new CloudAiSignInException(503, "Cloud sign-in is unavailable. Offline checks still work.");
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            if (session == null)
            {
                if (_saved == null || clock.GetUtcNow() < _retryAfter) return null;
                _retryAfter = clock.GetUtcNow().AddSeconds(30);
                var config = await LoadConfigAsync(cancellationToken);
                if (config.Project.AbsoluteUri.TrimEnd('/') != _saved.Project) { ClearSaved(); return null; }
                return (await RefreshAsync(config, _saved.RefreshToken, _saved.UserId, cancellationToken))?.AccessToken;
            }
            if (session.ExpiresAt > clock.GetUtcNow().AddSeconds(60)) return session.AccessToken;
            return (await RefreshAsync(session.Config, session.RefreshToken, session.UserId, cancellationToken))?.AccessToken;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or CloudAiSignInException or IOException or UnauthorizedAccessException) { return null; }
        finally { _gate.Release(); }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            ClearSaved();
            Volatile.Write(ref _session, null);
            if (session == null) return;
            try
            {
                using var response = await SendAsync(HttpMethod.Post, new Uri(session.Config.Project, "/auth/v1/logout?scope=local"),
                    null, session.Config.PublicKey, session.AccessToken, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { /* Local credentials are already discarded. */ }
        }
        finally { _gate.Release(); }
    }

    public async Task RejectAccessTokenAsync(string token)
    {
        await _gate.WaitAsync();
        try { if (_session?.AccessToken == token) { ClearSaved(); Volatile.Write(ref _session, null); } }
        finally { _gate.Release(); }
    }

    public async Task<CloudAiAccountStatus> ConnectBrowserAccountAsync(string refreshToken, Guid expectedUser, CancellationToken cancellationToken = default)
    {
        if (refreshToken.Length is < 1 or > 8192 || expectedUser == Guid.Empty)
            throw new CloudAiSignInException(400, "Invalid website account connection.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (await RefreshAsync(config, refreshToken, expectedUser, cancellationToken) == null)
                throw new CloudAiSignInException(401, "The website connection expired. Connect this PC again.");
            return Status;
        }
        finally { _gate.Release(); }
    }

    private async Task<Session?> RefreshAsync(AuthConfig config, string refreshToken, Guid userId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, new Uri(config.Project, "/auth/v1/token?grant_type=refresh_token"),
            new { refresh_token = refreshToken }, config.PublicKey, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
            { ClearSaved(); Volatile.Write(ref _session, null); }
            return null;
        }
        using var json = await ReadJsonAsync(response, cancellationToken);
        var refreshed = ParseSession(config, json.RootElement);
        if (refreshed?.UserId != userId)
        { ClearSaved(); Volatile.Write(ref _session, null); return null; }
        Save(refreshed);
        Volatile.Write(ref _session, refreshed);
        return refreshed;
    }

    private void Save(Session session)
    {
        _saved = new(session.Config.Project.AbsoluteUri.TrimEnd('/'), session.UserId, session.RefreshToken);
        _remembered = false;
        if (credentialStore == null) return;
        try { credentialStore.Write(_saved); _remembered = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _saved = null; Volatile.Write(ref _session, null);
            throw new CloudAiSignInException(503, "Could not securely remember this connection. Local checks still work.");
        }
    }
    private void ClearSaved()
    {
        _saved = null;
        _remembered = false;
        Volatile.Write(ref _session, null);
        // Do not report a successful disconnect if the stored credential cannot be removed.
        credentialStore?.Clear();
    }
    private async Task<AuthConfig> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var configured = configuration["Gemini:ProxyUrl"];
        var proxy = RigMdAgentRuntimeSettingsStore.GetTrustedCloudProxyUri(string.IsNullOrWhiteSpace(configured)
            ? RigMdAgentRuntimeSettingsStore.DefaultProxyUrl : configured);
        if (proxy == null) throw new CloudAiSignInException(503, "Cloud sign-in is not configured.");
        using var metadata = await SendAsync(HttpMethod.Get, new Uri(proxy, "/api/cloud-ai-config"), null, null, null, cancellationToken);
        if (!metadata.IsSuccessStatusCode) throw new CloudAiSignInException(503, "The website is unavailable. Offline checks still work.");
        using var json = await ReadJsonAsync(metadata, cancellationToken);
        var project = Text(json.RootElement, "supabaseUrl");
        var key = Text(json.RootElement, "publishableKey");
        if (!Regex.IsMatch(project, @"^https://[a-z0-9-]+\.supabase\.co$") || !IsPublicKey(key))
            throw new CloudAiSignInException(503, "Cloud sign-in configuration is invalid.");
        return new(new Uri(project), key);
    }

    private Session? ParseSession(AuthConfig config, JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        var accessToken = Text(json, "access_token");
        var refreshToken = Text(json, "refresh_token");
        if (accessToken.Length > 8192 || !Regex.IsMatch(accessToken, @"^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$") ||
            refreshToken.Length is < 1 or > 8192 || !json.TryGetProperty("expires_in", out var expiry) ||
            expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt32(out var seconds) || seconds is < 1 or > 86400 ||
            !json.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object) return null;
        var email = Text(user, "email");
        if (!Guid.TryParse(Text(user, "id"), out var userId) || Text(user, "role") != "authenticated" ||
            (user.TryGetProperty("is_anonymous", out var anonymous) && anonymous.ValueKind == JsonValueKind.True) ||
            email.Length == 0 || !DateTimeOffset.TryParse(Text(user, "email_confirmed_at"), out _)) return null;
        return new(config, userId, accessToken, refreshToken, email, clock.GetUtcNow().AddSeconds(seconds));
    }

    private static string Text(JsonElement json, string property) => json.ValueKind == JsonValueKind.Object &&
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool IsPublicKey(string key)
    {
        if (key.StartsWith("sb_secret_", StringComparison.Ordinal)) return false;
        if (key.StartsWith("sb_publishable_", StringComparison.Ordinal)) return true;
        try
        {
            var part = key.Split('.')[1].Replace('-', '+').Replace('_', '/');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(part.PadRight((part.Length + 3) / 4 * 4, '=')));
            return Text(doc.RootElement, "role") == "anon";
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException) { return false; }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, object? body,
        string? publicKey, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body != null) request.Content = JsonContent.Create(body);
        if (publicKey != null) request.Headers.Add("apikey", publicKey);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400) { response.Dispose(); throw new HttpRequestException("Authentication redirect rejected."); }
        return response;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (output.Length + count > 65536) throw new JsonException("Authentication response too large.");
            output.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(output.ToArray());
    }
}
