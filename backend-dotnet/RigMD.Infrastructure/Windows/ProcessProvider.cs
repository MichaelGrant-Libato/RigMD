using System.Diagnostics;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class ProcessProvider : IProcessProvider
{
    private static readonly HashSet<string> BrowserNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi"
    };

    private static readonly HashSet<string> GameHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "valorant", "roblox", "genshin", "starrail", "fortnite",
        "cs2", "dota2", "league", "minecraft", "steam",
        "epicgameslauncher", "riotclientservices"
    };

    public ProcessInsightsDto GetProcessInsights()
    {
        var dto = new ProcessInsightsDto();
        var processTotals = new Dictionary<string, ProcessAppDto>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            var processes = Process.GetProcesses();
            
            foreach (var proc in processes)
            {
                try
                {
                    var name = proc.ProcessName;
                    var memoryMb = proc.WorkingSet64 / (1024.0 * 1024.0);

                    if (memoryMb <= 0) continue;

                    if (!processTotals.TryGetValue(name, out var appDto))
                    {
                        appDto = new ProcessAppDto { Name = name.ToLowerInvariant(), ProcessCount = 0, MemoryMb = 0 };
                        processTotals[name] = appDto;
                    }
                    
                    appDto.ProcessCount++;
                    appDto.MemoryMb += memoryMb;

                    if (memoryMb > 2500 && !GameHints.Contains(name) && !GameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                    {
                        dto.MemoryLeakWarning = $"Process '{name}' is consuming an unusually high amount of memory ({Math.Round(memoryMb, 2)} MB). This may indicate a memory leak.";
                    }

                    if (BrowserNames.Contains(name))
                    {
                        dto.BrowserProcessCount++;
                        dto.BrowserMemoryMb += memoryMb;
                    }

                    if (!dto.GameProcesses.Contains(name.ToLowerInvariant()) && 
                        (GameHints.Contains(name) || GameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase))))
                    {
                        dto.GameProcesses.Add(name.ToLowerInvariant());
                    }
                }
                catch
                {
                    // Ignore access denied on specific processes
                }
            }

            dto.BrowserDetected = dto.BrowserProcessCount > 0;
            dto.BrowserMemoryMb = Math.Round(dto.BrowserMemoryMb, 2);
            dto.BrowserHeavy = dto.BrowserProcessCount >= 8 || dto.BrowserMemoryMb >= 1024;
            
            dto.GameDetected = dto.GameProcesses.Count > 0;
            
            // Top 8 memory apps
            dto.TopMemoryApps = processTotals.Values
                .OrderByDescending(p => p.MemoryMb)
                .Take(8)
                .Select(p => {
                    p.MemoryMb = Math.Round(p.MemoryMb, 2);
                    return p;
                })
                .ToList();
        }
        catch
        {
            // Return empty stats if Process enumeration completely fails
        }

        return dto;
    }
}
