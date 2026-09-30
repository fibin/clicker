using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Clicker.Models;
using static Clicker.Services.NativeMethods;

namespace Clicker.Services;

/// <summary>
/// Global hotkey via low-level keyboard and mouse hooks (WH_KEYBOARD_LL / WH_MOUSE_LL).
/// Unlike RegisterHotKey this also works while a fullscreen game has focus and supports
/// the side mouse buttons. The hooks live on their own thread, so a busy UI never delays input.
/// </summary>
public sealed class GlobalHotkeyListener : IDisposable
{
    // Keep delegates in fields so the GC doesn't collect them while Windows still calls them.
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    private volatile HotkeyBinding? _binding;
    private volatile bool _suspended;
    private bool _keyIsDown;

    public GlobalHotkeyListener()
    {
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    /// <summary>Raised on the hook thread. Handlers must return quickly (marshal to the UI thread).</summary>
    public event Action? Pressed;

    public HotkeyBinding? Binding
    {
        get => _binding;
        set { _binding = value; _keyIsDown = false; }
    }

    /// <summary>While true, key presses are ignored (used while the user records a new hotkey).</summary>
    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    public void Start()
    {
        if (_thread != null) return;

        using var ready = new ManualResetEventSlim(false);
        Exception? error = null;

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            IntPtr module = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);

            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error());
                Unhook();
                ready.Set();
                return;
            }

            ready.Set();

            // Low-level hooks are called while this thread is pumping messages.
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            Unhook();
        })
        {
            IsBackground = true,
            Name = "GlobalHotkey",
            Priority = ThreadPriority.AboveNormal,
        };

        _thread.Start();
        ready.Wait();

        if (error != null)
        {
            _thread = null;
            throw error;
        }
    }

    public void Dispose()
    {
        if (_thread == null) return;

        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    private void Unhook()
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                int msg = (int)wParam;
                bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool up = msg is WM_KEYUP or WM_SYSKEYUP;
                HandleKey((int)data.vkCode, down, up);
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            // Fast path: mouse moves arrive constantly, ignore everything but the buttons we support.
            if (msg is WM_XBUTTONDOWN or WM_XBUTTONUP or WM_MBUTTONDOWN or WM_MBUTTONUP)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((data.flags & LLMHF_INJECTED) == 0)
                {
                    int vk = msg is WM_MBUTTONDOWN or WM_MBUTTONUP
                        ? VK_MBUTTON
                        : ((data.mouseData >> 16) & 0xFFFF) == 1 ? VK_XBUTTON1 : VK_XBUTTON2;

                    HandleKey(vk, down: msg is WM_XBUTTONDOWN or WM_MBUTTONDOWN, up: msg is WM_XBUTTONUP or WM_MBUTTONUP);
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleKey(int vk, bool down, bool up)
    {
        HotkeyBinding? binding = _binding;
        if (binding == null || vk != binding.VirtualKey) return;

        if (up)
        {
            _keyIsDown = false;
            return;
        }

        // Ignore auto-repeat while the key is held.
        if (!down || _keyIsDown) return;
        _keyIsDown = true;

        if (_suspended || !ModifiersPressed(binding)) return;

        try
        {
            Pressed?.Invoke();
        }
        catch
        {
            // Never let an exception escape into the hook chain.
        }
    }

    /// <summary>
    /// Required modifiers must be held. Extra modifiers are allowed — in games you often hold Shift/Ctrl
    /// while pressing the hotkey.
    /// </summary>
    private static bool ModifiersPressed(HotkeyBinding binding)
    {
        return (!binding.Ctrl || IsDown(VK_CONTROL))
            && (!binding.Alt || IsDown(VK_MENU))
            && (!binding.Shift || IsDown(VK_SHIFT))
            && (!binding.Win || IsDown(VK_LWIN) || IsDown(VK_RWIN));
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
