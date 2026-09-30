using System;
using System.Diagnostics;
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
    /// <summary>Upper bound for how long the button is held down in one click.</summary>
    private const int MaxHoldMs = 30;

    /// <summary>Upper bound for the pause between moving the cursor and clicking (lets games register the hover).</summary>
    private const int MaxSettleMs = 15;

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
                    if (InputSender.MoveTo(target.X, target.Y))
                    {
                        // The cursor actually jumped: give the game a moment to notice the hover.
                        int settleMs = Math.Min(interval / 2, MaxSettleMs);
                        if (settleMs > 0)
                            WaitUntil(clock, clock.Elapsed.TotalMilliseconds + settleMs, abortOnStop: true);
                        if (!_running) break;
                    }
                }

                // Never click on our own window: starting with the mouse over "Start" would press "Stop".
                if (!InputSender.IsCursorOverOwnWindow())
                {
                    // Hold the button a little: many games read button state once per frame and
                    // miss a click whose down and up events arrive at the same instant.
                    int holdMs = Math.Min(interval / 2, MaxHoldMs);
                    InputSender.MouseButton(button, down: true);
                    if (holdMs > 0)
                        WaitUntil(clock, clock.Elapsed.TotalMilliseconds + holdMs, abortOnStop: false);
                    InputSender.MouseButton(button, down: false);
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
}
