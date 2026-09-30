using System.Collections.Generic;

namespace Clicker.Models;

public sealed class AppSettings
{
    public const int MinIntervalMs = 1;
    public const int MaxIntervalMs = 3_600_000; // 1 hour

    public int IntervalMs { get; set; } = 100;

    public MouseButtonKind Button { get; set; } = MouseButtonKind.Left;

    /// <summary>Start/stop hotkey.</summary>
    public HotkeyBinding Hotkey { get; set; } = HotkeyBinding.DefaultToggle;

    /// <summary>"Save the current cursor position as a point" hotkey.</summary>
    public HotkeyBinding RecordHotkey { get; set; } = HotkeyBinding.DefaultRecord;

    /// <summary>Saved screen points. At most one has IsActive = true.</summary>
    public List<SavedPoint> Points { get; set; } = new();

    /// <summary>Put the cursor back on the active point before every click (otherwise only once on start).</summary>
    public bool MoveBeforeEachClick { get; set; } = true;

    /// <summary>"uk" or "en". Empty = pick from the Windows language on first start.</summary>
    public string Language { get; set; } = "";
}
