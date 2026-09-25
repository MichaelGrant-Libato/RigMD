using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Ai;
using RigMD.Domain.Rules;

namespace RigMD.Infrastructure.Ai;

/// <summary>
/// Free-tier Google Gemini diagnostic explainer (gemini-2.5-flash / gemini-2.0-flash)
/// with automatic zero-cost fallback to OfflineAiExplainer when offline or unconfigured.
/// </summary>
public class GeminiAiExplainer : IAiExplainer
{
    private static readonly string[] CandidateModels =
    [
        "gemini-2.5-flash",
        "gemini-2.0-flash"
    ];

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly OfflineAiExplainer _offlineFallback;
    private readonly ILogger<GeminiAiExplainer> _logger;

    public GeminiAiExplainer(
        HttpClient httpClient,
        IConfiguration configuration,
        OfflineAiExplainer offlineFallback,
        ILogger<GeminiAiExplainer> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _offlineFallback = offlineFallback;
        _logger = logger;
    }

    public async Task<string> GenerateExplanationAsync(
        DiagnosticResult result,
        DiagnosticSymptomPayload symptomPayload)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return await _offlineFallback.GenerateExplanationAsync(result, symptomPayload);
        }

        var prompt = BuildPrompt(result, symptomPayload);
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 260
            }
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);

        foreach (var model in CandidateModels)
        {
            try
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0 &&
                    candidates[0].TryGetProperty("content", out var contentElem) &&
                    contentElem.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textElem))
                {
                    var text = textElem.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "GeminiAiExplainer failed on model {Model}; trying next candidate or offline fallback.",
                    model);
            }
        }

        return await _offlineFallback.GenerateExplanationAsync(result, symptomPayload);
    }

    private string? ResolveApiKey()
    {
        var fromConfig = _configuration["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig) &&
            !fromConfig.Contains("USE_DOTNET_USER_SECRETS", StringComparison.OrdinalIgnoreCase))
        {
            return fromConfig.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        return null;
    }

    private static string BuildPrompt(
        DiagnosticResult result,
        DiagnosticSymptomPayload symptomPayload)
    {
        var evidenceText = result.Evidence is { Count: > 0 }
            ? string.Join(", ", result.Evidence.Select(e => $"{e.Label}: {e.Value}"))
            : "None";

        var proofSource = result.Proof is { Count: > 0 } ? result.Proof : result.AllLiveProof;
        var proofText = proofSource is { Count: > 0 }
            ? string.Join("; ", proofSource.Select(p => $"{p.Label}: {p.Value} ({p.Status})"))
            : "No abnormal telemetry readings detected.";

        return $@"You are RigMD's Windows hardware and OS diagnostic explanation engine.
Explain the following diagnostic result to a user in clear, grounded English (2 to 3 concise sentences).

--- DIAGNOSTIC RESULT ---
Diagnosed Category: {result.DiagnosedCategory}
Confidence Level: {result.ConfidenceLabel}
Recommended Action Tier: {result.ActionCategory}
Symptom / Scope: {symptomPayload.SymptomType} ({symptomPayload.AffectedActivity})
Evidence Factors: {evidenceText}
Live Telemetry Readings: {proofText}
Recommended Next Step: {result.RecommendedNextStep}
-------------------------

STRICT RULES:
1. Reference the actual telemetry numbers from Live Telemetry Readings so the user sees concrete proof.
2. Keep the explanation concise: 2 to 3 sentences maximum.
3. Mention that the user can run the RigMD Autonomous ReAct Agent below to inspect deeper telemetry and preview safe fixes before anything changes.";
    }
}
