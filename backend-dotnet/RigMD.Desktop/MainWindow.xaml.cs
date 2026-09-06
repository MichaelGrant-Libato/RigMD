using System.Windows;

namespace RigMD.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await Browser.EnsureCoreWebView2Async();
        Browser.Source = new System.Uri("http://localhost:5273");
        Browser.Visibility = Visibility.Visible;
        LoadingText.Visibility = Visibility.Collapsed;
    }
}