using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace RigMD.Agent.Services;

public sealed class InteractiveUserTempPathResolver
{
    private readonly ILogger<InteractiveUserTempPathResolver> _logger;

    public InteractiveUserTempPathResolver(
        ILogger<InteractiveUserTempPathResolver> logger)
    {
        _logger = logger;
    }

    public InteractiveUserTempPath Resolve()
    {
        var accountName =
            GetInteractiveUserName();

        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException(
                "No interactive Windows user could be identified.");
        }

        var profilePath =
            ResolveProfilePath(
                accountName);

        var fullProfilePath =
            Path.GetFullPath(
                    profilePath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(
                fullProfilePath))
        {
            throw new DirectoryNotFoundException(
                $"Interactive user profile does not exist: {fullProfilePath}");
        }

        var tempPath =
            Path.GetFullPath(
                Path.Combine(
                    fullProfilePath,
                    "AppData",
                    "Local",
                    "Temp"));

        EnsurePathIsInsideProfile(
            fullProfilePath,
            tempPath);

        EnsureExpectedTempPath(
            fullProfilePath,
            tempPath);

        if (!Directory.Exists(
                tempPath))
        {
            throw new DirectoryNotFoundException(
                $"Interactive user's TEMP directory does not exist: {tempPath}");
        }

        _logger.LogInformation(
            "Resolved interactive user TEMP directory. Account={AccountName}, ProfilePath={ProfilePath}, TempPath={TempPath}",
            accountName,
            fullProfilePath,
            tempPath);

        return new InteractiveUserTempPath
        {
            AccountName =
                accountName,

            ProfilePath =
                fullProfilePath,

            TempPath =
                tempPath
        };
    }

    private static string?
        GetInteractiveUserName()
    {
        using var searcher =
            new ManagementObjectSearcher(
                "SELECT UserName FROM Win32_ComputerSystem");

        using var results =
            searcher.Get();

        foreach (ManagementObject computer in results)
        {
            using (computer)
            {
                var value =
                    computer["UserName"]?
                        .ToString();

                if (!string.IsNullOrWhiteSpace(
                        value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static string
        ResolveProfilePath(
            string accountName)
    {
        SecurityIdentifier sid;

        try
        {
            var account =
                new NTAccount(
                    accountName);

            sid =
                (SecurityIdentifier)
                account.Translate(
                    typeof(
                        SecurityIdentifier));
        }
        catch (IdentityNotMappedException ex)
        {
            throw new InvalidOperationException(
                $"Could not resolve the Windows SID for interactive user '{accountName}'.",
                ex);
        }

        const string profileListPath =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

        using var profileKey =
            Registry.LocalMachine.OpenSubKey(
                $@"{profileListPath}\{sid.Value}");

        if (profileKey == null)
        {
            throw new InvalidOperationException(
                $"Windows profile information was not found for SID '{sid.Value}'.");
        }

        var profileImagePath =
            profileKey.GetValue(
                    "ProfileImagePath")?
                .ToString();

        if (string.IsNullOrWhiteSpace(
                profileImagePath))
        {
            throw new InvalidOperationException(
                $"Windows profile path was not found for SID '{sid.Value}'.");
        }

        return Environment
            .ExpandEnvironmentVariables(
                profileImagePath);
    }

    private static void
        EnsurePathIsInsideProfile(
            string profilePath,
            string candidatePath)
    {
        var normalizedProfilePath =
            Path.GetFullPath(
                    profilePath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var normalizedCandidatePath =
            Path.GetFullPath(
                candidatePath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var profilePrefix =
            normalizedProfilePath +
            Path.DirectorySeparatorChar;

        if (!normalizedCandidatePath.StartsWith(
                profilePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Resolved TEMP directory is outside the interactive user's profile.");
        }
    }

    private static void
        EnsureExpectedTempPath(
            string profilePath,
            string candidatePath)
    {
        var expectedTempPath =
            Path.GetFullPath(
                    Path.Combine(
                        profilePath,
                        "AppData",
                        "Local",
                        "Temp"))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var normalizedCandidatePath =
            Path.GetFullPath(
                    candidatePath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        if (!string.Equals(
                expectedTempPath,
                normalizedCandidatePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Resolved TEMP directory does not match the expected interactive user TEMP location.");
        }
    }
}

public sealed class InteractiveUserTempPath
{
    public string AccountName { get; init; } =
        string.Empty;

    public string ProfilePath { get; init; } =
        string.Empty;

    public string TempPath { get; init; } =
        string.Empty;
}