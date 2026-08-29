using RigMD.Application.Models;

namespace RigMD.Application.Services;

public interface IAutomaticDiagnosisService
{
    AutomaticDiagnosisResult Diagnose(
        AutomaticDiagnosisInput input);
}