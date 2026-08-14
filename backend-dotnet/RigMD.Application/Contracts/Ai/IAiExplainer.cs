using System.Threading.Tasks;
using RigMD.Domain.Rules;

namespace RigMD.Application.Contracts.Ai;

public interface IAiExplainer
{
    Task<string> GenerateExplanationAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload);
}
