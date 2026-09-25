using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RigMD.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\RigMD_Desktop_SingleInstance_Mutex";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private Process? _apiProcess;
    private IntPtr _jobHandle = IntPtr.Zero;

    private const string ApiHost = "localhost";
    private const int ApiPort = 5273;

    private static readonly string ApiUrl =
        $"http{Uri.SchemeDelimiter}{ApiHost}:{ApiPort}";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show(
                    "RigMD is already running.",
                    "RigMD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown(0);
                return;
            }
        }
        catch
        {
            // Non-fatal if mutex creation fails in restricted environments
        }

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
        KillStaleApiProcesses();

        var desktopDirectory =
            AppDomain.CurrentDomain.BaseDirectory;

        string apiDirectory;

#if DEBUG
        apiDirectory = Path.GetFullPath(Path.Combine(desktopDirectory, "..", "..", "..", "..", "RigMD.Api", "bin", "Debug", "net10.0-windows"));
#else
        var rigMdDirectory =
            Directory.GetParent(
                desktopDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar))
            ?.FullName;

        if (string.IsNullOrWhiteSpace(rigMdDirectory))
        {
            throw new DirectoryNotFoundException(
                "The RigMD installation directory could not be determined.");
        }

        apiDirectory =
            Path.Combine(
                rigMdDirectory,
                "Api");
#endif

        var apiExe =
            Path.Combine(
                apiDirectory,
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
                Arguments = $"--parent-pid {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = apiDirectory
            }
        };

#if DEBUG
        _apiProcess.StartInfo.EnvironmentVariables[
            "ASPNETCORE_ENVIRONMENT"] = "Development";

        _apiProcess.StartInfo.EnvironmentVariables[
            "DOTNET_ENVIRONMENT"] = "Development";
#else
        _apiProcess.StartInfo.EnvironmentVariables[
            "ASPNETCORE_ENVIRONMENT"] = "Production";

        _apiProcess.StartInfo.EnvironmentVariables[
            "DOTNET_ENVIRONMENT"] = "Production";
#endif

        _apiProcess.StartInfo.EnvironmentVariables[
            "ASPNETCORE_URLS"] = ApiUrl;

        if (!_apiProcess.Start())
        {
            throw new InvalidOperationException(
                "The local API process could not be started.");
        }

        BindProcessToKillOnCloseJob(_apiProcess);
    }

    /// <summary>
    /// Cleans up any orphaned RigMD.Api processes from a prior crash before binding port 5273.
    /// Safe because the Single-Instance Mutex guarantees no other RigMD.Desktop instance is active.
    /// </summary>
    private static void KillStaleApiProcesses()
    {
        try
        {
            foreach (var stale in Process.GetProcessesByName("RigMD.Api"))
            {
                using (stale)
                {
                    try
                    {
                        if (!stale.HasExited)
                        {
                            stale.Kill(entireProcessTree: true);
                            stale.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        // Ignore if already exiting or inaccessible
                    }
                }
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    /// <summary>
    /// Attaches the child API process to a Windows Job Object configured with
    /// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so the OS kernel automatically terminates
    /// RigMD.Api.exe if RigMD.Desktop.exe crashes or is killed via Task Manager.
    /// </summary>
    private void BindProcessToKillOnCloseJob(Process childProcess)
    {
        try
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero)
            {
                return;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, extendedInfoPtr, false);
                if (SetInformationJobObject(
                        _jobHandle,
                        JobObjectExtendedLimitInformation,
                        extendedInfoPtr,
                        (uint)length))
                {
                    AssignProcessToJobObject(_jobHandle, childProcess.Handle);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }
        catch
        {
            // Fallback to --parent-pid watcher in RigMD.Api if Job Object assignment is restricted
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
                using var response =
                    await client.GetAsync(ApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var html =
                        await response.Content.ReadAsStringAsync();

                    var cssMatch =
                        Regex.Match(
                            html,
                            "<link[^>]+href=\"([^\"]+\\.css)\"",
                            RegexOptions.IgnoreCase);

                    if (cssMatch.Success)
                    {
                        var cssPath =
                            cssMatch.Groups[1].Value;

                        var cssUrl =
                            new Uri(
                                new Uri(ApiUrl),
                                cssPath);

                        using var cssResponse =
                            await client.GetAsync(cssUrl);

                        if (cssResponse.IsSuccessStatusCode &&
                            cssResponse.Content.Headers.ContentType
                                ?.MediaType == "text/css")
                        {
                            return;
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                // API is not accepting connections yet.
            }
            catch (TaskCanceledException)
            {
                // Request timed out while API was starting.
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
                    _apiProcess.Kill(entireProcessTree: true);
                    _apiProcess.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
            finally
            {
                _apiProcess.Dispose();
            }
        }

        if (_jobHandle != IntPtr.Zero)
        {
            try
            {
                CloseHandle(_jobHandle);
            }
            catch { }
            finally
            {
                _jobHandle = IntPtr.Zero;
            }
        }

        if (_singleInstanceMutex != null)
        {
            try
            {
                if (_ownsMutex)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
            }
            catch { }
            finally
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }

        base.OnExit(e);
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}