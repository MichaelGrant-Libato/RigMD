namespace RigMD.Application.Contracts.Autonomy;

public sealed record CloudAiAccountStatus(bool SignedIn, string? Email, bool Remembered = false, bool Reconnecting = false);

public interface ICloudAiSession
{
    CloudAiAccountStatus Status { get; }
    Task<CloudAiAccountStatus> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<CloudAiAccountStatus> ConnectBrowserAccountAsync(string refreshToken, Guid expectedUser, CancellationToken cancellationToken = default)
        => throw new CloudAiSignInException(503, "Website account connection is unavailable.");
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task RejectAccessTokenAsync(string token);
}

public sealed class CloudAiSignInException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
