namespace RigMD.Application.Contracts.Common;

/// <summary>
/// Provides access to the current client identifier for data isolation.
/// </summary>
public interface ICurrentClientProvider
{
    string GetCurrentClientId();
}
