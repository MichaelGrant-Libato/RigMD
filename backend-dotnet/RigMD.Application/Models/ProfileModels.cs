namespace RigMD.Application.Models;

public class SaveProfileRequestDto
{
    public string cpu_model { get; set; } = string.Empty;
    public string ram_capacity { get; set; } = string.Empty;
    public string storage_type { get; set; } = string.Empty;
    public string storage_capacity { get; set; } = string.Empty;
    public object? storage_details { get; set; }
    public string os_version { get; set; } = string.Empty;
    public string? gpu_driver { get; set; }
    public string? chipset_driver { get; set; }
    public string? system_age { get; set; }
}

public class ProfileResponseDto
{
    public Guid id { get; set; }
    public string cpu_model { get; set; } = string.Empty;
    public string ram_capacity { get; set; } = string.Empty;
    public string storage_type { get; set; } = string.Empty;
    public string storage_capacity { get; set; } = string.Empty;
    public object? storage_details { get; set; }
    public string os_version { get; set; } = string.Empty;
    public string? gpu_driver { get; set; }
    public string? chipset_driver { get; set; }
    public string? system_age { get; set; }
    public DateTime created_at { get; set; }
}