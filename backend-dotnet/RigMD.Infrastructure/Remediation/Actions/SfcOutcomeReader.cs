using System.Text.RegularExpressions;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed record SfcOutcome(string Status, string Summary, string Meaning);

internal static class SfcOutcomeReader
{
    public static SfcOutcome Read(int exitCode, string? output)
    {
        var text = Regex.Replace((output ?? "").Replace("\0", ""), @"\s+", " ").ToLowerInvariant();
        if (exitCode != 0 || text.Contains("could not perform the requested operation") || text.Contains("could not start the repair service"))
            return new("Could not complete", "The Windows file check could not be confirmed as complete.", "Review the SFC output. No healthy or repaired result was inferred.");
        if (text.Contains("system repair pending"))
            return new("Restart required", "Windows reported a pending repair requiring a restart.", "Save work and follow Windows restart guidance before repeating the check.");
        if (text.Contains("unable to fix some of them"))
            return new("Unrepaired files reported", "Windows reported files it could not repair.", "Review the SFC log and Windows servicing guidance. The original problem is not verified as fixed.");
        var repaired = text.Contains("found corrupt files and successfully repaired them");
        var clear = text.Contains("did not find any integrity violations");
        if (repaired && !clear)
            return new("Repairs reported", "Windows reported repairing protected system files.", "This does not confirm that an app crash is fixed; verify the original symptom separately.");
        if (clear && !repaired)
            return new("No integrity violations reported", "Windows reported no integrity violations in protected system files.", "This does not establish the app-crash cause or overall hardware health.");
        return new("Unverified", "The scan ended, but its conclusion could not be verified.", "Output may be localized, incomplete or unrecognized. Review the SFC output; no healthy or repaired result was inferred.");
    }
}
