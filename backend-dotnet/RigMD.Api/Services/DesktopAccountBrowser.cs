using System.Diagnostics;
namespace RigMD.Api.Services;
public interface IDesktopAccountBrowser { void Open(Uri uri); }
public sealed class DesktopAccountBrowser : IDesktopAccountBrowser
{
    public void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}
