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

public class GeminiAiExplainer : IAiExplainer
{
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

    public async Task<string> GenerateExplanationAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload)
    {
        var apiKey = _configuration["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("No Gemini API key found. Falling back to offline explainer.");
            return await _offlineFallback.GenerateExplanationAsync(result, symptomPayload);
        }

        try
        {
            return await CallGeminiApiAsync(apiKey, result, symptomPayload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call Gemini API. Falling back to offline explainer.");
            return await _offlineFallback.GenerateExplanationAsync(result, symptomPayload);
        }
    }

    private async Task<string> CallGeminiApiAsync(string apiKey, DiagnosticResult result, DiagnosticSymptomPayload symptomPayload)
    {
        var prompt = BuildPrompt(result, symptomPayload);
        
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            }
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
        
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var generatedText = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return generatedText?.Trim() ?? "An unexpected error occurred while generating the explanation.";
    }

    private string BuildPrompt(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload)
    {
        var evidenceStrings = result.Evidence.Select(e => $"{e.Label}: {e.Value}").ToList();
        var evidenceText = evidenceStrings.Any() ? string.Join(", ", evidenceStrings) : "None";

        var proofStrings = result.AllLiveProof
            .Where(p => p.Status.Equals("elevated", StringComparison.OrdinalIgnoreCase) || p.Status.Equals("high", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Label}: {p.Value}")
            .ToList();
        var proofText = proofStrings.Any() ? string.Join(", ", proofStrings) : "No abnormal telemetry readings detected.";

        return $@"
You are RigMD's diagnostic explanation engine. Your job is to explain the following diagnostic result to a non-technical user in simple, plain English.

--- DIAGNOSTIC RESULT ---
Diagnosed Issue: {result.DiagnosedCategory}
Confidence Level: {result.ConfidenceLabel}
Recommended Action: {result.ActionCategory}
Key Evidence from User: {evidenceText}
Live Abnormal Telemetry: {proofText}
-------------------------

RULES (YOU MUST FOLLOW THESE STRICTLY):
1. Do NOT suggest any Windows commands (e.g., sfc /scannow, chkdsk).
2. Do NOT output registry paths or specific executable names.
3. Keep the explanation extremely concise: exactly 1 to 3 sentences maximum.
4. Speak directly to the user (e.g., 'RigMD found that...').
5. Explain *why* the issue was diagnosed based on the evidence and telemetry, and briefly reassure them that the recommended action is the safest next step.
";
    }
}
