namespace RigMD.Application.Services;

internal static class CpuLoadThresholds
{
    // The same measurement has the same review threshold in every check.
    public const double Elevated = 80;
    public const double Responsiveness = Elevated;
    public const double Startup = Elevated;
    public const double SystemStabilityRisk = Elevated;
}
