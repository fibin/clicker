using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Clicker.Localization;
using Clicker.Models;

namespace Clicker;

/// <summary>The always-on-top status overlay and its settings tab.</summary>
public partial class MainWindow
{
    private readonly OverlayWindow _overlay = new();
    private bool _overlayMoveMode;

    private void InitOverlay()
    {
        OverlayEnabledCheck.IsChecked = _settings.OverlayEnabled;
        OverlayShowStoppedCheck.IsChecked = _settings.OverlayShowWhenStopped;
        OverlayBlinkCheck.IsChecked = _settings.OverlayBlink;
        OverlayOpacitySlider.Value = _settings.OverlayOpacity;
        OverlayScaleSlider.Value = _settings.OverlayScale;
        SelectByTag(BlinkSpeedCombo, _settings.OverlayBlinkSpeed.ToString());

        _overlay.SetAppearance(_settings.OverlayOpacity, _settings.OverlayScale);
        _overlay.PlaceAt(_settings.OverlayLeft, _settings.OverlayTop);
        _overlay.Moved += (left, top) =>
        {
            _settings.OverlayLeft = left;
            _settings.OverlayTop = top;
            SaveSettings();
        };

        // Show a live preview while the Overlay tab is open; hide it again when the window goes to the tray.
        IsVisibleChanged += (_, _) => UpdateOverlay();
        Activated += (_, _) => UpdateOverlay();
        Deactivated += (_, _) => UpdateOverlay();
        StateChanged += (_, _) =>
        {
            // Never leave the overlay grabbing the mouse while the settings window is out of sight.
            if (WindowState == WindowState.Minimized && _overlayMoveMode)
                SetOverlayMoveMode(false);
            else
                UpdateOverlay();
        };

        UpdateOverlaySliderTexts();
    }

    private OverlayState CurrentOverlayState =>
        _recorder.IsRecording ? OverlayState.Recording
        : _player.IsRunning ? OverlayState.Playing
        : _engine.IsRunning ? OverlayState.Clicking
        : OverlayState.Stopped;

    /// <summary>Shows, hides and fills the overlay for the current state. Called on every status refresh.</summary>
    private void UpdateOverlay()
    {
        OverlayState state = CurrentOverlayState;
        bool active = state != OverlayState.Stopped;
        // Preview only while the user is actually looking at the Overlay tab (not with the window behind a game).
        bool preview = IsVisible && IsActive && WindowState != WindowState.Minimized
                       && ReferenceEquals(Tabs.SelectedItem, OverlayTab);

        bool show = _overlayMoveMode
                    || (_settings.OverlayEnabled && (active || preview || _settings.OverlayShowWhenStopped));
        if (!show)
        {
            _overlay.HideOverlay();
            return;
        }

        string titleKey = state switch
        {
            OverlayState.Clicking => "OverlayClicking",
            OverlayState.Playing => "OverlayPlaying",
            OverlayState.Recording => "OverlayRecording",
            _ => "OverlayStopped",
        };

        _overlay.SetContent(state, Loc.Instance[titleKey], active ? ClickCountText.Text : "");
        _overlay.SetBlink(_settings.OverlayBlink && !_overlayMoveMode && (active || preview), _settings.OverlayBlinkSpeed);
        _overlay.ShowOverlay();
    }

    private void OverlayOption_Click(object sender, RoutedEventArgs e)
    {
        _settings.OverlayEnabled = OverlayEnabledCheck.IsChecked == true;
        _settings.OverlayShowWhenStopped = OverlayShowStoppedCheck.IsChecked == true;
        _settings.OverlayBlink = OverlayBlinkCheck.IsChecked == true;
        SaveSettings();
        UpdateOverlay();
    }

    private void OverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOverlaySliderTexts();
        if (!_initialized) return;

        _settings.OverlayOpacity = (int)Math.Round(OverlayOpacitySlider.Value);
        _settings.OverlayScale = (int)Math.Round(OverlayScaleSlider.Value);
        _overlay.SetAppearance(_settings.OverlayOpacity, _settings.OverlayScale);
        SaveSettings();
    }

    private void UpdateOverlaySliderTexts()
    {
        // Sliders fire ValueChanged while the window is still being built.
        if (OverlayOpacityText == null || OverlayScaleText == null) return;

        OverlayOpacityText.Text = ((int)Math.Round(OverlayOpacitySlider.Value)).ToString(CultureInfo.InvariantCulture) + "%";
        OverlayScaleText.Text = ((int)Math.Round(OverlayScaleSlider.Value)).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void BlinkSpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || BlinkSpeedCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (!Enum.TryParse(tag, out BlinkSpeed speed)) return;

        _settings.OverlayBlinkSpeed = speed;
        SaveSettings();
        UpdateOverlay();
    }

    private void MoveOverlayButton_Click(object sender, RoutedEventArgs e) => SetOverlayMoveMode(!_overlayMoveMode);

    private void SetOverlayMoveMode(bool on)
    {
        _overlayMoveMode = on;
        _overlay.MoveMode = on;
        MoveOverlayButton.Content = Loc.Instance[on ? "MoveOverlayDone" : "MoveOverlay"];
        UpdateOverlay();
    }

    private void ResetOverlayPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.OverlayLeft = null;
        _settings.OverlayTop = null;
        _overlay.PlaceAt(null, null);
        SaveSettings();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged also bubbles up from combo boxes inside the tabs.
        if (!_initialized || !ReferenceEquals(e.OriginalSource, Tabs)) return;

        // Leaving the Overlay tab locks the overlay in place again.
        if (_overlayMoveMode && !ReferenceEquals(Tabs.SelectedItem, OverlayTab))
            SetOverlayMoveMode(false);

        UpdateOverlay();
    }
}
