namespace RigMD.Application.Models;

public class WarningSummaryDto
{
    public int observed_warning_signs { get; set; }

    public int total_observed_occurrences { get; set; }

    public int total_reference_items { get; set; }
}

public class WarningSignRowDto
{
    public string id { get; set; } = string.Empty;

    public string warning_sign { get; set; } = string.Empty;

    public string meaning { get; set; } = string.Empty;

    public string threshold { get; set; } = string.Empty;

    public string action { get; set; } = string.Empty;

    public string category { get; set; } = string.Empty;

    public string[] keywords { get; set; } =
        Array.Empty<string>();

    public bool observed { get; set; }

    public int observed_count { get; set; }
}

public class WarningSignsResponseDto
{
    public WarningSummaryDto summary { get; set; } =
        new();

    public string[] categories { get; set; } =
        Array.Empty<string>();

    public List<WarningSignRowDto> warning_signs { get; set; } =
        new();

    public string? database_warning { get; set; }
}