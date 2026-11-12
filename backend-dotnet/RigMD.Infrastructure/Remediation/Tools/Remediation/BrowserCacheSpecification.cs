using System.Text.Json;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

internal sealed record BrowserCacheSpecification(string Key)
{
    private static readonly string[] Supported = ["chrome", "edge", "firefox"];
    internal string[] Browsers => Key.Split(',');

    internal static BrowserCacheSpecification Parse(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined) return new(string.Join(',', Supported));
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Browser-cache arguments must be an object.");
        HashSet<string>? selected = null;
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name != "browsers" || selected != null || property.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Only one browsers array is supported. Unknown, duplicate or incorrectly typed arguments are rejected.");
            selected = new(StringComparer.OrdinalIgnoreCase);
            foreach (var value in property.Value.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } browser ||
                    !Supported.Contains(browser, StringComparer.OrdinalIgnoreCase) || !selected.Add(browser))
                    throw new InvalidOperationException("Choose each browser at most once using its exact identifier: chrome, edge or firefox.");
            }
            if (selected.Count == 0) throw new InvalidOperationException("Choose at least one browser.");
        }
        return new(string.Join(',', selected == null ? Supported : Supported.Where(selected.Contains)));
    }
}
