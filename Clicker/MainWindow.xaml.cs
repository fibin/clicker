using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ClickEngine _engine = new();
    private readonly GlobalHotkeyListener _hotkeys = new();
    private readonly TrayIcon _tray = new();
    private readonly DispatcherTimer _counterTimer;
    private readonly bool _isAdmin;

    private bool _initialized;
    private bool _capturingHotkey;
    private bool _intervalValid = true;
    private bool _exitRequested;
    private bool _trayHintShown;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        // Clicker engine
        _engine.IntervalMs = settings.IntervalMs;
        _engine.Button = settings.Button;

        // Initial control values
        IntervalBox.Text = settings.IntervalMs.ToString(CultureInfo.InvariantCulture);
        SelectByTag(ButtonCombo, settings.Button.ToString());
        SelectByTag(LanguageCombo, Loc.Instance.Language);

        // Tray
        _tray.ShowRequested += ShowFromTray;
        _tray.ToggleRequested += ToggleClicking;
        _tray.ExitRequested += ExitApplication;

        // Global hotkey
        _hotkeys.Binding = settings.Hotkey;
        _hotkeys.Pressed += () => Dispatcher.BeginInvoke(new Action(ToggleClicking));
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
            SettingsStore.Save(_settings);
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
        SettingsStore.Save(_settings);
    }

    // ------------------------------------------------------------------ Hotkey

    private void ChangeHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
            EndHotkeyCapture(null);
        else
            BeginHotkeyCapture();
    }

    private void BeginHotkeyCapture()
    {
        _capturingHotkey = true;
        _hotkeys.Suspended = true;
        Keyboard.ClearFocus();
        Focus();
        RefreshHotkeyUi();
    }

    private void EndHotkeyCapture(HotkeyBinding? newBinding)
    {
        _capturingHotkey = false;

        if (newBinding != null)
        {
            _settings.Hotkey = newBinding;
            _hotkeys.Binding = newBinding;
            SettingsStore.Save(_settings);
        }

        _hotkeys.Suspended = false;
        RefreshHotkeyUi();
        UpdateRunningState();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (!_capturingHotkey)
        {
            // The global hook already handles the hotkey. Swallow it here so that e.g. Space or Enter
            // don't also press a focused button and toggle twice.
            if (KeyInterop.VirtualKeyFromKey(key) == _settings.Hotkey.VirtualKey && !IntervalBox.IsKeyboardFocused)
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

        ModifierKeys mods = Keyboard.Modifiers;
        EndHotkeyCapture(new HotkeyBinding
        {
            VirtualKey = vk,
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Win = mods.HasFlag(ModifierKeys.Windows),
        });
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_capturingHotkey) return;

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
        ModifierKeys mods = Keyboard.Modifiers;
        EndHotkeyCapture(new HotkeyBinding
        {
            VirtualKey = vk,
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Win = mods.HasFlag(ModifierKeys.Windows),
        });
    }

    private void RefreshHotkeyUi()
    {
        Loc loc = Loc.Instance;
        if (_capturingHotkey)
        {
            HotkeyText.Text = loc["PressKey"];
            HotkeyBox.BorderBrush = (Brush)FindResource("AccentBrush");
            ChangeHotkeyButton.Content = loc["Cancel"];
        }
        else
        {
            HotkeyText.Text = _settings.Hotkey.ToDisplayString();
            HotkeyBox.BorderBrush = (Brush)FindResource("CardBorderBrush");
            ChangeHotkeyButton.Content = loc["Change"];
        }
    }

    // ------------------------------------------------------------------ Language

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language }) return;

        _settings.Language = language;
        SettingsStore.Save(_settings);
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
            if (_capturingHotkey) EndHotkeyCapture(null);
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
        SettingsStore.Save(_settings);
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
        SettingsStore.Save(_settings);

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
