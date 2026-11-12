using System.Text.Json;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

internal sealed record TempCleanupSpecification(bool IncludeWindowsTemp, int MinAgeMinutes)
{
    internal static TempCleanupSpecification Parse(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined) return new(false, 0);
        if (arguments.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Cleanup arguments must be an object.");
        var include = false; var minutes = 0; var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new InvalidOperationException("Duplicate cleanup arguments are not allowed.");
            if (property.Name == "includeWindowsTemp" && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) include = property.Value.GetBoolean();
            else if (property.Name == "minAgeMinutes" && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value) && value is >= 0 and <= 525600) minutes = value;
            else throw new InvalidOperationException("Supported arguments: includeWindowsTemp (boolean) and minAgeMinutes (whole minutes from 0 to 525600). Nulls, unknown arguments and other types are rejected.");
        }
        return new(include, minutes);
    }
}
