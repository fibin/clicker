using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Clicker.Localization;
using Clicker.Models;

namespace Clicker;

/// <summary>Global hotkey dispatch and recording new hotkey combinations.</summary>
public partial class MainWindow
{
    /// <summary>The action whose hotkey is being recorded, or null.</summary>
    private HotkeyAction? _capturing;

    private void OnHotkeyPressed(HotkeyAction action, ScreenPoint cursor)
    {
        switch (action)
        {
            case HotkeyAction.Toggle:
                ToggleRunning();
                break;
            case HotkeyAction.SavePoint:
                AddPoint(cursor);
                break;
            case HotkeyAction.RecordPattern:
                ToggleRecording();
                break;
        }
    }

    private HotkeyBinding GetBinding(HotkeyAction action) => action switch
    {
        HotkeyAction.SavePoint => _settings.RecordHotkey,
        HotkeyAction.RecordPattern => _settings.PatternHotkey,
        _ => _settings.Hotkey,
    };

    private void StoreBinding(HotkeyAction action, HotkeyBinding binding)
    {
        switch (action)
        {
            case HotkeyAction.SavePoint:
                _settings.RecordHotkey = binding;
                break;
            case HotkeyAction.RecordPattern:
                _settings.PatternHotkey = binding;
                break;
            default:
                _settings.Hotkey = binding;
                break;
        }

        _hotkeys.SetBinding(action, binding);
    }

    private void ChangeToggleHotkeyButton_Click(object sender, RoutedEventArgs e) => ToggleCapture(HotkeyAction.Toggle);

    private void ChangeRecordHotkeyButton_Click(object sender, RoutedEventArgs e) => ToggleCapture(HotkeyAction.SavePoint);

    private void ChangePatternHotkeyButton_Click(object sender, RoutedEventArgs e) => ToggleCapture(HotkeyAction.RecordPattern);

    private void ToggleCapture(HotkeyAction action)
    {
        if (_capturing == action)
        {
            EndHotkeyCapture(null);
            return;
        }

        if (_recorder.IsRecording) FinishRecording();

        _capturing = action;
        _hotkeys.Suspended = true;
        HotkeyErrorText.Visibility = Visibility.Collapsed;
        Keyboard.ClearFocus();
        Focus();
        RefreshHotkeyUi();
    }

    /// <summary>Finishes recording a hotkey. Returns false (and keeps waiting) if another action already uses it.</summary>
    private bool EndHotkeyCapture(HotkeyBinding? newBinding)
    {
        if (newBinding != null && _capturing is HotkeyAction action)
        {
            foreach (HotkeyAction other in new[] { HotkeyAction.Toggle, HotkeyAction.SavePoint, HotkeyAction.RecordPattern })
            {
                if (other != action && newBinding.SameAs(GetBinding(other)))
                {
                    HotkeyErrorText.Visibility = Visibility.Visible;
                    return false;
                }
            }

            StoreBinding(action, newBinding);
            SaveSettings();
        }

        _capturing = null;
        _hotkeys.Suspended = false;
        HotkeyErrorText.Visibility = Visibility.Collapsed;
        RefreshHotkeyUi();
        RefreshPointsUi();
        RefreshPatternsUi();
        UpdateRunningState();
        return true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (_capturing == null)
        {
            // The global hook already handles hotkeys. Swallow them here so that e.g. Space or Enter
            // don't also press a focused control. Text boxes still get their keys.
            int pressedVk = KeyInterop.VirtualKeyFromKey(key);
            bool isHotkey = pressedVk == _settings.Hotkey.VirtualKey
                            || pressedVk == _settings.RecordHotkey.VirtualKey
                            || pressedVk == _settings.PatternHotkey.VirtualKey;
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
        if (_capturing == null) return;

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
        UpdateHotkeyRow(HotkeyAction.Toggle, ToggleHotkeyText, ToggleHotkeyBox, ChangeToggleHotkeyButton);
        UpdateHotkeyRow(HotkeyAction.SavePoint, RecordHotkeyText, RecordHotkeyBox, ChangeRecordHotkeyButton);
        UpdateHotkeyRow(HotkeyAction.RecordPattern, PatternHotkeyText, PatternHotkeyBox, ChangePatternHotkeyButton);
    }

    private void UpdateHotkeyRow(HotkeyAction action, TextBlock text, Border box, Button button)
    {
        Loc loc = Loc.Instance;
        bool capturingThis = _capturing == action;

        text.Text = capturingThis ? loc["PressKey"] : GetBinding(action).ToDisplayString();
        box.BorderBrush = (Brush)FindResource(capturingThis ? "AccentBrush" : "CardBorderBrush");
        button.Content = loc[capturingThis ? "Cancel" : "Change"];
    }
}
