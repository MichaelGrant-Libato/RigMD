using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace RigMD.Desktop;

public partial class App : System.Windows.Application
{
    private Process? _apiProcess;
    private const string ApiUrl = "http://localhost:5273";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            StartApiProcess();

            await WaitForApiReady();

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"RigMD could not start its local API.\n\n{ex.Message}",
                "RigMD Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void StartApiProcess()
    {
        var apiExe = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "RigMD.Api.exe");

        if (!File.Exists(apiExe))
        {
            throw new FileNotFoundException(
                "The local API executable was not found.",
                apiExe);
        }

        _apiProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = apiExe,
                Arguments = $"--urls={ApiUrl}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            }
        };

        _apiProcess.StartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] =
            "Development";

        if (!_apiProcess.Start())
        {
            throw new InvalidOperationException(
                "The local API process could not be started.");
        }
    }

    private async Task WaitForApiReady()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        for (int i = 0; i < 30; i++)
        {
            if (_apiProcess == null || _apiProcess.HasExited)
            {
                throw new InvalidOperationException(
                    "The local API process exited before startup completed.");
            }

            try
            {
                using var response = await client.GetAsync(ApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();

                    var cssMatch = Regex.Match(
                        html,
                        "<link[^>]+href=\"([^\"]+\\.css)\"",
                        RegexOptions.IgnoreCase);

                    if (cssMatch.Success)
                    {
                        var cssPath = cssMatch.Groups[1].Value;
                        var cssUrl = new Uri(new Uri(ApiUrl), cssPath);

                        using var cssResponse = await client.GetAsync(cssUrl);

                        if (cssResponse.IsSuccessStatusCode &&
                            cssResponse.Content.Headers.ContentType?.MediaType ==
                            "text/css")
                        {
                            return;
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                // The API is not accepting connections yet.
            }
            catch (TaskCanceledException)
            {
                // The request timed out while the API was starting.
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException(
            "The local API and frontend assets did not become ready within the startup timeout.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_apiProcess != null)
        {
            try
            {
                if (!_apiProcess.HasExited)
                {
                    _apiProcess.Kill();
                    _apiProcess.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // The process has already exited.
            }
            finally
            {
                _apiProcess.Dispose();
            }
        }

        base.OnExit(e);
    }
}