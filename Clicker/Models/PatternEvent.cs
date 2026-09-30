using System.Text.Json.Serialization;

namespace Clicker.Models;

public enum PatternEventKind
{
    Move,
    MouseDown,
    MouseUp,
    Wheel,
    HorizontalWheel,
    KeyDown,
    KeyUp,
}

public enum PatternMouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>
/// One recorded input event. Short JSON names and "skip defaults" keep long recordings small on disk.
/// </summary>
public readonly record struct PatternEvent
{
    /// <summary>Milliseconds since the start of the pattern.</summary>
    [JsonPropertyName("t")]
    public int Time { get; init; }

    [JsonPropertyName("k")]
    public PatternEventKind Kind { get; init; }

    /// <summary>Cursor position (physical pixels, virtual desktop) for mouse events.</summary>
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("b")]
    public PatternMouseButton Button { get; init; }

    /// <summary>Wheel delta (120 = one notch).</summary>
    [JsonPropertyName("d")]
    public int Delta { get; init; }

    [JsonPropertyName("vk")]
    public int VirtualKey { get; init; }

    /// <summary>Hardware scan code — games usually read keys by scan code.</summary>
    [JsonPropertyName("sc")]
    public int ScanCode { get; init; }

    [JsonPropertyName("ex")]
    public bool Extended { get; init; }
}
