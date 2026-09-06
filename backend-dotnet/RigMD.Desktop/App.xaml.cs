using System.Configuration;
using System.Data;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RigMD.Desktop;

public partial class App : System.Windows.Application
{
    private Process? _apiProcess;
    private const string ApiUrl = "http://localhost:5273";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartApiProcess();
        await WaitForApiReady();
    }

    private void StartApiProcess()
    {
        var apiExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RigMD.Api.exe");
        if (!File.Exists(apiExe)) return;

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

        // Run in Development mode so appsettings.Development.json is loaded
        _apiProcess.StartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";

        _apiProcess.Start();
    }

    private async Task WaitForApiReady()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        for (int i = 0; i < 30; i++) // wait up to 30 seconds
        {
            try
            {
                var response = await client.GetAsync($"{ApiUrl}/api/hardware/live");
                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                {
                    return; // API is ready
                }
            }
            catch
            {
                // API not ready yet
            }
            await Task.Delay(1000);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_apiProcess != null && !_apiProcess.HasExited)
        {
            _apiProcess.Kill();
            _apiProcess.Dispose();
        }
        base.OnExit(e);
    }
}

