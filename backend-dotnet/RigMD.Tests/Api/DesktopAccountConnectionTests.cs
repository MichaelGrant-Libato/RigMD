using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Autonomy;

namespace RigMD.Tests.Api;
public class DesktopAccountConnectionTests
{
    private sealed class Browser : IDesktopAccountBrowser { public Uri? Last; public void Open(Uri uri) => Last = uri; }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Account : ICloudAiSession
    {
        public int Connections;
        public CloudAiAccountStatus Status => new(Connections > 0, Connections > 0 ? "member@example.com" : null);
        public Task<CloudAiAccountStatus> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task RejectAccessTokenAsync(string token) => Task.CompletedTask;
        public Task<CloudAiAccountStatus> ConnectBrowserAccountAsync(string refresh, Guid id, CancellationToken cancellationToken = default)
        { Assert.Equal("desktop-refresh", refresh); Assert.NotEqual(Guid.Empty, id); Connections++; return Task.FromResult(Status); }
    }
    private static IConfiguration Config(string proxy = "https://www.rig-md.com/api/gemini") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ProxyUrl"] = proxy }).Build();
    private static string Encode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '=')); }
    private static (string State, string Payload) Packet(Browser browser, Clock clock, bool expired = false, bool tampered = false)
    {
        var values = browser.Last!.Fragment[1..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var state = values["state"];
        using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(Decode(values["publicKey"]), out _);
        var key = RandomNumberGenerator.GetBytes(32); var iv = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        var plain = JsonSerializer.SerializeToUtf8Bytes(new { state, refreshToken = "desktop-refresh", userId = Guid.NewGuid(),
            expiresAt = clock.Now.AddSeconds(expired ? -1 : 90).ToUnixTimeMilliseconds() });
        var cipher = new byte[plain.Length]; using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plain, cipher, tag, Encoding.UTF8.GetBytes(state)); if (tampered) cipher[0] ^= 1;
        return (state, JsonSerializer.Serialize(new { wrappedKey = Encode(rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256)),
            iv = Encode(iv), tag = Encode(tag), ciphertext = Encode(cipher) }));
    }
    [Fact]
    public async Task FreshRequest_UsesTrustedWebsiteFragment_AndCompletesOnlyOnce()
    {
        var account = new Account(); var browser = new Browser(); var clock = new Clock();
        using var flow = new DesktopAccountConnection(account, Config(), clock, browser);
        var status = await flow.StartAsync(default);
        Assert.Equal("waiting", status.State); Assert.Equal("https://www.rig-md.com/desktop-connect", browser.Last!.GetLeftPart(UriPartial.Path));
        Assert.Empty(browser.Last.Query); Assert.DoesNotContain("refresh", browser.Last.AbsoluteUri);
        var packet = Packet(browser, clock);
        var results = await Task.WhenAll(flow.CompleteAsync(packet.State, packet.Payload, default), flow.CompleteAsync(packet.State, packet.Payload, default));
        Assert.Single(results, x => x); Assert.Equal(1, account.Connections); Assert.Equal("connected", flow.Status.State);
    }
    [Fact]
    public async Task CancelAfterCompletion_ReportsConnectedInsteadOfPretendingToUndoSignIn()
    {
        var account = new Account(); var browser = new Browser(); var clock = new Clock();
        using var flow = new DesktopAccountConnection(account, Config(), clock, browser);
        await flow.StartAsync(default); var packet = Packet(browser, clock);
        Assert.True(await flow.CompleteAsync(packet.State, packet.Payload, default));
        await flow.CancelAsync(); Assert.Equal("connected", flow.Status.State); Assert.True(account.Status.SignedIn);
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("tampered")]
    [InlineData("canceled")]
    [InlineData("wrong-state")]
    [InlineData("replaced")]
    public async Task InvalidOrOldPackets_NeverConnect(string kind)
    {
        var account = new Account(); var browser = new Browser(); var clock = new Clock();
        using var flow = new DesktopAccountConnection(account, Config(), clock, browser);
        await flow.StartAsync(default); var packet = Packet(browser, clock, kind == "expired", kind == "tampered");
        if (kind == "canceled") await flow.CancelAsync();
        if (kind == "replaced") await flow.StartAsync(default);
        Assert.False(await flow.CompleteAsync(kind == "wrong-state" ? new string('Z', 43) : packet.State, packet.Payload, default));
        Assert.Equal(0, account.Connections);
    }
    [Fact]
    public async Task LocalDeadlineAndInvalidProxy_FailClosed()
    {
        var browser = new Browser(); var clock = new Clock(); var account = new Account();
        using var flow = new DesktopAccountConnection(account, Config(), clock, browser);
        await flow.StartAsync(default); var packet = Packet(browser, clock); clock.Now = clock.Now.AddMinutes(3);
        Assert.Equal("expired", flow.Status.State); Assert.False(await flow.CompleteAsync(packet.State, packet.Payload, default));
        using var wrong = new DesktopAccountConnection(account, Config("https://evil.test/api/gemini"), clock, browser);
        await Assert.ThrowsAsync<CloudAiSignInException>(() => wrong.StartAsync(default));
    }
    [Fact]
    public async Task MalformedEnvelopes_DoNotThrowOrConsumeAValidPendingRequest()
    {
        var browser = new Browser(); var clock = new Clock(); var account = new Account();
        using var flow = new DesktopAccountConnection(account, Config(), clock, browser);
        await flow.StartAsync(default); var packet = Packet(browser, clock);
        foreach (var json in new[] { "null", "[]", "{}", "bad-json", "{\"wrappedKey\":null}" })
            Assert.False(await flow.CompleteAsync(packet.State, json, default));
        Assert.True(await flow.CompleteAsync(packet.State, packet.Payload, default));
    }
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1:5273", "https://www.rig-md.com", true)]
    [InlineData("127.0.0.1", "127.0.0.1:5273", "https://rig-md.com", true)]
    [InlineData("10.0.0.2", "127.0.0.1:5273", "https://www.rig-md.com", false)]
    [InlineData("127.0.0.1", "evil.test:5273", "https://www.rig-md.com", false)]
    [InlineData("127.0.0.1", "127.0.0.1:5273", "https://www.rig-md.com.evil.test", false)]
    [InlineData("127.0.0.1", "127.0.0.1:5273", "null", false)]
    public void CallbackRequiresLoopbackExactHostAndWebsiteOrigin(string ip, string host, string origin, bool expected)
    {
        var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Host = new HostString(host); context.Request.Headers.Origin = origin;
        Assert.Equal(expected, DesktopAccountConnection.IsTrustedCallback(context));
    }
}
