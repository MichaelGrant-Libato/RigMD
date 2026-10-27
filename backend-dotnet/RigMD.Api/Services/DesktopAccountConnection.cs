using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Services;
namespace RigMD.Api.Services;

public sealed record DesktopConnectionStatus(string State, string? Code = null, DateTimeOffset? ExpiresAt = null);
public sealed class DesktopAccountConnection(ICloudAiSession account, IConfiguration configuration, TimeProvider clock, IDesktopAccountBrowser browser) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RSA? _key;
    private string? _state;
    private DateTimeOffset _expiresAt;
    private string _status = "idle";
    public DesktopConnectionStatus Status => new(_status == "waiting" && clock.GetUtcNow() >= _expiresAt ? "expired" : _status,
        _state?[..8].ToUpperInvariant(), _state == null ? null : _expiresAt);

    public async Task<DesktopConnectionStatus> StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (account.Status.SignedIn) throw new CloudAiSignInException(409, "Disconnect this PC before connecting a different account.");
            var configured = configuration["Gemini:ProxyUrl"];
            var proxy = RigMdAgentRuntimeSettingsStore.GetTrustedCloudProxyUri(string.IsNullOrWhiteSpace(configured)
                ? RigMdAgentRuntimeSettingsStore.DefaultProxyUrl : configured);
            if (proxy == null) throw new CloudAiSignInException(503, "Website connection is not configured.");
            _key?.Dispose(); _key = RSA.Create(3072);
            _state = Encode(RandomNumberGenerator.GetBytes(32));
            _expiresAt = clock.GetUtcNow().AddMinutes(2); _status = "waiting";
            var uri = new Uri(proxy, "/desktop-connect#state=" + _state + "&publicKey=" + Encode(_key.ExportSubjectPublicKeyInfo()));
            try { browser.Open(uri); }
            catch (Exception) { Reset("error"); throw new CloudAiSignInException(503, "Could not open your browser. Try connecting again."); }
            return Status;
        }
        finally { _gate.Release(); }
    }
    public async Task CancelAsync()
    {
        await _gate.WaitAsync();
        try { Reset(account.Status.SignedIn ? "connected" : "canceled"); }
        finally { _gate.Release(); }
    }
    public static bool IsTrustedCallback(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address) &&
        context.Request.Host.Value == "127.0.0.1:5273" && context.Request.Headers.Origin.Count == 1 &&
        context.Request.Headers.Origin[0] is "https://www.rig-md.com" or "https://rig-md.com";

    public async Task<bool> CompleteAsync(string state, string payload, CancellationToken cancellationToken)
    {
        if (state.Length != 43 || payload.Length > 20000) return false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_key == null || _status != "waiting" || clock.GetUtcNow() >= _expiresAt || _state == null ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(_state), Encoding.ASCII.GetBytes(state))) return false;
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var wrapped = Decode(root.GetProperty("wrappedKey").GetString()!, 384);
            var iv = Decode(root.GetProperty("iv").GetString()!, 12);
            var tag = Decode(root.GetProperty("tag").GetString()!, 16);
            var cipher = Decode(root.GetProperty("ciphertext").GetString()!, null);
            if (cipher.Length > 12000) return false;
            var key = _key.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
            var plain = new byte[cipher.Length];
            try
            {
                if (key.Length != 32) return false;
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(iv, cipher, tag, plain, Encoding.UTF8.GetBytes(state));
                using var packet = JsonDocument.Parse(plain);
                var value = packet.RootElement;
                var expires = DateTimeOffset.FromUnixTimeMilliseconds(value.GetProperty("expiresAt").GetInt64());
                if (value.GetProperty("state").GetString() != state || expires <= clock.GetUtcNow() || expires > clock.GetUtcNow().AddMinutes(3) ||
                    !Guid.TryParse(value.GetProperty("userId").GetString(), out var userId)) return false;
                var refresh = value.GetProperty("refreshToken").GetString();
                if (string.IsNullOrEmpty(refresh) || refresh.Length > 8192) return false;
                // Consume before network I/O: duplicate callbacks cannot establish another session.
                Reset("connecting");
                await account.ConnectBrowserAccountAsync(refresh, userId, cancellationToken);
                _status = "connected";
                return true;
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentOutOfRangeException or CloudAiSignInException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        { if (_status == "connecting") Reset("error"); return false; }
        finally { _gate.Release(); }
    }
    private void Reset(string status) { _key?.Dispose(); _key = null; _state = null; _status = status; }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string text, int? expected)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 17000 || !Regex.IsMatch(text, @"\A[A-Za-z0-9_-]+\z")) throw new FormatException();
        var normalized = text.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
        if (expected != null && bytes.Length != expected) throw new FormatException();
        return bytes;
    }
    public void Dispose() { _key?.Dispose(); _gate.Dispose(); }
}
