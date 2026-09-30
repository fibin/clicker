namespace Clicker.Models;

public sealed class AppSettings
{
    public const int MinIntervalMs = 1;
    public const int MaxIntervalMs = 3_600_000; // 1 hour

    public int IntervalMs { get; set; } = 100;

    public MouseButtonKind Button { get; set; } = MouseButtonKind.Left;

    public HotkeyBinding Hotkey { get; set; } = HotkeyBinding.Default;

    /// <summary>"uk" or "en". Empty = pick from the Windows language on first start.</summary>
    public string Language { get; set; } = "";
}
