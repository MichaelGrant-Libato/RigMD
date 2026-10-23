using System.Threading.Tasks;
using RigMD.Domain.Rules;

namespace RigMD.Application.Contracts.Ai;

public sealed record DiagnosticExplanation(string Text, string Source, string? Model = null);

public interface IAiExplainer
{
    Task<string> GenerateExplanationAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload);
    async Task<DiagnosticExplanation> GenerateExplanationWithSourceAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload)
        => new(await GenerateExplanationAsync(result, symptomPayload), "unknown");
}
