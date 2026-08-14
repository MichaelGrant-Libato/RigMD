using System;
using System.Linq;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Ai;
using RigMD.Application.Contracts.Providers;
using RigMD.Domain.Rules;

namespace RigMD.Application.Services;

public class DiagnosticEngineService : IDiagnosticEngineService
{
    private readonly IWindowsSystemProfileService _hardwareProvider;
    private readonly IAiExplainer _aiExplainer;

    public DiagnosticEngineService(IWindowsSystemProfileService hardwareProvider, IAiExplainer aiExplainer)
    {
        _hardwareProvider = hardwareProvider;
        _aiExplainer = aiExplainer;
    }

    public async Task<DiagnosticReportDto> SubmitDiagnosisAsync(DiagnosticSymptomPayload payload)
    {
        // 1. Get live system profile
        var hardwareProfile = _hardwareProvider.GetLiveSystemProfile();

        // 2. Map to metrics
        var metrics = new HardwareMetrics
        {
            CpuUsagePercent = hardwareProfile.Cpu.UsagePercent,
            RamUsagePercent = hardwareProfile.Ram.UsagePercent,
            DiskUsagePercent = hardwareProfile.StorageDrives.FirstOrDefault(d => d.UsagePercent.HasValue)?.UsagePercent ?? 0,
            StorageType = hardwareProfile.PrimaryStorageType,
            BrowserMemoryMb = hardwareProfile.ProcessInsights.BrowserMemoryMb,
            BrowserProcessCount = hardwareProfile.ProcessInsights.BrowserProcessCount,
            BrowserHeavy = hardwareProfile.ProcessInsights.BrowserHeavy,
            GameDetected = hardwareProfile.ProcessInsights.GameDetected,
            GameProcesses = hardwareProfile.ProcessInsights.GameProcesses
        };

        // 3. Run pure rule engine
        var result = DiagnosticEngine.RunDiagnostic(payload, metrics);

        // 4. Generate AI explanation
        var explanation = await _aiExplainer.GenerateExplanationAsync(result, payload);

        // 5. Generate a session ID (since persistence is not fully wired yet)
        var sessionId = Guid.NewGuid().ToString();

        // 6. Map to DTO
        return new DiagnosticReportDto
        {
            session_id = sessionId,
            diagnosed_category = result.DiagnosedCategory,
            action_category = result.ActionCategory,
            confidence_label = result.ConfidenceLabel,
            ai_explanation = explanation,
            recommended_next_step = result.RecommendedNextStep,
            
            // Map the proof to lowercase properties matching the frontend
            proof = result.Proof.Select(p => new {
                label = p.Label,
                value = p.Value,
                status = p.Status,
                meaning = p.Meaning
            }).ToList(),
            
            verification_target = new {
                target = result.Target.Target,
                label = result.Target.Label,
                description = result.Target.Description
            }
        };
    }
}
