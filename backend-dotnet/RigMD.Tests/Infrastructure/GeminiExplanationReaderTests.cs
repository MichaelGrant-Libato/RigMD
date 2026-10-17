using System.Text.Json;
using RigMD.Infrastructure.Ai;

namespace RigMD.Tests.Infrastructure;

public class GeminiExplanationReaderTests
{
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
