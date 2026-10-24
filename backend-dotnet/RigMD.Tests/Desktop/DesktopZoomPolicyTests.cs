using RigMD.Desktop;

namespace RigMD.Tests.Desktop;

public class DesktopZoomPolicyTests
{
    [Theory]
    [InlineData(0.25, 0.8)]
    [InlineData(0.79, 0.8)]
    [InlineData(0.8, 0.8)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.75, 1.75)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.01, 2.0)]
    [InlineData(5.0, 2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void Clamp_KeepsZoomInReadableRange(double requested, double expected) =>
        Assert.Equal(expected, DesktopZoomPolicy.Clamp(requested));

    [Fact]
    public void RepeatedCommands_StopAtBothLimits_AndResetExactly()
    {
        var factor = DesktopZoomPolicy.Default;
        for (var i = 0; i < 100; i++)
            factor = DesktopZoomPolicy.Apply(factor, DesktopZoomCommand.Increase);
        Assert.Equal(2.0, factor);
        Assert.Equal(1.0, DesktopZoomPolicy.Apply(factor, DesktopZoomCommand.Reset));

        for (var i = 0; i < 100; i++)
            factor = DesktopZoomPolicy.Apply(factor, DesktopZoomCommand.Decrease);
        Assert.Equal(0.8, factor);
        Assert.Equal(1.0, DesktopZoomPolicy.Apply(factor, DesktopZoomCommand.Reset));
        Assert.Equal(1.0, DesktopZoomPolicy.Apply(0.9, DesktopZoomCommand.Increase));
        Assert.Equal(1.0, DesktopZoomPolicy.Apply(1.1, DesktopZoomCommand.Decrease));
    }

    [Theory]
    [InlineData(0xBD, (int)DesktopZoomCommand.Decrease)]
    [InlineData(0x6D, (int)DesktopZoomCommand.Decrease)]
    [InlineData(0x30, (int)DesktopZoomCommand.Reset)]
    [InlineData(0x60, (int)DesktopZoomCommand.Reset)]
    [InlineData(0xBB, (int)DesktopZoomCommand.Increase)]
    [InlineData(0x6B, (int)DesktopZoomCommand.Increase)]
    public void ControlShortcuts_HandleMainAndNumericKeypads(int key, int expected)
    {
        Assert.Equal((DesktopZoomCommand)expected, DesktopZoomPolicy.FromKey(key, control: true, alt: false));
        Assert.Equal(DesktopZoomCommand.None, DesktopZoomPolicy.FromKey(key, control: false, alt: false));
        Assert.Equal(DesktopZoomCommand.None, DesktopZoomPolicy.FromKey(key, control: true, alt: true));
    }

    [Theory]
    [InlineData(0x43)]
    [InlineData(0x56)]
    [InlineData(0x41)]
    [InlineData(0x50)]
    [InlineData(0x74)]
    public void UnrelatedKeys_AreNotIntercepted(int key) =>
        Assert.Equal(DesktopZoomCommand.None, DesktopZoomPolicy.FromKey(key, control: true, alt: false));
}
