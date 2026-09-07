using System;
using System.Windows;

namespace RigMD.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();

            Browser.Source = new Uri("http://localhost:5273");

            Browser.Visibility = Visibility.Visible;
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LoadingText.Text = "RigMD could not load the desktop interface.";

            MessageBox.Show(
                $"WebView2 could not initialize.\n\n{ex.Message}",
                "RigMD Interface Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}