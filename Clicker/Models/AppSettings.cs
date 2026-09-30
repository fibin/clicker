using System.Collections.Generic;

namespace Clicker.Models;

public sealed class AppSettings
{
    public const int MinIntervalMs = 1;
    public const int MaxIntervalMs = 3_600_000; // 1 hour
    public const int MaxRepeatCount = 1_000_000;
    public const int MaxLoopDelayMs = 3_600_000;

    public int IntervalMs { get; set; } = 100;

    public MouseButtonKind Button { get; set; } = MouseButtonKind.Left;

    /// <summary>Start/stop hotkey.</summary>
    public HotkeyBinding Hotkey { get; set; } = HotkeyBinding.DefaultToggle;

    /// <summary>"Save the current cursor position as a point" hotkey.</summary>
    public HotkeyBinding RecordHotkey { get; set; } = HotkeyBinding.DefaultRecord;

    /// <summary>Start / stop recording a pattern.</summary>
    public HotkeyBinding PatternHotkey { get; set; } = HotkeyBinding.DefaultPattern;

    /// <summary>How many times a pattern is played; 0 = until stopped.</summary>
    public int PatternRepeatCount { get; set; }

    /// <summary>Pause between pattern repeats.</summary>
    public int PatternLoopDelayMs { get; set; } = 500;

    /// <summary>Record keyboard keys too (not only the mouse).</summary>
    public bool RecordKeyboard { get; set; } = true;

    /// <summary>Saved screen points. At most one has IsActive = true.</summary>
    public List<SavedPoint> Points { get; set; } = new();

    /// <summary>Put the cursor back on the active point before every click (otherwise only once on start).</summary>
    public bool MoveBeforeEachClick { get; set; } = true;

    // ---- Status overlay ----

    public const int MinOverlayOpacity = 10;
    public const int MinOverlayScale = 50;
    public const int MaxOverlayScale = 250;

    public bool OverlayEnabled { get; set; } = true;

    /// <summary>Opacity in percent (10..100).</summary>
    public int OverlayOpacity { get; set; } = 85;

    /// <summary>Size in percent (50..250).</summary>
    public int OverlayScale { get; set; } = 100;

    /// <summary>Blink while clicking / playing / recording.</summary>
    public bool OverlayBlink { get; set; } = true;

    public BlinkSpeed OverlayBlinkSpeed { get; set; } = BlinkSpeed.Normal;

    /// <summary>Also show the overlay while nothing is running.</summary>
    public bool OverlayShowWhenStopped { get; set; }

    /// <summary>Top-left corner in WPF units (DIPs). Null = default place (top-right of the main screen).</summary>
    public double? OverlayLeft { get; set; }

    public double? OverlayTop { get; set; }

    /// <summary>"uk" or "en". Empty = pick from the Windows language on first start.</summary>
    public string Language { get; set; } = "";
}
