using System.Text.Json;
using RigMD.Infrastructure.Ai;

namespace RigMD.Tests.Infrastructure;

public class GeminiExplanationReaderTests
{
    [Theory]
    [InlineData("Available")]
    [InlineData("Partial")]
    [InlineData("Unavailable")]
    [InlineData("High")]
    public async Task LocalExplanationDoesNotMisrepresentCoverageAsDiagnosticCertainty(string coverage)
    {
        var answer = await new OfflineAiExplainer().GenerateExplanationWithSourceAsync(
            new RigMD.Domain.Rules.DiagnosticResult
            {
                DiagnosedCategory = "High Memory Pressure", ConfidenceLabel = coverage,
                AllLiveProof = [new() { Label = "Physical Memory (RAM)", Value = "25%", Status = "normal" },
                    new() { Label = "Application memory warning", Value = "Reported warning", Status = "high" }]
            }, new RigMD.Domain.Rules.DiagnosticSymptomPayload());
        Assert.DoesNotContain("certainty:", answer.Text);
        Assert.DoesNotContain("consuming a high share", answer.Text);
        Assert.Contains("separately from the total RAM", answer.Text);
    }

    [Fact]
    public void AcceptedGeminiResponseCarriesExplicitSourceAndReturnedModelVersion()
    {
        using var json = JsonDocument.Parse("""{"modelVersion":"gemini-test-001","candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"Measured memory use is high."}]}}]}""");
        var answer = GeminiAiExplainer.ReadExplanationResponse(json.RootElement, "requested-model");
        Assert.NotNull(answer);
        Assert.Equal("gemini", answer.Source);
        Assert.Equal("gemini-test-001", answer.Model);
        Assert.Equal("Measured memory use is high.", answer.Text);
    }

    [Fact]
    public void MissingModelVersionDoesNotTurnTheRequestedModelIntoAClaimAboutTheReturnedModel()
    {
        using var json = JsonDocument.Parse("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"Measured memory use is high."}]}}]}""");
        var answer = GeminiAiExplainer.ReadExplanationResponse(json.RootElement, "requested-model");
        Assert.NotNull(answer);
        Assert.Equal("gemini", answer.Source);
        Assert.Null(answer.Model);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"candidates\":null}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}")]
    public void RejectedResponseDoesNotReceiveGeminiAttribution(string value)
    {
        using var json = JsonDocument.Parse(value);
        Assert.Null(GeminiAiExplainer.ReadExplanationResponse(json.RootElement, "test-model"));
    }

    [Fact]
    public async Task BuiltInGuidanceIsExplicitlyLocal_NotGemini()
    {
        var answer = await new OfflineAiExplainer().GenerateExplanationWithSourceAsync(
            new RigMD.Domain.Rules.DiagnosticResult { DiagnosedCategory = "High Memory Pressure", ActionCategory = "Troubleshoot", ConfidenceLabel = "High" },
            new RigMD.Domain.Rules.DiagnosticSymptomPayload());
        Assert.Equal("local", answer.Source);
        Assert.Null(answer.Model);
    }

    [Theory]
    [InlineData("Close unused apps")]
    [InlineData("Close **unused apps**")]
    [InlineData("- Close unused apps\n- Check memory")]
    [InlineData("Memory use is normal.")]
    public void CompletedAnswer_DoesNotRequireTerminalPunctuation(string text)
    {
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            finishReason = "STOP", content = new { parts = new[] { new { text } } }
        }));
        Assert.Equal(text, GeminiAiExplainer.ReadCompletedExplanation(response.RootElement));
    }

    [Fact]
    public void CompletedAnswer_CombinesTextParts_WithoutExposingThoughtsOrSignatures()
    {
        using var response = JsonDocument.Parse("""
            {"finishReason":"STOP","content":{"parts":[
              {"thought":true,"text":"private reasoning"},
              {"thoughtSignature":"opaque signature"},
              {"text":"Close unused "},
              {"thought":false,"text":"**apps**"}
            ]}}
            """);
        Assert.Equal("Close unused **apps**", GeminiAiExplainer.ReadCompletedExplanation(response.RootElement));
    }

    [Theory]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    [InlineData("RECITATION")]
    [InlineData("OTHER")]
    [InlineData(null)]
    public void IncompleteOrBlockedAnswer_IsNotAccepted_EvenWithFinalPunctuation(string? finishReason)
    {
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            finishReason, content = new { parts = new[] { new { text = "This is not an accepted answer." } } }
        }));
        Assert.Null(GeminiAiExplainer.ReadCompletedExplanation(response.RootElement));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"content\":{\"parts\":[{\"text\":\"Unconfirmed.\"}]}}")]
    [InlineData("{\"finishReason\":\"STOP\",\"content\":null}")]
    [InlineData("{\"finishReason\":\"STOP\",\"content\":{\"parts\":null}}")]
    [InlineData("{\"finishReason\":\"STOP\",\"content\":{\"parts\":[null,{\"text\":12},{\"text\":\" \"}]}}")]
    [InlineData("{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"Reasoning only.\"}]}}")]
    public void MissingMalformedOrThoughtOnlyAnswer_IsNotAccepted(string json)
    {
        using var response = JsonDocument.Parse(json);
        Assert.Null(GeminiAiExplainer.ReadCompletedExplanation(response.RootElement));
    }
}
