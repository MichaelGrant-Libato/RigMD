using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace RigMD.Desktop;

public partial class MainWindow : Window
{
    private readonly string _localApiToken;
    private static readonly Uri LocalApiOrigin = new("http://localhost:5273");
    private bool _isSavingPdf;
    private bool _exitConfirmationOpen;

    public MainWindow(string localApiToken)
    {
        _localApiToken = localApiToken;
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
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
                if (_isSavingPdf) { args.Cancel = true; return; }
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                    target.GetLeftPart(UriPartial.Authority) != LocalApiOrigin.GetLeftPart(UriPartial.Authority))
                    args.Cancel = true;
            };
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.WebMessageReceived += Browser_WebMessageReceived;

            // Only the trusted local page receives the per-launch credential; never persist it.
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                $"if (window.location.origin === {JsonSerializer.Serialize(LocalApiOrigin.GetLeftPart(UriPartial.Authority))}) " +
                $"Object.defineProperty(window, '__rigmdLocalApiToken', {{ value: {JsonSerializer.Serialize(_localApiToken)}, writable: false, configurable: false }});");
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "if (window.top === window && window.location.origin === 'http://localhost:5273') " +
                "Object.defineProperty(window, '__rigmdDesktopPdfExport', { value: true, writable: false, configurable: false });");

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

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Cancel first so re-entrant close requests cannot bypass the dialog.
        e.Cancel = true;
        if (_exitConfirmationOpen) return;
        _exitConfirmationOpen = true;
        try
        {
            e.Cancel = new ExitConfirmationDialog(_isSavingPdf) { Owner = this }.ShowDialog() != true;
        }
        finally { _exitConfirmationOpen = false; }
    }

    private async void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!DesktopReportExportRequest.TryParse(e.Source, e.WebMessageAsJson, out var request) ||
            Browser.Source?.GetLeftPart(UriPartial.Authority) != LocalApiOrigin.GetLeftPart(UriPartial.Authority))
            return;
        if (_isSavingPdf) { ReplyToPdfRequest(request!, "busy"); return; }

        _isSavingPdf = true;
        Browser.IsEnabled = false;
        ZoomToolbar.IsEnabled = false;
        string? temporaryPath = null;
        try
        {
            // Only print when the formatted report is mounted; never export the application shell.
            var hasReport = await Browser.CoreWebView2.ExecuteScriptAsync(
                "Boolean(document.querySelector('.rigmd-print-report .rigmd-report-paper'))");
            if (hasReport != "true") throw new InvalidOperationException("No report is ready.");
            var dialog = new SaveFileDialog
            {
                Title = "Save RigMD report as PDF",
                FileName = request!.FileName,
                DefaultExt = ".pdf",
                Filter = "PDF document (*.pdf)|*.pdf",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) { ReplyToPdfRequest(request, "canceled"); return; }
            if (!string.Equals(Path.GetExtension(dialog.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The report needs a PDF filename.");

            var settings = Browser.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            using var pdf = await Browser.CoreWebView2.PrintToPdfStreamAsync(settings);
            var directory = Path.GetDirectoryName(dialog.FileName)!;
            temporaryPath = Path.Combine(directory, $".rigmd-report-{Guid.NewGuid():N}.tmp");
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await pdf.CopyToAsync(destination);
            // Preserve any existing report until the replacement PDF has been written completely.
            File.Move(temporaryPath, dialog.FileName, overwrite: true);
            temporaryPath = null;
            ReplyToPdfRequest(request, "saved");
        }
        catch (Exception)
        {
            // Do not send local paths or exception details to the page.
            ReplyToPdfRequest(request!, "error");
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _isSavingPdf = false;
            Browser.IsEnabled = true;
            ZoomToolbar.IsEnabled = true;
        }
    }

    private void ReplyToPdfRequest(DesktopReportExportRequest request, string status)
    {
        try
        {
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "rigmd:report-pdf-result", requestId = request.RequestId, status
            }));
        }
        catch (InvalidOperationException) { /* The browser may have closed during system shutdown. */ }
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
