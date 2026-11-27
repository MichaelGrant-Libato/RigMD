using System.Threading;
using System.Threading.Tasks;
using RigMD.Domain.Rules;

namespace RigMD.Application.Contracts.Ai;

public sealed record DiagnosticExplanation(string Text, string Source, string? Model = null, string? Status = null);

public interface IAiExplainer
{
    Task<string> GenerateExplanationAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload);
    async Task<DiagnosticExplanation> GenerateExplanationWithSourceAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload)
        => new(await GenerateExplanationAsync(result, symptomPayload), "unknown");
    Task<DiagnosticExplanation> GenerateExplanationWithSourceAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload, CancellationToken token)
        => GenerateExplanationWithSourceAsync(result, symptomPayload).WaitAsync(token);
}
