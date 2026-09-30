using System;
using System.Collections.Generic;
using System.Diagnostics;
using Clicker.Models;
using static Clicker.Services.NativeMethods;

namespace Clicker.Services;

/// <summary>
/// Collects mouse and keyboard events while a pattern is being recorded.
/// Fed from the low-level hook thread, so every method here must be quick.
/// </summary>
public sealed class PatternRecorder
{
    /// <summary>Mouse moves closer together than this are merged (a 1000 Hz mouse would otherwise flood the list).</summary>
    private const int MoveMergeMs = 8;

    /// <summary>Safety cap so a forgotten recording can't eat all the memory.</summary>
    private const int MaxEvents = 500_000;

    private readonly object _lock = new();
    private readonly Stopwatch _clock = new();
    private readonly bool[] _skipButtonUntilUp = new bool[5];
    private List<PatternEvent> _events = new();
    private volatile bool _isRecording;
    private volatile bool _recordKeyboard = true;

    public bool IsRecording => _isRecording;

    public TimeSpan Elapsed => _clock.Elapsed;

    public int EventCount
    {
        get
        {
            lock (_lock) return _events.Count;
        }
    }

    public void Start(bool recordKeyboard)
    {
        lock (_lock)
        {
            _events = new List<PatternEvent>();
            Array.Clear(_skipButtonUntilUp);
        }

        _recordKeyboard = recordKeyboard;
        _clock.Restart();
        _isRecording = true;
    }

    /// <summary>Stops recording and returns the events, shifted so the first one happens at 0 ms.</summary>
    public List<PatternEvent> Stop()
    {
        _isRecording = false;
        _clock.Stop();

        List<PatternEvent> events;
        lock (_lock)
        {
            events = _events;
            _events = new List<PatternEvent>();
        }

        // A modifier pressed as part of the stop hotkey (e.g. Ctrl of Ctrl+F8) is not part of the pattern.
        while (events.Count > 0 && events[^1].Kind == PatternEventKind.KeyDown && IsModifier(events[^1].VirtualKey))
            events.RemoveAt(events.Count - 1);

        if (events.Count == 0) return events;

        // Drop the idle time before the first action.
        int offset = events[0].Time;
        if (offset > 0)
        {
            for (int i = 0; i < events.Count; i++)
                events[i] = events[i] with { Time = events[i].Time - offset };
        }

        return events;
    }

    /// <summary>Called from the mouse hook for every non-injected mouse message.</summary>
    internal void OnMouse(int message, in MSLLHOOKSTRUCT data)
    {
        if (!_isRecording) return;

        int time = (int)_clock.ElapsedMilliseconds;
        int x = data.pt.X;
        int y = data.pt.Y;

        switch (message)
        {
            case WM_MOUSEMOVE:
                AddMove(time, x, y);
                break;

            case WM_LBUTTONDOWN:
                AddButton(time, x, y, PatternMouseButton.Left, down: true);
                break;
            case WM_LBUTTONUP:
                AddButton(time, x, y, PatternMouseButton.Left, down: false);
                break;
            case WM_RBUTTONDOWN:
                AddButton(time, x, y, PatternMouseButton.Right, down: true);
                break;
            case WM_RBUTTONUP:
                AddButton(time, x, y, PatternMouseButton.Right, down: false);
                break;
            case WM_MBUTTONDOWN:
                AddButton(time, x, y, PatternMouseButton.Middle, down: true);
                break;
            case WM_MBUTTONUP:
                AddButton(time, x, y, PatternMouseButton.Middle, down: false);
                break;
            case WM_XBUTTONDOWN:
            case WM_XBUTTONUP:
                PatternMouseButton xButton = ((data.mouseData >> 16) & 0xFFFF) == 1 ? PatternMouseButton.X1 : PatternMouseButton.X2;
                AddButton(time, x, y, xButton, down: message == WM_XBUTTONDOWN);
                break;

            case WM_MOUSEWHEEL:
            case WM_MOUSEHWHEEL:
                if (InputSender.IsOwnWindowAt(x, y)) break; // scrolling the clicker's own window
                Add(new PatternEvent
                {
                    Time = time,
                    Kind = message == WM_MOUSEWHEEL ? PatternEventKind.Wheel : PatternEventKind.HorizontalWheel,
                    X = x,
                    Y = y,
                    Delta = (short)((data.mouseData >> 16) & 0xFFFF),
                });
                break;
        }
    }

    /// <summary>Called from the keyboard hook for non-injected key messages that are not hotkeys.</summary>
    internal void OnKey(bool down, in KBDLLHOOKSTRUCT data)
    {
        if (!_isRecording || !_recordKeyboard) return;

        Add(new PatternEvent
        {
            Time = (int)_clock.ElapsedMilliseconds,
            Kind = down ? PatternEventKind.KeyDown : PatternEventKind.KeyUp,
            VirtualKey = (int)data.vkCode,
            ScanCode = (int)data.scanCode,
            Extended = (data.flags & LLKHF_EXTENDED) != 0,
        });
    }

    private static bool IsModifier(int vk) =>
        vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN or (>= 0xA0 and <= 0xA5);

    private void AddMove(int time, int x, int y)
    {
        var move = new PatternEvent { Time = time, Kind = PatternEventKind.Move, X = x, Y = y };

        lock (_lock)
        {
            int last = _events.Count - 1;
            if (last >= 0 && _events[last].Kind == PatternEventKind.Move && time - _events[last].Time < MoveMergeMs)
            {
                // Keep the time of the first move in the burst, the position of the latest.
                _events[last] = _events[last] with { X = x, Y = y };
                return;
            }

            if (_events.Count < MaxEvents)
                _events.Add(move);
        }
    }

    private void AddButton(int time, int x, int y, PatternMouseButton button, bool down)
    {
        int index = (int)button;

        // Clicks on the clicker's own window (e.g. the "Stop recording" button) are not part of the pattern.
        if (down && InputSender.IsOwnWindowAt(x, y))
        {
            _skipButtonUntilUp[index] = true;
            return;
        }

        if (!down && _skipButtonUntilUp[index])
        {
            _skipButtonUntilUp[index] = false;
            return;
        }

        Add(new PatternEvent
        {
            Time = time,
            Kind = down ? PatternEventKind.MouseDown : PatternEventKind.MouseUp,
            X = x,
            Y = y,
            Button = button,
        });
    }

    private void Add(PatternEvent e)
    {
        lock (_lock)
        {
            if (_events.Count < MaxEvents)
                _events.Add(e);
        }
    }
}
