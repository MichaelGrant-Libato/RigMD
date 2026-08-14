using System.Threading.Tasks;
using RigMD.Application.Models;
using RigMD.Domain.Rules;

namespace RigMD.Application.Services;

public interface IDiagnosticEngineService
{
    Task<DiagnosticReportDto> SubmitDiagnosisAsync(DiagnosticSymptomPayload payload);
}

public class DiagnosticReportDto
{
    public string diagnosed_category { get; set; } = string.Empty;
    public string action_category { get; set; } = string.Empty;
    public string confidence_label { get; set; } = string.Empty;
    public string ai_explanation { get; set; } = string.Empty;
    public string recommended_next_step { get; set; } = string.Empty;
    public object? proof { get; set; }
    public object? verification_target { get; set; }
    public string? session_id { get; set; }
}
