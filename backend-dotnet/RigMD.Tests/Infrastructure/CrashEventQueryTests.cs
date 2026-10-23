using System.Text.Json;
using RigMD.Infrastructure.Remediation.Tools.Diagnostic;

namespace RigMD.Tests.Infrastructure;

public class CrashEventQueryTests
{
    [Theory]
    [InlineData("ApplicationCrash", "Application", "Application Error")]
    [InlineData("BugCheck", "System", "Microsoft-Windows-Kernel-Power")]
    public async Task FocusedQuery_UsesMatchingChannelProvidersAndStartTime(string focus, string channel, string provider)
    {
        string? command = null;
        var tool = new QueryWindowsEventLogsTool((start, _) =>
        {
            command = start.Arguments;
            return Task.FromResult((0, "", ""));
        });
        var since = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { eventFocus = focus, logName = "Application", sinceUtc = since }));
        var result = await tool.ExecuteAsync(args.RootElement);
        Assert.True(result.Success);
        Assert.Contains($"qe {channel} ", command);
        Assert.Contains(provider, command);
        Assert.Contains("@SystemTime >=", command);
        Assert.DoesNotContain("timediff", command);
        Assert.DoesNotContain("or Level=", command);
        using var data = JsonDocument.Parse(result.DataJson!);
        Assert.Equal(since, data.RootElement.GetProperty("sinceUtc").GetString());
        Assert.Equal(0, data.RootElement.GetProperty("eventCount").GetInt32());
    }

    [Theory]
    [InlineData(5, "", "")]
    [InlineData(0, "", "Access denied")]
    [InlineData(0, "not xml", "")]
    public async Task FailedQuery_NeverReportsZeroVerifiedRecords(int exitCode, string output, string error)
    {
        var tool = new QueryWindowsEventLogsTool((_, _) => Task.FromResult((exitCode, output, error)));
        using var args = JsonDocument.Parse("{}");
        var result = await tool.ExecuteAsync(args.RootElement);
        Assert.False(result.Success);
        using var data = JsonDocument.Parse(result.DataJson!);
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("eventCount").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(data.RootElement.GetProperty("warning").GetString()));
    }

    [Theory]
    [InlineData("not a timestamp")]
    [InlineData("2099-01-01T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00")]
    public async Task InvalidBoundary_DoesNotRunQuery(string since)
    {
        var calls = 0;
        var tool = new QueryWindowsEventLogsTool((_, _) => { calls++; return Task.FromResult((0, "", "")); });
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { sinceUtc = since }));
        Assert.False((await tool.ExecuteAsync(args.RootElement)).Success);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Boundary_ExcludesOldRecords_AndDoesNotAcceptUndatedRecords(bool validDate)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        var time = validDate ? since.AddMinutes(-1).ToString("O") : "missing";
        var xml = $"<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Application Error\"/><EventID>1000</EventID><Level>2</Level><TimeCreated SystemTime=\"{time}\"/></System></Event>";
        var tool = new QueryWindowsEventLogsTool((_, _) => Task.FromResult((0, xml, "")));
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { sinceUtc = since.ToString("O"), eventFocus = "ApplicationCrash" }));
        var result = await tool.ExecuteAsync(args.RootElement);
        Assert.Equal(validDate, result.Success);
        using var data = JsonDocument.Parse(result.DataJson!);
        if (validDate) Assert.Equal(0, data.RootElement.GetProperty("eventCount").GetInt32());
        else Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("eventCount").ValueKind);
    }

    [Theory]
    [InlineData("ApplicationCrash", "Unrelated service", "1000", 0)]
    [InlineData("ApplicationCrash", "Application Error", "1000", 1)]
    [InlineData("BugCheck", "Unrelated service", "1001", 0)]
    [InlineData("BugCheck", "BugCheck", "1001", 1)]
    public async Task FocusedRecords_DoNotCountUnrelatedProviders(string focus, string provider, string eventId, int expected)
    {
        var time = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O");
        var xml = $"<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"{provider}\"/><EventID>{eventId}</EventID><Level>2</Level><TimeCreated SystemTime=\"{time}\"/></System></Event>";
        var tool = new QueryWindowsEventLogsTool((_, _) => Task.FromResult((0, xml, "")));
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { eventFocus = focus, sinceUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O") }));
        var result = await tool.ExecuteAsync(args.RootElement);
        Assert.True(result.Success);
        using var data = JsonDocument.Parse(result.DataJson!);
        Assert.Equal(expected, data.RootElement.GetProperty("eventCount").GetInt32());
    }
}
