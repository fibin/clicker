using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Clicker.Models;
using static Clicker.Services.NativeMethods;

namespace Clicker;

/// <summary>
/// Small always-on-top status badge. Normally it is "transparent" for the mouse (WS_EX_TRANSPARENT),
/// so clicks go straight through to the game. In move mode it accepts the mouse and can be dragged.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double ScreenMargin = 20;

    private static readonly Brush StoppedBrush = Frozen(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush ClickingBrush = Frozen(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush PlayingBrush = Frozen(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush RecordingBrush = Frozen(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush NormalBorder = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush MoveBorder = Frozen(Color.FromRgb(0x60, 0xA5, 0xFA));

    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private bool _moveMode;
    private bool _useDefaultPosition = true;
    private bool _blinking;
    private BlinkSpeed _blinkSpeed;

    public OverlayWindow()
    {
        InitializeComponent();

        // Games and other topmost windows can push us down; re-assert "topmost" every couple of seconds.
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => KeepOnTop();

        SizeChanged += (_, _) =>
        {
            if (_useDefaultPosition) PlaceDefault();
            else ClampToScreen();
        };
    }

    /// <summary>Raised after the user dragged the overlay to a new place (WPF units).</summary>
    public event Action<double, double>? Moved;

    /// <summary>In move mode the overlay takes the mouse and can be dragged; otherwise clicks pass through it.</summary>
    public bool MoveMode
    {
        get => _moveMode;
        set
        {
            if (_moveMode == value) return;
            _moveMode = value;

            Pill.BorderBrush = value ? MoveBorder : NormalBorder;
            Pill.BorderThickness = new Thickness(value ? 2 : 1);
            Pill.Cursor = value ? Cursors.SizeAll : null;
            MoveHintText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ApplyWindowStyles();
        }
    }

    public void SetContent(OverlayState state, string title, string detail)
    {
        Dot.Fill = state switch
        {
            OverlayState.Clicking => ClickingBrush,
            OverlayState.Playing => PlayingBrush,
            OverlayState.Recording => RecordingBrush,
            _ => StoppedBrush,
        };

        TitleText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <param name="opacityPercent">10..100</param>
    /// <param name="scalePercent">50..250</param>
    public void SetAppearance(int opacityPercent, int scalePercent)
    {
        Opacity = opacityPercent / 100.0;
        double scale = scalePercent / 100.0;
        Pill.LayoutTransform = new ScaleTransform(scale, scale);
    }

    public void SetBlink(bool blink, BlinkSpeed speed)
    {
        if (blink == _blinking && speed == _blinkSpeed) return;
        _blinking = blink;
        _blinkSpeed = speed;

        if (!blink)
        {
            Pill.BeginAnimation(OpacityProperty, null);
            Pill.Opacity = 1;
            return;
        }

        double halfCycleSeconds = speed switch
        {
            BlinkSpeed.Slow => 0.9,
            BlinkSpeed.Fast => 0.25,
            _ => 0.5,
        };

        // Fade to 35%, not lower: this multiplies with the window opacity setting.
        var animation = new DoubleAnimation(1.0, 0.35, TimeSpan.FromSeconds(halfCycleSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Pill.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>Puts the overlay at a saved place, or at the default one (top-right of the main screen) when null.</summary>
    public void PlaceAt(double? left, double? top)
    {
        if (left is double l && top is double t)
        {
            _useDefaultPosition = false;
            Left = l;
            Top = t;
            ClampToScreen();
        }
        else
        {
            _useDefaultPosition = true;
            PlaceDefault();
        }
    }

    public void ShowOverlay()
    {
        // Called on every status refresh (10x per second) — only do work when it becomes visible.
        if (IsVisible) return;

        Show();
        if (_useDefaultPosition) PlaceDefault();
        _topmostTimer.Start();
        KeepOnTop();
    }

    public void HideOverlay()
    {
        SetBlink(false, _blinkSpeed); // an endless animation would keep WPF rendering while hidden
        _topmostTimer.Stop();
        if (IsVisible) Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyWindowStyles();
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        base.OnClosed(e);
    }

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_moveMode) return;

        double oldLeft = Left;
        double oldTop = Top;
        try
        {
            DragMove(); // returns when the mouse button is released
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // A plain click without moving shouldn't pin the default position.
        if (Math.Abs(Left - oldLeft) < 0.5 && Math.Abs(Top - oldTop) < 0.5) return;

        _useDefaultPosition = false;
        ClampToScreen();
        Moved?.Invoke(Left, Top);
    }

    /// <summary>Tool window (no taskbar / Alt+Tab), never takes focus, and click-through unless moving.</summary>
    private void ApplyWindowStyles()
    {
        if (_hwnd == IntPtr.Zero) return;

        int style = GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_LAYERED;

        // Normal: click-through and never activated. Move mode: takes the mouse so it can be dragged
        // (the system drag loop works reliably only without WS_EX_NOACTIVATE).
        style = _moveMode
            ? style & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE
            : style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(_hwnd, GWL_EXSTYLE, style);
    }

    private void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void PlaceDefault()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Top + ScreenMargin;
    }

    /// <summary>Keeps the overlay fully on the (virtual) screen, e.g. after a monitor was unplugged.</summary>
    private void ClampToScreen()
    {
        double minLeft = SystemParameters.VirtualScreenLeft;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxLeft = Math.Max(minLeft, minLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
        double maxTop = Math.Max(minTop, minTop + SystemParameters.VirtualScreenHeight - ActualHeight);

        Left = Math.Clamp(Left, minLeft, maxLeft);
        Top = Math.Clamp(Top, minTop, maxTop);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
