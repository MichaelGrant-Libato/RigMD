using RigMD.Api.Services;
using System.Text.Json;

namespace RigMD.Tests.Api;

public class WindowsBootHistoryTests
{
    [Fact]
    public void ReadsWindowsBootDurationsAndPreservesMeasuredZero()
    {
        var time = DateTimeOffset.Parse("2026-11-27T01:02:03Z");
        var record = WindowsBootHistory.ReadRecord("""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <EventData>
                <Data Name="BootTime">32150</Data>
                <Data Name="MainPathBootTime">32150</Data>
                <Data Name="BootPostBootTime">0</Data>
              </EventData>
            </Event>
            """, time);
        Assert.NotNull(record);
        Assert.Equal(time, record.RecordedAt);
        Assert.Equal(32150, record.DurationMs);
        Assert.Equal(32150, record.MainPathMs);
        Assert.Equal(0, record.PostBootMs);
    }

    [Theory]
    [InlineData("<Event><EventData /></Event>")]
    [InlineData("<Event><EventData><Data Name='BootTime'>0</Data></EventData></Event>")]
    [InlineData("<broken")]
    public void MissingAndInvalidBootDurationAreSkipped(string xml)
    {
        Assert.Null(WindowsBootHistory.ReadRecord(xml, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CrashRecordsUseTheSavedWindowForTheSelectedLog()
    {
        var proof = JsonSerializer.Deserialize<JsonElement[]>("""
            [
              {"metric_id":"application_crash_history","window_start":"2026-11-20T01:00:00Z","window_end":"2026-11-27T01:00:00Z"},
              {"metric_id":"system_crash_history","window_start":"2026-11-01T01:00:00Z","window_end":"2026-11-27T01:00:00Z"}
            ]
            """)!.Cast<object>().ToArray();
        var now = DateTimeOffset.Parse("2026-11-27T02:00:00Z");
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T01:00:00Z"),
            RecentWindowsChanges.CrashWindow(proof, application: true, now)!.Value.Start);
        Assert.Null(RecentWindowsChanges.CrashWindow(proof, application: false, now));
    }
}
