using System.Collections.Generic;
using System.Windows.Input;
using Clicker.Localization;

namespace Clicker.Models;

/// <summary>A key (or mouse button) plus optional modifiers. Immutable so it can be shared with the hook thread.</summary>
public sealed class HotkeyBinding
{
    public const int VkMiddleMouse = 0x04;
    public const int VkMouse4 = 0x05;
    public const int VkMouse5 = 0x06;

    /// <summary>Win32 virtual-key code. Mouse buttons use VK_MBUTTON / VK_XBUTTON1 / VK_XBUTTON2.</summary>
    public int VirtualKey { get; init; }

    public bool Ctrl { get; init; }

    public bool Alt { get; init; }

    public bool Shift { get; init; }

    public bool Win { get; init; }

    public static HotkeyBinding Default => new() { VirtualKey = 0x75 }; // F6

    public bool IsValid => VirtualKey > 0 && VirtualKey < 0xFF;

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    private static string KeyName(int vk)
    {
        switch (vk)
        {
            case VkMiddleMouse: return Loc.Instance["MouseMiddle"];
            case VkMouse4: return Loc.Instance["Mouse4"];
            case VkMouse5: return Loc.Instance["Mouse5"];
        }

        Key key = KeyInterop.KeyFromVirtualKey(vk);

        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "Num " + (key - Key.NumPad0);

        // Several Key values share numbers (e.g. PageDown == Next), so name them explicitly.
        return key switch
        {
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Capital => "Caps Lock",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.Oem3 => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.Oem2 => "/",
            Key.Oem1 => ";",
            Key.Oem7 => "'",
            Key.Oem4 => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Divide => "Num /",
            Key.Decimal => "Num .",
            Key.None => $"VK {vk}",
            _ => key.ToString(),
        };
    }
}
