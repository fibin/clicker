using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Clicker.Models;
using static Clicker.Services.NativeMethods;

namespace Clicker.Services;

/// <summary>
/// Replays a recorded pattern on a dedicated thread with the original timing, in a loop.
/// Anything still pressed when a loop ends or playback is stopped is released,
/// so a stopped pattern never leaves a key or mouse button stuck down.
/// </summary>
public sealed class PatternPlayer : IDisposable
{
    private volatile bool _running;
    private Thread? _thread;
    private int _loopsDone;

    public bool IsRunning => _running;

    /// <summary>Completed loops in the current run.</summary>
    public int LoopsDone => Volatile.Read(ref _loopsDone);

    /// <summary>Loops requested for the current run; 0 = endless.</summary>
    public int TotalLoops { get; private set; }

    /// <summary>Raised on the player thread when playback ends by itself (all repeats done).</summary>
    public event Action? Finished;

    /// <param name="events">Events to play, sorted by time.</param>
    /// <param name="repeatCount">How many times to play; 0 = until stopped.</param>
    /// <param name="loopDelayMs">Pause between repeats.</param>
    public void Start(IReadOnlyList<PatternEvent> events, int repeatCount, int loopDelayMs)
    {
        if (_running || events.Count == 0) return;

        TotalLoops = Math.Max(0, repeatCount);
        Interlocked.Exchange(ref _loopsDone, 0);
        _running = true;

        var snapshot = new List<PatternEvent>(events);
        int delay = Math.Max(0, loopDelayMs);
        _thread = new Thread(() => Run(snapshot, TotalLoops, delay))
        {
            IsBackground = true,
            Name = "PatternPlayer",
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

    private void Run(List<PatternEvent> events, int repeatCount, int loopDelayMs)
    {
        var heldButtons = new HashSet<PatternMouseButton>();
        var heldKeys = new Dictionary<int, PatternEvent>(); // vk -> the KeyDown event (for scan code / extended flag)
        bool finishedNaturally = false;

        timeBeginPeriod(1);
        try
        {
            for (int loop = 0; repeatCount == 0 || loop < repeatCount; loop++)
            {
                if (loop > 0 && loopDelayMs > 0)
                {
                    var pause = Stopwatch.StartNew();
                    WaitUntil(pause, loopDelayMs);
                }

                if (!_running) break;

                var clock = Stopwatch.StartNew();
                foreach (PatternEvent e in events)
                {
                    WaitUntil(clock, e.Time);
                    if (!_running) break;
                    Play(e, heldButtons, heldKeys);
                }

                ReleaseAll(heldButtons, heldKeys);
                if (!_running) break;

                Interlocked.Increment(ref _loopsDone);

                // A pattern with no duration and no pause would otherwise spin a CPU core and flood the input queue.
                if (loopDelayMs == 0 && events[^1].Time == 0)
                    Thread.Sleep(1);
            }

            finishedNaturally = _running;
        }
        finally
        {
            ReleaseAll(heldButtons, heldKeys);
            timeEndPeriod(1);
            _running = false;
        }

        if (finishedNaturally)
            Finished?.Invoke();
    }

    private static void Play(PatternEvent e, HashSet<PatternMouseButton> heldButtons, Dictionary<int, PatternEvent> heldKeys)
    {
        switch (e.Kind)
        {
            case PatternEventKind.Move:
                InputSender.MoveTo(e.X, e.Y);
                break;

            case PatternEventKind.MouseDown:
                // Never click on the clicker's own window (it would press its buttons).
                if (InputSender.IsOwnWindowAt(e.X, e.Y)) break;
                InputSender.MoveTo(e.X, e.Y);
                InputSender.MouseButton(e.Button, down: true);
                heldButtons.Add(e.Button);
                break;

            case PatternEventKind.MouseUp:
                // Only release what this pattern pressed (the press may have been skipped above).
                if (!heldButtons.Remove(e.Button)) break;
                InputSender.MoveTo(e.X, e.Y);
                InputSender.MouseButton(e.Button, down: false);
                break;

            case PatternEventKind.Wheel:
            case PatternEventKind.HorizontalWheel:
                if (InputSender.IsOwnWindowAt(e.X, e.Y)) break;
                InputSender.MoveTo(e.X, e.Y);
                InputSender.Wheel(e.Delta, horizontal: e.Kind == PatternEventKind.HorizontalWheel);
                break;

            case PatternEventKind.KeyDown:
                InputSender.Key(e.VirtualKey, e.ScanCode, e.Extended, down: true);
                heldKeys[e.VirtualKey] = e;
                break;

            case PatternEventKind.KeyUp:
                // A key that was already held when recording started has no KeyDown — don't release it.
                if (!heldKeys.Remove(e.VirtualKey)) break;
                InputSender.Key(e.VirtualKey, e.ScanCode, e.Extended, down: false);
                break;
        }
    }

    private static void ReleaseAll(HashSet<PatternMouseButton> heldButtons, Dictionary<int, PatternEvent> heldKeys)
    {
        foreach (PatternMouseButton button in heldButtons)
            InputSender.MouseButton(button, down: false);
        heldButtons.Clear();

        foreach (PatternEvent key in heldKeys.Values)
            InputSender.Key(key.VirtualKey, key.ScanCode, key.Extended, down: false);
        heldKeys.Clear();
    }

    /// <summary>Precise wait that returns early when playback is stopped.</summary>
    private void WaitUntil(Stopwatch clock, double targetMs)
    {
        while (_running)
        {
            double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0) return;

            if (remaining > 2)
                Thread.Sleep((int)Math.Min(remaining - 1, 50));
            else if (remaining > 0.5)
                Thread.Sleep(0);
            else
                Thread.SpinWait(20);
        }
    }
}
