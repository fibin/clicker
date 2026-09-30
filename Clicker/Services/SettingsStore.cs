using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clicker.Models;

namespace Clicker.Services;

/// <summary>Saves settings as JSON in %APPDATA%\Clicker\settings.json.</summary>
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clicker", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (settings != null)
                    return Sanitize(settings);
            }
        }
        catch
        {
            // Corrupt file — fall back to defaults.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Not critical: settings just won't persist.
        }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        s.IntervalMs = Math.Clamp(s.IntervalMs, AppSettings.MinIntervalMs, AppSettings.MaxIntervalMs);
        if (s.Hotkey == null || !s.Hotkey.IsValid)
            s.Hotkey = HotkeyBinding.Default;
        if (!Enum.IsDefined(s.Button))
            s.Button = MouseButtonKind.Left;
        s.Language ??= "";
        return s;
    }
}
