using System;
using System.Runtime.InteropServices;
using Clicker.Models;
using static Clicker.Services.NativeMethods;

namespace Clicker.Services;

/// <summary>
/// Synthesises mouse and keyboard input with SendInput — the same path a real device driver uses,
/// so DirectInput / Raw Input games receive it too.
/// </summary>
internal static class InputSender
{
    /// <summary>Marker in dwExtraInfo so our own input is recognisable.</summary>
    private static readonly IntPtr Signature = new(0x0C11C4E5);

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>
    /// Moves the cursor to (x, y). Returns false if it was already there.
    /// Uses absolute virtual-desktop coordinates (multi-monitor safe), then fixes rounding with SetCursorPos.
    /// </summary>
    public static bool MoveTo(int x, int y)
    {
        if (GetCursorPos(out POINT current) && current.X == x && current.Y == y)
            return false;

        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        // Normalise to 0..65535. Windows maps back with floor(d * size / 65536), so round up.
        int dx = (int)Math.Ceiling((x - left) * 65536.0 / width);
        int dy = (int)Math.Ceiling((y - top) * 65536.0 / height);

        SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            dx: Math.Clamp(dx, 0, 65535), dy: Math.Clamp(dy, 0, 65535));

        if (!GetCursorPos(out POINT after) || after.X != x || after.Y != y)
            SetCursorPos(x, y);

        return true;
    }

    /// <summary>Presses or releases a mouse button at the current cursor position.</summary>
    public static void MouseButton(PatternMouseButton button, bool down)
    {
        switch (button)
        {
            case PatternMouseButton.Right:
                SendMouse(down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP);
                break;
            case PatternMouseButton.Middle:
                SendMouse(down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP);
                break;
            case PatternMouseButton.X1:
                SendMouse(down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, data: XBUTTON1);
                break;
            case PatternMouseButton.X2:
                SendMouse(down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, data: XBUTTON2);
                break;
            default:
                SendMouse(down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
                break;
        }
    }

    public static void MouseButton(MouseButtonKind button, bool down) =>
        MouseButton(button switch
        {
            MouseButtonKind.Right => PatternMouseButton.Right,
            MouseButtonKind.Middle => PatternMouseButton.Middle,
            _ => PatternMouseButton.Left,
        }, down);

    public static void Wheel(int delta, bool horizontal) =>
        SendMouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, data: unchecked((uint)delta));

    /// <summary>Presses or releases a key. Sent by scan code when known, because games read scan codes.</summary>
    public static void Key(int virtualKey, int scanCode, bool extended, bool down)
    {
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;

        var ki = new KEYBDINPUT { dwExtraInfo = Signature };
        if (scanCode != 0)
        {
            ki.wScan = (ushort)scanCode;
            flags |= KEYEVENTF_SCANCODE;
        }
        else
        {
            ki.wVk = (ushort)virtualKey;
        }

        ki.dwFlags = flags;

        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = ki;
        SendInput(1, inputs, InputSize);
    }

    /// <summary>True if the cursor is currently over a window of this app (main window, tray menu).</summary>
    public static bool IsCursorOverOwnWindow() =>
        GetCursorPos(out POINT point) && IsOwnWindowAt(point.X, point.Y);

    /// <summary>True if the window at (x, y) belongs to this app.</summary>
    public static bool IsOwnWindowAt(int x, int y)
    {
        IntPtr hwnd = WindowFromPoint(new POINT { X = x, Y = y });
        if (hwnd == IntPtr.Zero) return false;

        // The click-through status overlay doesn't count: clicks pass through it.
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0) return false;

        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == OwnProcessId;
    }

    private static void SendMouse(uint flags, int dx = 0, int dy = 0, uint data = 0)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi = new MOUSEINPUT
        {
            dx = dx,
            dy = dy,
            mouseData = data,
            dwFlags = flags,
            dwExtraInfo = Signature,
        };
        SendInput(1, inputs, InputSize);
    }
}
