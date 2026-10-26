using System.Text.Json;
using RigMD.Desktop;

namespace RigMD.Tests.Desktop;

public class DesktopReportExportRequestTests
{
    private static string Payload(string filename = "RigMD-Device-Report-S-127-2026-10-27.pdf", string id = "test-123") =>
        JsonSerializer.Serialize(new { type = "rigmd:save-report-pdf", requestId = id, filename });

    [Fact]
    public void AcceptsOnlyTheTrustedLocalPageAndASuggestedPdfName()
    {
        Assert.True(DesktopReportExportRequest.TryParse("http://localhost:5273/reports", Payload(), out var request));
        Assert.Equal("test-123", request!.RequestId);
        Assert.Equal("RigMD-Device-Report-S-127-2026-10-27.pdf", request.FileName);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5274")]
    [InlineData("http://localhost:5273.evil.test")]
    [InlineData("http://localhost:5273@evil.test")]
    [InlineData("file:///C:/report.html")]
    [InlineData("null")]
    public void RejectsMessagesFromOtherOrigins(string source) =>
        Assert.False(DesktopReportExportRequest.TryParse(source, Payload(), out _));

    [Theory]
    [InlineData("../report.pdf")]
    [InlineData("C:\\report.pdf")]
    [InlineData("RigMD-Device-Report-test.html")]
    [InlineData("RigMD-Device-Report-test.pdf.exe")]
    [InlineData("RigMD-Device-Report-<script>.pdf")]
    public void RejectsPathsAndNonPdfSuggestedNames(string filename) =>
        Assert.False(DesktopReportExportRequest.TryParse("http://localhost:5273", Payload(filename), out _));

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"type\":123}")]
    [InlineData("{\"type\":\"rigmd:save-report-pdf\",\"requestId\":123,\"filename\":true}")]
    public void RejectsMalformedRequestsWithoutThrowing(string message) =>
        Assert.False(DesktopReportExportRequest.TryParse("http://localhost:5273", message, out _));

    [Fact]
    public void RejectsOversizedOrInvalidRequestIdentifiers()
    {
        Assert.False(DesktopReportExportRequest.TryParse("http://localhost:5273", Payload(id: new string('a', 81)), out _));
        Assert.False(DesktopReportExportRequest.TryParse("http://localhost:5273", Payload(id: "id\nother"), out _));
        Assert.False(DesktopReportExportRequest.TryParse("http://localhost:5273", new string(' ', 1025), out _));
    }
}
