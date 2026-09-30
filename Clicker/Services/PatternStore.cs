using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clicker.Models;

namespace Clicker.Services;

/// <summary>
/// Patterns live in their own file (%APPDATA%\Clicker\patterns.json): recordings can be large,
/// and settings.json is rewritten on every small change.
/// </summary>
public static class PatternStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clicker", "patterns.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static List<Pattern> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var patterns = JsonSerializer.Deserialize<List<Pattern>>(File.ReadAllText(FilePath), Options);
                if (patterns != null)
                    return Sanitize(patterns);
            }
        }
        catch
        {
            // Corrupt file — start with an empty list rather than crashing.
        }

        return new List<Pattern>();
    }

    public static void Save(IEnumerable<Pattern> patterns)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Write to a temp file first so a crash mid-write can't destroy existing recordings.
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(patterns.ToList(), Options));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            // Not critical: patterns just won't persist.
        }
    }

    private static List<Pattern> Sanitize(List<Pattern> patterns)
    {
        patterns.RemoveAll(p => p == null || p.Events.Count == 0);

        bool activeSeen = false;
        foreach (Pattern pattern in patterns)
        {
            if (pattern.IsActive && activeSeen) pattern.IsActive = false;
            activeSeen |= pattern.IsActive;

            // Events must be in time order for playback.
            pattern.Events = pattern.Events.OrderBy(e => e.Time).ToList();
        }

        return patterns;
    }
}
