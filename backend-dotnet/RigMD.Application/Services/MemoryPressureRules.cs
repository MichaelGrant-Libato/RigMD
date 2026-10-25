namespace RigMD.Application.Services;

internal enum MemoryPressureState { Clear, Active, Unknown }

internal static class MemoryPressureRules
{
    public const double HighUsagePercent = 82;
    // A review policy, not a physical memory fault or paging diagnosis.
    public const double ResponsivenessUsagePercent = HighUsagePercent;

    public static bool IsValidUsage(double? usage) =>
        usage is >= 0 and <= 100 && double.IsFinite(usage.Value);

    public static MemoryPressureState Evaluate(
        double? usage, double? browserMb, string? warning, bool explicitMemoryScan,
        bool warningAvailable = true)
    {
        if (warningAvailable && !string.IsNullOrWhiteSpace(warning)) return MemoryPressureState.Active;
        var validUsage = IsValidUsage(usage);
        if (validUsage && usage >= HighUsagePercent)
            return MemoryPressureState.Active;

        if (!validUsage || !warningAvailable)
            return MemoryPressureState.Unknown;

        return MemoryPressureState.Clear;
    }
}
