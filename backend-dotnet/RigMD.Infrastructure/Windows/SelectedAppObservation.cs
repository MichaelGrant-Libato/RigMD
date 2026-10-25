using System.Diagnostics;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public static class SelectedAppObservation
{
    public static AppProcessObservationDto Read(string processName)
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName(processName); }
        catch { return new(null, null); }
        var count = 0;
        double bytes = 0;
        var complete = true;
        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited) continue;
                bytes += process.WorkingSet64;
                count++;
            }
            catch { complete = false; }
            finally { process.Dispose(); }
        }
        return complete ? new(count, bytes / 1024 / 1024) : new(null, null);
    }
}
