using System.Security.Cryptography;
using System.Text.Json;

namespace RigMD.Api.Services;

public sealed class RemediationApprovalStore(TimeProvider clock)
{
    public sealed record Scope(string Operation, Guid SessionId, string ToolName, string Arguments);
    public sealed record Approval(string Token, DateTimeOffset ExpiresAt);
    private sealed record Entry(Scope Scope, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static Scope CreateScope(string operation, Guid sessionId, string toolName, string? argumentsJson)
    {
        var json = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        if (json.Length > 65536) throw new JsonException("Arguments are too large.");
        using var arguments = JsonDocument.Parse(json);
        if (arguments.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Tool arguments must be an object.");
        return new Scope(operation, sessionId, toolName,
            JsonSerializer.Serialize(arguments.RootElement));
    }

    public Approval Issue(Scope scope)
    {
        lock (_sync)
        {
            var now = clock.GetUtcNow();
            foreach (var key in _entries.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
                _entries.Remove(key);
            // Bound memory even if authenticated callers repeatedly request previews.
            if (_entries.Count >= 256) _entries.Remove(_entries.MinBy(entry => entry.Value.ExpiresAt).Key);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = now.Add(Lifetime);
            _entries.Add(token, new Entry(scope, expiresAt));
            return new Approval(token, expiresAt);
        }
    }

    public bool TryConsume(string? token, Scope scope)
    {
        if (token?.Length != 64) return false;
        lock (_sync)
        {
            // Removal is atomic and precedes execution, including failed/changed requests.
            return _entries.Remove(token, out var entry) && entry.ExpiresAt > clock.GetUtcNow() && entry.Scope == scope;
        }
    }
}
