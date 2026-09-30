using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Clicker.Localization;
using Clicker.Models;
using Clicker.Services;

namespace Clicker;

/// <summary>
/// Main window. Split into partial files by feature:
/// MainWindow.Hotkeys.cs, MainWindow.Points.cs, MainWindow.Patterns.cs.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Brush IdleDotBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    private readonly AppSettings _settings;
    private readonly ClickEngine _engine = new();
    private readonly PatternPlayer _player = new();
    private readonly PatternRecorder _recorder = new();
    private readonly GlobalHotkeyListener _hotkeys = new();
    private readonly TrayIcon _tray = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly ObservableCollection<SavedPoint> _points;
    private readonly ObservableCollection<Pattern> _patterns;
    private readonly bool _isAdmin;

    private bool _initialized;
    private bool _intervalValid = true;
    private bool _exitRequested;
    private bool _trayHintShown;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        VersionText.Text = "v" + AppVersion;
        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        // Saved points
        _points = new ObservableCollection<SavedPoint>(settings.Points);
        PointsList.ItemsSource = _points;
        MoveEachClickCheck.IsChecked = settings.MoveBeforeEachClick;

        // Patterns
        _patterns = new ObservableCollection<Pattern>(PatternStore.Load());
        PatternsList.ItemsSource = _patterns;
        RecordKeyboardCheck.IsChecked = settings.RecordKeyboard;
        RepeatsBox.Text = settings.PatternRepeatCount.ToString(CultureInfo.InvariantCulture);
        LoopDelayBox.Text = settings.PatternLoopDelayMs.ToString(CultureInfo.InvariantCulture);
        _player.Finished += () => Dispatcher.BeginInvoke(new Action(OnPlaybackFinished));

        // Clicker engine
        _engine.IntervalMs = settings.IntervalMs;
        _engine.Button = settings.Button;
        _engine.MoveBeforeEachClick = settings.MoveBeforeEachClick;

        // Initial control values
        IntervalBox.Text = settings.IntervalMs.ToString(CultureInfo.InvariantCulture);
        SelectByTag(ButtonCombo, settings.Button.ToString());
        SelectByTag(LanguageCombo, Loc.Instance.Language);

        // Status overlay
        InitOverlay();

        // Tray
        _tray.ShowRequested += ShowFromTray;
        _tray.ToggleRequested += TrayToggle;
        _tray.ExitRequested += ExitApplication;

        // Global hotkeys (events arrive on the hook thread -> marshal to the UI thread)
        _hotkeys.SetBinding(HotkeyAction.Toggle, settings.Hotkey);
        _hotkeys.SetBinding(HotkeyAction.SavePoint, settings.RecordHotkey);
        _hotkeys.SetBinding(HotkeyAction.RecordPattern, settings.PatternHotkey);
        _hotkeys.Recorder = _recorder;
        _hotkeys.Pressed += (action, position) =>
            Dispatcher.BeginInvoke(new Action(() => OnHotkeyPressed(action, position)));
        try
        {
            _hotkeys.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.Format("HookError", ex.Message), Loc.Instance["AppTitle"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Live status refresh (click counter, recording time, loop number)
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _statusTimer.Tick += (_, _) => UpdateStatusDetails();

        Loc.Instance.LanguageChanged += RefreshTexts;

        _initialized = true;
        RefreshTexts();
    }

    // ------------------------------------------------------------------ Start / stop

    private bool IsRunning => _engine.IsRunning || _player.IsRunning;

    /// <summary>Start/stop hotkey, Start button and tray menu: plays the active pattern if there is one, otherwise auto-clicks.</summary>
    private void ToggleRunning()
    {
        if (_recorder.IsRecording) return;

        if (IsRunning)
        {
            StopAll();
        }
        else
        {
            Pattern? pattern = ActivePattern;
            if (pattern != null)
                StartPattern(pattern);
            else if (_intervalValid)
                _engine.Start();
        }

        UpdateRunningState();
    }

    /// <summary>Tray menu Start/Stop item: also ends a recording in progress.</summary>
    private void TrayToggle()
    {
        if (_recorder.IsRecording)
            FinishRecording();
        else
            ToggleRunning();
    }

    private void StopAll()
    {
        _engine.Stop();
        _player.Stop();
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e) => ToggleRunning();

    /// <summary>Refreshes everything that depends on idle / clicking / playing / recording.</summary>
    private void UpdateRunningState()
    {
        Loc loc = Loc.Instance;
        bool recording = _recorder.IsRecording;
        bool running = IsRunning;

        string actionText = loc[running ? "Stop" : "Start"];
        StartStopButton.Content = actionText + "   (" + _settings.Hotkey.ToDisplayString() + ")";
        StartStopButton.Background = (Brush)FindResource(running ? "StopBrush" : "StartBrush");
        StartStopButton.IsEnabled = !recording && (running || CanStart());

        if (recording)
        {
            StatusDot.Fill = (Brush)FindResource("StopBrush");
            StatusText.Text = loc["StatusRecording"];
        }
        else if (running)
        {
            StatusDot.Fill = (Brush)FindResource("StartBrush");
            StatusText.Text = loc[_player.IsRunning ? "StatusPlaying" : "StatusRunning"];
        }
        else
        {
            StatusDot.Fill = IdleDotBrush;
            StatusText.Text = loc["StatusStopped"];
        }

        RecordPatternButton.Content = loc[recording ? "StopRecording" : "RecordPattern"]
                                      + "   (" + _settings.PatternHotkey.ToDisplayString() + ")";
        RecordPatternButton.Background = (Brush)FindResource(recording ? "StopBrush" : "AccentBrush");

        if (recording || running)
            _statusTimer.Start();
        else
            _statusTimer.Stop();

        UpdateStatusDetails();
        UpdateTargetText();

        _tray.Update(
            running || recording,
            loc.Format("TrayTooltip", StatusText.Text),
            loc["TrayShow"],
            recording ? loc["StopRecording"] : actionText,
            loc["TrayExit"]);
    }

    private bool CanStart() => ActivePattern != null ? _patternNumbersValid : _intervalValid;

    /// <summary>Right side of the status line: clicks, loop number or recording time.</summary>
    private void UpdateStatusDetails()
    {
        Loc loc = Loc.Instance;

        if (_recorder.IsRecording)
        {
            ClickCountText.Text = loc.Format("RecordingProgress",
                _recorder.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture), _recorder.EventCount);
        }
        else if (_player.IsRunning)
        {
            int current = _player.LoopsDone + 1;
            ClickCountText.Text = _player.TotalLoops == 0
                ? loc.Format("LoopProgressEndless", current)
                : loc.Format("LoopProgress", Math.Min(current, _player.TotalLoops), _player.TotalLoops);
        }
        else if (_engine.IsRunning || ActivePattern == null)
        {
            ClickCountText.Text = loc.Format("ClickCount", _engine.ClickCount.ToString("N0", CultureInfo.CurrentCulture));
        }
        else
        {
            ClickCountText.Text = "";
        }

        UpdateOverlay();
    }

    /// <summary>Second status line: what the next start will do.</summary>
    private void UpdateTargetText()
    {
        Pattern? pattern = ActivePattern;
        SavedPoint? point = _points.FirstOrDefault(p => p.IsActive);

        TargetText.Text = pattern != null
            ? Loc.Instance.Format("TargetPattern", pattern.Name)
            : point != null
                ? Loc.Instance.Format("TargetPoint", point.Name, point.CoordinatesText)
                : Loc.Instance["TargetCursor"];
    }

    // ------------------------------------------------------------------ Interval

    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void IntervalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        ValidateInterval();
    }

    private void IntervalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_intervalValid)
            SaveSettings();
    }

    private void ValidateInterval()
    {
        _intervalValid = TryParseNumber(IntervalBox.Text, AppSettings.MinIntervalMs, AppSettings.MaxIntervalMs, out int value);

        if (_intervalValid)
        {
            // Applies immediately, even while clicking.
            _settings.IntervalMs = value;
            _engine.IntervalMs = value;

            double cps = 1000.0 / value;
            CpsText.Text = Loc.Instance.Format("ClicksPerSecond", cps.ToString(cps >= 10 ? "0" : "0.##", CultureInfo.CurrentCulture));
            IntervalError.Visibility = Visibility.Collapsed;
            IntervalBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            CpsText.Text = "";
            IntervalError.Text = Loc.Instance.Format("InvalidInterval", AppSettings.MinIntervalMs, AppSettings.MaxIntervalMs);
            IntervalError.Visibility = Visibility.Visible;
            IntervalBox.BorderBrush = (Brush)FindResource("ErrorBrush");
        }

        StartStopButton.IsEnabled = !_recorder.IsRecording && (IsRunning || CanStart());
    }

    // ------------------------------------------------------------------ Mouse button

    private void ButtonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ButtonCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (!Enum.TryParse(tag, out MouseButtonKind button)) return;

        _settings.Button = button;
        _engine.Button = button;
        SaveSettings();
    }

    // ------------------------------------------------------------------ Language

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language }) return;

        _settings.Language = language;
        SaveSettings();
        Loc.Instance.SetLanguage(language); // raises LanguageChanged -> RefreshTexts
    }

    /// <summary>Updates texts that are built in code (XAML texts update by themselves through {loc:Tr}).</summary>
    private void RefreshTexts()
    {
        // A closed ComboBox caches the selected item's text — reselect to pick up the new language.
        bool wasInitialized = _initialized;
        _initialized = false;
        foreach (ComboBox combo in new[] { ButtonCombo, BlinkSpeedCombo })
        {
            int selected = combo.SelectedIndex;
            combo.SelectedIndex = -1;
            combo.SelectedIndex = selected;
        }

        _initialized = wasInitialized;
        MoveOverlayButton.Content = Loc.Instance[_overlayMoveMode ? "MoveOverlayDone" : "MoveOverlay"];

        AdminPanel.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;
        AdminStatusText.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;

        foreach (Pattern pattern in _patterns)
            pattern.RefreshInfo();

        ValidateInterval();
        ValidatePatternNumbers();
        RefreshHotkeyUi();
        RefreshPointsUi();
        RefreshPatternsUi();
        ApplyPointTarget();
        UpdateRunningState();
    }

    // ------------------------------------------------------------------ Shared list editing

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter / Esc finish editing a point or pattern name.
        if (e.Key is Key.Enter or Key.Escape)
        {
            Keyboard.ClearFocus();
            Focus();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ Tray / closing

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        // Bring to front even if another app is in the foreground.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private bool IsHiddenOrMinimized => !IsVisible || WindowState == WindowState.Minimized;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            // The X button hides the window to the tray; the app keeps running.
            e.Cancel = true;
            if (_capturing != null) EndHotkeyCapture(null);
            if (_overlayMoveMode) SetOverlayMoveMode(false);
            Hide();

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloon(Loc.Instance["AppTitle"],
                    Loc.Instance.Format("TrayHint", _settings.Hotkey.ToDisplayString()));
            }

            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.LanguageChanged -= RefreshTexts;
        _statusTimer.Stop();
        if (_recorder.IsRecording) FinishRecording(); // don't lose a recording on exit
        _engine.Dispose();
        _player.Dispose();
        _hotkeys.Dispose();
        _tray.Dispose();
        _overlay.Close();
        SaveSettings();
        SavePatterns();
        base.OnClosed(e);
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    // ------------------------------------------------------------------ Administrator

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        SavePatterns();

        string? exePath = Environment.ProcessPath;
        if (exePath == null) return;

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = App.ElevatedRestartArg,
            });
        }
        catch (Win32Exception)
        {
            return; // user pressed "No" in the UAC prompt
        }

        ExitApplication();
    }

    // ------------------------------------------------------------------ Helpers

    private void SaveSettings()
    {
        _settings.Points = _points.ToList();
        SettingsStore.Save(_settings);
    }

    /// <summary>"1.2.0" (or "1.0.0-dev.15" for CI test builds), without the "+commit" build metadata.</summary>
    private static string AppVersion
    {
        get
        {
            string? version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(version)) return "?";

            int plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    private static bool TryParseNumber(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem { Tag: string t } comboItem && t == tag)
            {
                combo.SelectedItem = comboItem;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }
}
