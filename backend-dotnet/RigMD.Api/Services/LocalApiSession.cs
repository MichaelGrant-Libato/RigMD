using System.Security.Cryptography;

namespace RigMD.Api.Services;

public sealed class LocalApiSession
{
    public const string EnvironmentVariable = "RIGMD_LOCAL_API_TOKEN";
    public const string ClientId = "local";
    private readonly byte[]? _token;

    public LocalApiSession(string? token)
    {
        _token = ParseToken(token);
    }

    public bool IsValid(string? suppliedToken)
    {
        var candidate = ParseToken(suppliedToken);
        return _token != null && candidate != null &&
            CryptographicOperations.FixedTimeEquals(_token, candidate);
    }

    private static byte[]? ParseToken(string? token)
    {
        if (token?.Length != 64) return null;
        try { return Convert.FromHexString(token); }
        catch (FormatException) { return null; }
    }
}
