using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Clicker.Models;
using static Clicker.Services.NativeMethods;

namespace Clicker.Services;

/// <summary>
/// Sends mouse clicks on a dedicated high-priority thread.
/// Uses SendInput (hardware-level input injection), which DirectInput / Raw Input games also receive.
/// </summary>
public sealed class ClickEngine : IDisposable
{
    /// <summary>Marker in dwExtraInfo so our own input is recognisable.</summary>
    private static readonly IntPtr ClickerSignature = new(0x0C11C4E5);

    /// <summary>Upper bound for how long the button is held down in one click.</summary>
    private const int MaxHoldMs = 30;

    /// <summary>Upper bound for the pause between moving the cursor and clicking (lets games register the hover).</summary>
    private const int MaxSettleMs = 15;

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private volatile bool _running;
    private volatile int _intervalMs = 100;
    private volatile MouseButtonKind _button = MouseButtonKind.Left;
    private volatile ScreenPoint? _target;
    private volatile bool _moveBeforeEachClick = true;
    private int _pendingMove; // 1 = move the cursor to the target before the next click
    private Thread? _thread;
    private long _clickCount;

    public bool IsRunning => _running;

    public long ClickCount => Interlocked.Read(ref _clickCount);

    /// <summary>Interval between clicks. Can be changed while running.</summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set => _intervalMs = Math.Max(1, value);
    }

    /// <summary>Mouse button to click. Can be changed while running.</summary>
    public MouseButtonKind Button
    {
        get => _button;
        set => _button = value;
    }

    /// <summary>
    /// Point to click at. Null = click wherever the cursor is.
    /// When set, the cursor is moved there on start (and right away if already running).
    /// </summary>
    public ScreenPoint? Target
    {
        get => _target;
        set
        {
            _target = value;
            Volatile.Write(ref _pendingMove, 1);
        }
    }

    /// <summary>If true, the cursor is put back on <see cref="Target"/> before every click.</summary>
    public bool MoveBeforeEachClick
    {
        get => _moveBeforeEachClick;
        set => _moveBeforeEachClick = value;
    }

    public void Start()
    {
        if (_running) return;

        Interlocked.Exchange(ref _clickCount, 0);
        Volatile.Write(ref _pendingMove, 1);
        _running = true;
        _thread = new Thread(ClickLoop)
        {
            IsBackground = true,
            Name = "Clicker",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running) return;

        _running = false;
        _thread?.Join(1000);
        _thread = null;
    }

    public void Dispose() => Stop();

    private void ClickLoop()
    {
        // Raise the system timer resolution to 1 ms so short intervals are accurate.
        timeBeginPeriod(1);
        try
        {
            var clock = Stopwatch.StartNew();
            double nextClickAt = 0;

            while (_running)
            {
                int interval = _intervalMs;
                MouseButtonKind button = _button;

                // Read the flag before the target, so a point switched right now is never lost.
                bool pendingMove = Interlocked.Exchange(ref _pendingMove, 0) == 1;
                ScreenPoint? target = _target;
                if (target != null && (_moveBeforeEachClick || pendingMove))
                {
                    if (MoveCursor(target))
                    {
                        // The cursor actually jumped: give the game a moment to notice the hover.
                        int settleMs = Math.Min(interval / 2, MaxSettleMs);
                        if (settleMs > 0)
                            WaitUntil(clock, clock.Elapsed.TotalMilliseconds + settleMs, abortOnStop: true);
                        if (!_running) break;
                    }
                }

                if (!IsCursorOverOwnWindow())
                {
                    // Hold the button a little: many games read button state once per frame and
                    // miss a click whose down and up events arrive at the same instant.
                    int holdMs = Math.Min(interval / 2, MaxHoldMs);
                    SendButton(button, down: true);
                    if (holdMs > 0)
                        WaitUntil(clock, clock.Elapsed.TotalMilliseconds + holdMs, abortOnStop: false);
                    SendButton(button, down: false);
                    Interlocked.Increment(ref _clickCount);
                }

                nextClickAt += interval;
                double now = clock.Elapsed.TotalMilliseconds;
                if (now > nextClickAt + interval)
                    nextClickAt = now; // we fell behind (PC was busy) — don't fire a burst to catch up

                WaitUntil(clock, nextClickAt, abortOnStop: true);
            }
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    /// <summary>Sleeps most of the time and spins only for the last millisecond, for precise timing without burning CPU.</summary>
    private void WaitUntil(Stopwatch clock, double targetMs, bool abortOnStop)
    {
        while (true)
        {
            if (abortOnStop && !_running) return;

            double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0) return;

            if (remaining > 2)
                Thread.Sleep((int)Math.Min(remaining - 1, 50)); // max 50 ms so Stop() reacts quickly
            else if (remaining > 0.5)
                Thread.Sleep(0);
            else
                Thread.SpinWait(20);
        }
    }

    /// <summary>
    /// Moves the cursor to <paramref name="target"/>. Returns false if it was already there.
    /// Uses SendInput with absolute virtual-desktop coordinates so games see a real mouse move
    /// (works across multiple monitors), then corrects any rounding error with SetCursorPos.
    /// </summary>
    private static bool MoveCursor(ScreenPoint target)
    {
        if (GetCursorPos(out POINT current) && current.X == target.X && current.Y == target.Y)
            return false;

        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        // Normalise to 0..65535 across the whole virtual desktop.
        // Windows maps back with floor(d * size / 65536), so round up to land on the exact pixel.
        int dx = (int)Math.Ceiling((target.X - left) * 65536.0 / width);
        int dy = (int)Math.Ceiling((target.Y - top) * 65536.0 / height);

        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi = new MOUSEINPUT
        {
            dx = Math.Clamp(dx, 0, 65535),
            dy = Math.Clamp(dy, 0, 65535),
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            dwExtraInfo = ClickerSignature,
        };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());

        if (!GetCursorPos(out POINT after) || after.X != target.X || after.Y != target.Y)
            SetCursorPos(target.X, target.Y);

        return true;
    }

    /// <summary>
    /// Never click on the clicker's own windows (main window, tray menu):
    /// otherwise starting with the mouse over the Start button would immediately press Stop.
    /// </summary>
    private bool IsCursorOverOwnWindow()
    {
        if (!GetCursorPos(out POINT point)) return false;

        IntPtr hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == _ownProcessId;
    }

    private static void SendButton(MouseButtonKind button, bool down)
    {
        uint flags = (button, down) switch
        {
            (MouseButtonKind.Right, true) => MOUSEEVENTF_RIGHTDOWN,
            (MouseButtonKind.Right, false) => MOUSEEVENTF_RIGHTUP,
            (MouseButtonKind.Middle, true) => MOUSEEVENTF_MIDDLEDOWN,
            (MouseButtonKind.Middle, false) => MOUSEEVENTF_MIDDLEUP,
            (_, true) => MOUSEEVENTF_LEFTDOWN,
            (_, false) => MOUSEEVENTF_LEFTUP,
        };

        // No MOUSEEVENTF_MOVE: the click happens at the current cursor position.
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi = new MOUSEINPUT
        {
            dwFlags = flags,
            dwExtraInfo = ClickerSignature,
        };

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }
}
