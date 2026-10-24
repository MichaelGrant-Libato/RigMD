namespace RigMD.Desktop;

internal enum DesktopZoomCommand
{
    None,
    Decrease,
    Reset,
    Increase
}

internal static class DesktopZoomPolicy
{
    public const double Minimum = 0.8;
    public const double Maximum = 2.0;
    public const double Default = 1.0;

    public static double Clamp(double factor) =>
        double.IsFinite(factor) ? Math.Clamp(factor, Minimum, Maximum) : Default;

    public static double Apply(double factor, DesktopZoomCommand command) => command switch
    {
        DesktopZoomCommand.Decrease => Clamp(Math.Round(Clamp(factor) - 0.1, 2)),
        DesktopZoomCommand.Increase => Clamp(Math.Round(Clamp(factor) + 0.1, 2)),
        DesktopZoomCommand.Reset => Default,
        _ => Clamp(factor)
    };

    public static DesktopZoomCommand FromKey(int virtualKey, bool control, bool alt) =>
        !control || alt ? DesktopZoomCommand.None : virtualKey switch
        {
            0xBD or 0x6D => DesktopZoomCommand.Decrease,
            0x30 or 0x60 => DesktopZoomCommand.Reset,
            0xBB or 0x6B => DesktopZoomCommand.Increase,
            _ => DesktopZoomCommand.None
        };
}
