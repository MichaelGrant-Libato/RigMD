namespace RigMD.Application.Contracts.Autonomy;

public sealed record CloudAiAccountStatus(bool SignedIn, string? Email);

public interface ICloudAiSession
{
    CloudAiAccountStatus Status { get; }
    Task<CloudAiAccountStatus> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task RejectAccessTokenAsync(string token);
}

public sealed class CloudAiSignInException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
