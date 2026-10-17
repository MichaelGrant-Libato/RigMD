namespace RigMD.Application.Services;

internal static class CpuLoadThresholds
{
    // Preserve the existing diagnosis thresholds for each check's workload context.
    public const double Elevated = 80;
    public const double Responsiveness = 65;
    public const double Startup = 70;
    public const double SystemStabilityRisk = 85;
}
