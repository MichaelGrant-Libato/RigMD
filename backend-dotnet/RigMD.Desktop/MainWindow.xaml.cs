using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace RigMD.Desktop;

public partial class MainWindow : Window
{
    private readonly string _localApiToken;
    private static readonly Uri LocalApiOrigin = new("http://localhost:5273");

    public MainWindow(string localApiToken)
    {
        _localApiToken = localApiToken;
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "RigMD",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            var environment =
                await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);

            await Browser.EnsureCoreWebView2Async(environment);
            ConfigureZoomControls();
            Browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                    target.GetLeftPart(UriPartial.Authority) != LocalApiOrigin.GetLeftPart(UriPartial.Authority))
                    args.Cancel = true;
            };
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;

            // Only the trusted local page receives the per-launch credential; never persist it.
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                $"if (window.location.origin === {JsonSerializer.Serialize(LocalApiOrigin.GetLeftPart(UriPartial.Authority))}) " +
                $"Object.defineProperty(window, '__rigmdLocalApiToken', {{ value: {JsonSerializer.Serialize(_localApiToken)}, writable: false, configurable: false }});");

#if !DEBUG
            // Lock down F12 DevTools, right-click Inspect, and browser accelerator keys in Release/Installed builds
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
#endif

            Browser.Source = new Uri(
                "http" + Uri.SchemeDelimiter + "localhost:5273");

            Browser.Visibility = Visibility.Visible;
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LoadingText.Text =
                "RigMD could not load the desktop interface.";

            MessageBox.Show(
                $"WebView2 could not initialize.\n\n{ex.Message}",
                "RigMD Interface Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ConfigureZoomControls()
    {
        Browser.CoreWebView2.Settings.IsZoomControlEnabled = true;
        // Pinch page-scaling bypasses ZoomFactor and can clip controls outside the viewport.
        Browser.CoreWebView2.Settings.IsPinchZoomEnabled = false;
        Browser.ZoomFactorChanged += Browser_ZoomFactorChanged;
        SetZoom(DesktopZoomPolicy.Default);
        ZoomToolbar.Visibility = Visibility.Visible;
    }

    private void SetZoom(double factor)
    {
        var bounded = DesktopZoomPolicy.Clamp(factor);
        if (Math.Abs(Browser.ZoomFactor - bounded) > 0.0001)
            Browser.ZoomFactor = bounded;
        ZoomResetButton.Content = $"{bounded * 100:0}%";
        ZoomOutButton.IsEnabled = bounded > DesktopZoomPolicy.Minimum;
        ZoomInButton.IsEnabled = bounded < DesktopZoomPolicy.Maximum;
    }

    private void ChangeZoom(DesktopZoomCommand command) =>
        SetZoom(DesktopZoomPolicy.Apply(Browser.ZoomFactor, command));

    private void Browser_ZoomFactorChanged(object? sender, EventArgs e) => SetZoom(Browser.ZoomFactor);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ChangeZoom(DesktopZoomCommand.Decrease);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ChangeZoom(DesktopZoomCommand.Reset);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ChangeZoom(DesktopZoomCommand.Increase);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Browser.CoreWebView2 == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var command = DesktopZoomPolicy.FromKey(KeyInterop.VirtualKeyFromKey(key),
            modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Alt));
        if (command == DesktopZoomCommand.None) return;
        e.Handled = true;
        // WebView2 forwards accelerator input synchronously; defer browser changes until it returns.
        Dispatcher.BeginInvoke(new Action(() => ChangeZoom(command)));
    }
}
