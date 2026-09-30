using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Clicker.Localization;
using Clicker.Models;
using Clicker.Services;

namespace Clicker;

public partial class MainWindow : Window
{
    private enum HotkeyKind
    {
        None,
        Toggle,
        Record,
    }

    private readonly AppSettings _settings;
    private readonly ClickEngine _engine = new();
    private readonly GlobalHotkeyListener _hotkeys = new();
    private readonly TrayIcon _tray = new();
    private readonly DispatcherTimer _counterTimer;
    private readonly ObservableCollection<SavedPoint> _points;
    private readonly bool _isAdmin;

    private bool _initialized;
    private HotkeyKind _capturing = HotkeyKind.None;
    private bool _intervalValid = true;
    private bool _exitRequested;
    private bool _trayHintShown;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        // Saved points
        _points = new ObservableCollection<SavedPoint>(settings.Points);
        PointsList.ItemsSource = _points;
        MoveEachClickCheck.IsChecked = settings.MoveBeforeEachClick;

        // Clicker engine
        _engine.IntervalMs = settings.IntervalMs;
        _engine.Button = settings.Button;
        _engine.MoveBeforeEachClick = settings.MoveBeforeEachClick;

        // Initial control values
        IntervalBox.Text = settings.IntervalMs.ToString(CultureInfo.InvariantCulture);
        SelectByTag(ButtonCombo, settings.Button.ToString());
        SelectByTag(LanguageCombo, Loc.Instance.Language);

        // Tray
        _tray.ShowRequested += ShowFromTray;
        _tray.ToggleRequested += ToggleClicking;
        _tray.ExitRequested += ExitApplication;

        // Global hotkeys (events arrive on the hook thread -> marshal to the UI thread)
        _hotkeys.ToggleBinding = settings.Hotkey;
        _hotkeys.RecordBinding = settings.RecordHotkey;
        _hotkeys.TogglePressed += () => Dispatcher.BeginInvoke(new Action(ToggleClicking));
        _hotkeys.RecordPressed += position => Dispatcher.BeginInvoke(new Action(() => AddPoint(position)));
        try
        {
            _hotkeys.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.Format("HookError", ex.Message), Loc.Instance["AppTitle"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Click counter refresh (the engine can click 1000 times/s — don't update the UI per click)
        _counterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _counterTimer.Tick += (_, _) => UpdateClickCount();

        Loc.Instance.LanguageChanged += RefreshTexts;

        _initialized = true;
        ApplyTarget();
        RefreshTexts();
    }

    // ------------------------------------------------------------------ Start / stop

    private void ToggleClicking()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            _counterTimer.Stop();
        }
        else
        {
            if (!_intervalValid) return;
            _engine.Start();
            _counterTimer.Start();
        }

        UpdateRunningState();
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e) => ToggleClicking();

    private void UpdateRunningState()
    {
        bool running = _engine.IsRunning;
        Loc loc = Loc.Instance;

        string actionText = loc[running ? "Stop" : "Start"];
        StartStopButton.Content = actionText + "   (" + _settings.Hotkey.ToDisplayString() + ")";
        StartStopButton.Background = (Brush)FindResource(running ? "StopBrush" : "StartBrush");
        StartStopButton.IsEnabled = running || _intervalValid;

        StatusDot.Fill = running ? (Brush)FindResource("StartBrush") : new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        StatusText.Text = loc[running ? "StatusRunning" : "StatusStopped"];
        UpdateClickCount();

        _tray.Update(
            running,
            loc.Format("TrayTooltip", StatusText.Text),
            loc["TrayShow"],
            actionText,
            loc["TrayExit"]);
    }

    private void UpdateClickCount() =>
        ClickCountText.Text = Loc.Instance.Format("ClickCount", _engine.ClickCount.ToString("N0", CultureInfo.CurrentCulture));

    // ------------------------------------------------------------------ Interval

    private void IntervalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
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
        _intervalValid = int.TryParse(IntervalBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                         && value >= AppSettings.MinIntervalMs
                         && value <= AppSettings.MaxIntervalMs;

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

        StartStopButton.IsEnabled = _engine.IsRunning || _intervalValid;
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

    // ------------------------------------------------------------------ Hotkeys

    private void ChangeToggleHotkeyButton_Click(object sender, RoutedEventArgs e) => ToggleCapture(HotkeyKind.Toggle);

    private void ChangeRecordHotkeyButton_Click(object sender, RoutedEventArgs e) => ToggleCapture(HotkeyKind.Record);

    private void ToggleCapture(HotkeyKind kind)
    {
        if (_capturing == kind)
        {
            EndHotkeyCapture(null);
            return;
        }

        _capturing = kind;
        _hotkeys.Suspended = true;
        HotkeyErrorText.Visibility = Visibility.Collapsed;
        Keyboard.ClearFocus();
        Focus();
        RefreshHotkeyUi();
    }

    /// <summary>Finishes recording. Returns false (and keeps waiting) if the combination belongs to the other action.</summary>
    private bool EndHotkeyCapture(HotkeyBinding? newBinding)
    {
        if (newBinding != null)
        {
            HotkeyBinding other = _capturing == HotkeyKind.Toggle ? _settings.RecordHotkey : _settings.Hotkey;
            if (newBinding.SameAs(other))
            {
                HotkeyErrorText.Visibility = Visibility.Visible;
                return false;
            }

            if (_capturing == HotkeyKind.Toggle)
            {
                _settings.Hotkey = newBinding;
                _hotkeys.ToggleBinding = newBinding;
            }
            else if (_capturing == HotkeyKind.Record)
            {
                _settings.RecordHotkey = newBinding;
                _hotkeys.RecordBinding = newBinding;
            }

            SaveSettings();
        }

        _capturing = HotkeyKind.None;
        _hotkeys.Suspended = false;
        HotkeyErrorText.Visibility = Visibility.Collapsed;
        RefreshHotkeyUi();
        RefreshPointsUi();
        UpdateRunningState();
        return true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (_capturing == HotkeyKind.None)
        {
            // The global hook already handles hotkeys. Swallow them here so that e.g. Space or Enter
            // don't also press a focused control. Text boxes still get their keys.
            int pressedVk = KeyInterop.VirtualKeyFromKey(key);
            bool isHotkey = pressedVk == _settings.Hotkey.VirtualKey || pressedVk == _settings.RecordHotkey.VirtualKey;
            if (isHotkey && Keyboard.FocusedElement is not TextBox)
                e.Handled = true;
            return;
        }

        e.Handled = true;

        if (key == Key.Escape)
        {
            EndHotkeyCapture(null);
            return;
        }

        // Wait for a real key while only modifiers are held.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed)
            return;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        EndHotkeyCapture(CreateBinding(vk));
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_capturing == HotkeyKind.None) return;

        int vk = e.ChangedButton switch
        {
            MouseButton.XButton1 => HotkeyBinding.VkMouse4,
            MouseButton.XButton2 => HotkeyBinding.VkMouse5,
            MouseButton.Middle => HotkeyBinding.VkMiddleMouse,
            _ => 0,
        };

        // Left/right clicks keep working normally (e.g. to press "Cancel").
        if (vk == 0) return;

        e.Handled = true;
        EndHotkeyCapture(CreateBinding(vk));
    }

    private static HotkeyBinding CreateBinding(int vk)
    {
        ModifierKeys mods = Keyboard.Modifiers;
        return new HotkeyBinding
        {
            VirtualKey = vk,
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Win = mods.HasFlag(ModifierKeys.Windows),
        };
    }

    private void RefreshHotkeyUi()
    {
        UpdateHotkeyRow(HotkeyKind.Toggle, _settings.Hotkey, ToggleHotkeyText, ToggleHotkeyBox, ChangeToggleHotkeyButton);
        UpdateHotkeyRow(HotkeyKind.Record, _settings.RecordHotkey, RecordHotkeyText, RecordHotkeyBox, ChangeRecordHotkeyButton);
    }

    private void UpdateHotkeyRow(HotkeyKind kind, HotkeyBinding binding, TextBlock text, Border box, Button button)
    {
        Loc loc = Loc.Instance;
        bool capturingThis = _capturing == kind;

        text.Text = capturingThis ? loc["PressKey"] : binding.ToDisplayString();
        box.BorderBrush = (Brush)FindResource(capturingThis ? "AccentBrush" : "CardBorderBrush");
        button.Content = loc[capturingThis ? "Cancel" : "Change"];
    }

    // ------------------------------------------------------------------ Screen points

    /// <summary>Called when the "save point" hotkey is pressed: stores the cursor position and makes it the active point.</summary>
    private void AddPoint(ScreenPoint position)
    {
        var point = new SavedPoint
        {
            Name = NextPointName(),
            X = position.X,
            Y = position.Y,
        };

        foreach (SavedPoint other in _points)
            other.IsActive = false;
        point.IsActive = true;

        _points.Add(point);
        SaveSettings();
        ApplyTarget();
        RefreshPointsUi();

        // Feedback — the window is usually hidden behind the game.
        SystemSounds.Asterisk.Play();
        if (!IsVisible || WindowState == WindowState.Minimized)
            _tray.ShowBalloon(Loc.Instance["AppTitle"], Loc.Instance.Format("PointSaved", point.Name, point.CoordinatesText));
    }

    private string NextPointName()
    {
        int number = _points.Count + 1;
        while (_points.Any(p => p.Name == Loc.Instance.Format("PointName", number)))
            number++;
        return Loc.Instance.Format("PointName", number);
    }

    private void PointToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: SavedPoint point }) return;

        // Radio-like behaviour: turning one point on turns the others off. Turning it off leaves none active.
        if (point.IsActive)
        {
            foreach (SavedPoint other in _points)
            {
                if (!ReferenceEquals(other, point))
                    other.IsActive = false;
            }
        }

        SaveSettings();
        ApplyTarget();
    }

    private void DeletePoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SavedPoint point }) return;

        _points.Remove(point);
        SaveSettings();
        ApplyTarget();
        RefreshPointsUi();
    }

    private void PointName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SavedPoint point } textBox) return;

        // Push the typed name into the point first — this handler may run before the binding does.
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (string.IsNullOrWhiteSpace(point.Name))
            point.Name = NextPointName();
        else
            point.Name = point.Name.Trim();

        SaveSettings();
        ApplyTarget(); // refreshes the target line with the new name
    }

    private void PointName_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter / Esc finish editing the name.
        if (e.Key is Key.Enter or Key.Escape)
        {
            Keyboard.ClearFocus();
            Focus();
            e.Handled = true;
        }
    }

    private void MoveEachClickCheck_Click(object sender, RoutedEventArgs e)
    {
        bool value = MoveEachClickCheck.IsChecked == true;
        _settings.MoveBeforeEachClick = value;
        _engine.MoveBeforeEachClick = value;
        SaveSettings();
    }

    /// <summary>Sends the active point (or none) to the engine and updates the "Target" line.</summary>
    private void ApplyTarget()
    {
        SavedPoint? active = _points.FirstOrDefault(p => p.IsActive);
        ScreenPoint? target = active?.Position;

        // Only touch the engine when the position really changed: setting Target moves the cursor while running.
        if (_engine.Target != target)
            _engine.Target = target;

        TargetText.Text = active != null
            ? Loc.Instance.Format("TargetPoint", active.Name, active.CoordinatesText)
            : Loc.Instance["TargetCursor"];
    }

    private void RefreshPointsUi()
    {
        PointsEmptyText.Text = Loc.Instance.Format("PointsEmpty", _settings.RecordHotkey.ToDisplayString());
        PointsEmptyText.Visibility = _points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        // The closed ComboBox caches the selected item's text — reselect to pick up the new language.
        int selected = ButtonCombo.SelectedIndex;
        bool wasInitialized = _initialized;
        _initialized = false;
        ButtonCombo.SelectedIndex = -1;
        ButtonCombo.SelectedIndex = selected;
        _initialized = wasInitialized;

        AdminPanel.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;
        AdminStatusText.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;

        ValidateInterval();
        RefreshHotkeyUi();
        RefreshPointsUi();
        ApplyTarget();
        UpdateRunningState();
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            // The X button hides the window to the tray; the app keeps running.
            e.Cancel = true;
            if (_capturing != HotkeyKind.None) EndHotkeyCapture(null);
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
        _counterTimer.Stop();
        _engine.Dispose();
        _hotkeys.Dispose();
        _tray.Dispose();
        SaveSettings();
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
