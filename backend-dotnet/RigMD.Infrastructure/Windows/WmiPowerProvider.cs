using System.Diagnostics;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Infrastructure.Windows;

public class WmiPowerProvider : IPowerProvider
{
    public string GetActivePowerPlan()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                // Output looks like: Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
                var startIndex = output.IndexOf('(');
                var endIndex = output.IndexOf(')');
                
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    return output.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return "Unknown";
    }
}
