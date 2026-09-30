using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>Returns <paramref name="current"/> if usable, otherwise the first free F-key starting at the default.</summary>
    private static HotkeyBinding PickHotkey(HotkeyBinding? current, HotkeyBinding fallback, params HotkeyBinding[] taken)
    {
        bool IsFree(HotkeyBinding b) => b.IsValid && !taken.Any(t => t.SameAs(b));

        if (current != null && IsFree(current)) return current;
        if (IsFree(fallback)) return fallback;

        for (int vk = 0x70; vk <= 0x7B; vk++) // F1..F12
        {
            var candidate = new HotkeyBinding { VirtualKey = vk };
            if (IsFree(candidate)) return candidate;
        }

        return fallback;
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        s.IntervalMs = Math.Clamp(s.IntervalMs, AppSettings.MinIntervalMs, AppSettings.MaxIntervalMs);
        // Every action needs a valid hotkey, and no two actions may share one.
        s.Hotkey = PickHotkey(s.Hotkey, HotkeyBinding.DefaultToggle);
        s.RecordHotkey = PickHotkey(s.RecordHotkey, HotkeyBinding.DefaultRecord, s.Hotkey);
        s.PatternHotkey = PickHotkey(s.PatternHotkey, HotkeyBinding.DefaultPattern, s.Hotkey, s.RecordHotkey);

        s.PatternRepeatCount = Math.Clamp(s.PatternRepeatCount, 0, AppSettings.MaxRepeatCount);
        s.PatternLoopDelayMs = Math.Clamp(s.PatternLoopDelayMs, 0, AppSettings.MaxLoopDelayMs);

        s.Points ??= new List<SavedPoint>();
        s.Points.RemoveAll(p => p == null);
        bool activeSeen = false;
        foreach (SavedPoint point in s.Points)
        {
            // Keep only the first active point.
            if (point.IsActive && activeSeen) point.IsActive = false;
            activeSeen |= point.IsActive;
        }

        if (!Enum.IsDefined(s.Button))
            s.Button = MouseButtonKind.Left;
        s.Language ??= "";
        return s;
    }
}
