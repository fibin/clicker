namespace Clicker.Models;

/// <summary>Things a global hotkey can do.</summary>
public enum HotkeyAction
{
    /// <summary>Start / stop auto-clicking or pattern playback.</summary>
    Toggle,

    /// <summary>Save the current cursor position as a point.</summary>
    SavePoint,

    /// <summary>Start / stop recording a pattern.</summary>
    RecordPattern,
}
