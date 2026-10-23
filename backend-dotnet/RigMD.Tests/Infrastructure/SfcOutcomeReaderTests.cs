using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Tests.Infrastructure;

public class SfcOutcomeReaderTests
{
    [Fact]
    public async Task PreviewDoesNotInventAZeroByteEstimate_OrRunTheScan()
    {
        var tool = new RigMD.Infrastructure.Remediation.Tools.Remediation.RunSystemFileCheckerTool(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var preview = await tool.PreviewImpactAsync(System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        Assert.False(preview.EstimatedBytesKnown);
        Assert.True(preview.RequiresUserConfirmation);
        Assert.True(preview.RequiresAdmin);
    }

    [Theory]
    [InlineData(0, "Windows Resource Protection did not find any integrity violations.", "No integrity violations reported")]
    [InlineData(0, "Windows Resource Protection found corrupt files and successfully repaired them.", "Repairs reported")]
    [InlineData(0, "Windows Resource Protection found corrupt files but was unable to fix some of them.", "Unrepaired files reported")]
    [InlineData(0, "Windows Resource Protection could not perform the requested operation.", "Could not complete")]
    [InlineData(0, "There is a system repair pending which requires reboot to complete.", "Restart required")]
    [InlineData(1, "Windows Resource Protection did not find any integrity violations.", "Could not complete")]
    [InlineData(0, "", "Unverified")]
    [InlineData(0, "100% complete", "Unverified")]
    [InlineData(0, "Localized output not recognized by this parser.", "Unverified")]
    [InlineData(0, "did not find any integrity violations. found corrupt files and successfully repaired them.", "Unverified")]
    public void ConclusionRequiresTheActualWindowsMessage_NotJustZeroExitCode(int exit, string output, string expected)
    {
        var outcome = SfcOutcomeReader.Read(exit, output);
        Assert.Equal(expected, outcome.Status);
        Assert.DoesNotContain("app crash is fixed", outcome.Summary);
    }

    [Fact]
    public void NullSeparatorsAndLineWrappingAreHandledWithoutInventingAnOutcome()
    {
        var text = "Windows Resource Protection did not find\r\nany integrity violations.";
        Assert.Equal("No integrity violations reported", SfcOutcomeReader.Read(0, string.Join('\0', text.ToCharArray())).Status);
    }
}
