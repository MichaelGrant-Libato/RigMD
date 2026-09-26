using System;
using System.IO;
using System.Text.Json;

namespace RigMD.Application.Services;

public class RigMdAgentRuntimeSettings
{
    /// <summary>
    /// "auto" (Gemini 3.5 Flash cascade with Local ReAct fallback) or "local-only" (100% offline local C# ReAct engine).
    /// </summary>
    public string PreferredMode { get; set; } = "auto";

    /// <summary>
    /// When true, the Autonomous Doctor automatically executes safe Tier-1 maintenance tools
    /// (clear_temp_files, clear_browser_cache, flush_dns_cache) and verifies recovery immediately.
    /// </summary>
    public bool AutoExecuteSafeTier1 { get; set; } = false;

    /// <summary>
    /// Optional user-supplied Gemini API key stored strictly in %LocalAppData%\RigMD\agent-settings.json.
    /// </summary>
    public string? GeminiApiKey { get; set; }
}

public static class RigMdAgentRuntimeSettingsStore
{
    private static readonly object FileLock = new();

    public static string GetSettingsFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RigMD");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "agent-settings.json");
    }

    public static RigMdAgentRuntimeSettings Load()
    {
        lock (FileLock)
        {
            try
            {
                var path = GetSettingsFilePath();
                if (!File.Exists(path))
                {
                    return new RigMdAgentRuntimeSettings();
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RigMdAgentRuntimeSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new RigMdAgentRuntimeSettings();
            }
            catch
            {
                return new RigMdAgentRuntimeSettings();
            }
        }
    }

    public static RigMdAgentRuntimeSettings Save(
        string? preferredMode,
        bool? autoExecuteSafeTier1,
        string? geminiApiKey,
        bool clearApiKey = false)
    {
        lock (FileLock)
        {
            var current = Load();

            if (!string.IsNullOrWhiteSpace(preferredMode))
            {
                var normalized = preferredMode.Trim().ToLowerInvariant();
                current.PreferredMode = (normalized == "local-only" || normalized == "local")
                    ? "local-only"
                    : "auto";
            }

            if (autoExecuteSafeTier1.HasValue)
            {
                current.AutoExecuteSafeTier1 = autoExecuteSafeTier1.Value;
            }

            if (clearApiKey)
            {
                current.GeminiApiKey = null;
            }
            else if (!string.IsNullOrWhiteSpace(geminiApiKey))
            {
                current.GeminiApiKey = geminiApiKey.Trim();
            }

            try
            {
                var path = GetSettingsFilePath();
                var json = JsonSerializer.Serialize(current, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore non-fatal write errors in restricted test environments
            }

            return current;
        }
    }

    public static string? ResolveEffectiveGeminiApiKey(string? configuredApiKey = null)
    {
        var settings = Load();
        if (string.Equals(settings.PreferredMode, "local-only", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(settings.PreferredMode, "local", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
        {
            return settings.GeminiApiKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return configuredApiKey.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        return null;
    }

    public static string MaskApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var trimmed = key.Trim();
        if (trimmed.Length <= 8)
        {
            return "••••••••";
        }

        return $"{trimmed[..4]}••••••••{trimmed[^4..]}";
    }
}
