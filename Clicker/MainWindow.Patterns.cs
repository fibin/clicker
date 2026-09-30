using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Clicker.Localization;
using Clicker.Models;
using Clicker.Services;

namespace Clicker;

/// <summary>Recording mouse/keyboard patterns and replaying them in a loop.</summary>
public partial class MainWindow
{
    private bool _patternNumbersValid = true;

    /// <summary>The pattern currently being played (to stop playback if it is switched off or deleted).</summary>
    private Pattern? _playingPattern;

    private Pattern? ActivePattern => _patterns.FirstOrDefault(p => p.IsActive);

    // ------------------------------------------------------------------ Recording

    private void RecordPatternButton_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    /// <summary>"Record pattern" hotkey / button: first press starts, second press saves.</summary>
    private void ToggleRecording()
    {
        if (_capturing != null) EndHotkeyCapture(null);

        if (_recorder.IsRecording)
            FinishRecording();
        else
            BeginRecording();
    }

    private void BeginRecording()
    {
        StopAll();
        _recorder.Start(recordKeyboard: _settings.RecordKeyboard);
        SystemSounds.Exclamation.Play();
        UpdateRunningState();
    }

    private void FinishRecording()
    {
        List<PatternEvent> events = _recorder.Stop();

        if (events.Count == 0)
        {
            SystemSounds.Hand.Play();
            UpdateRunningState();
            ClickCountText.Text = Loc.Instance["NothingRecorded"];
            return;
        }

        var pattern = new Pattern
        {
            Name = NextName(_patterns.Select(p => p.Name), "PatternName"),
            Events = events,
        };

        // The new recording becomes the active pattern, ready to play with the start hotkey.
        foreach (Pattern other in _patterns)
            other.IsActive = false;
        pattern.IsActive = true;

        _patterns.Add(pattern);
        SavePatterns();
        RefreshPatternsUi();
        UpdateRunningState();

        SystemSounds.Asterisk.Play();
        if (IsHiddenOrMinimized)
            _tray.ShowBalloon(Loc.Instance["AppTitle"], Loc.Instance.Format("PatternSaved", pattern.Name, pattern.InfoText));
    }

    private void RecordKeyboardCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.RecordKeyboard = RecordKeyboardCheck.IsChecked == true;
        SaveSettings();
    }

    // ------------------------------------------------------------------ Playback

    private void StartPattern(Pattern pattern)
    {
        if (!_patternNumbersValid || pattern.Events.Count == 0) return;

        _playingPattern = pattern;
        _player.Start(pattern.Events, _settings.PatternRepeatCount, _settings.PatternLoopDelayMs);
    }

    /// <summary>All repeats are done.</summary>
    private void OnPlaybackFinished()
    {
        // A new run may already have started before this notification arrived.
        if (!_player.IsRunning)
            _playingPattern = null;

        UpdateRunningState();
    }

    /// <summary>Stops playback if the pattern being played is no longer the active one.</summary>
    private void StopPlaybackIfPatternChanged()
    {
        if (_player.IsRunning && (_playingPattern == null || !_playingPattern.IsActive || !_patterns.Contains(_playingPattern)))
        {
            _player.Stop();
            _playingPattern = null;
        }
    }

    // ------------------------------------------------------------------ List editing

    private void PatternToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: Pattern pattern }) return;

        // Only one pattern can be on. Turning it off switches back to plain auto-clicking.
        if (pattern.IsActive)
        {
            foreach (Pattern other in _patterns)
            {
                if (!ReferenceEquals(other, pattern))
                    other.IsActive = false;
            }
        }

        StopPlaybackIfPatternChanged();
        SavePatterns();
        UpdateRunningState();
    }

    private void DeletePattern_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Pattern pattern }) return;

        _patterns.Remove(pattern);
        StopPlaybackIfPatternChanged();
        SavePatterns();
        RefreshPatternsUi();
        UpdateRunningState();
    }

    private void PatternName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: Pattern pattern } textBox) return;

        // Push the typed name into the pattern first — this handler may run before the binding does.
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        pattern.Name = string.IsNullOrWhiteSpace(pattern.Name)
            ? NextName(_patterns.Where(p => !ReferenceEquals(p, pattern)).Select(p => p.Name), "PatternName")
            : pattern.Name.Trim();

        SavePatterns();
        UpdateTargetText();
    }

    // ------------------------------------------------------------------ Repeats / pause

    private void PatternNumbers_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        ValidatePatternNumbers();
        UpdateRunningState();
    }

    private void PatternNumbers_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_patternNumbersValid)
            SaveSettings();
    }

    private void ValidatePatternNumbers()
    {
        bool repeatsOk = TryParseNumber(RepeatsBox.Text, 0, AppSettings.MaxRepeatCount, out int repeats);
        bool delayOk = TryParseNumber(LoopDelayBox.Text, 0, AppSettings.MaxLoopDelayMs, out int delay);
        _patternNumbersValid = repeatsOk && delayOk;

        if (repeatsOk) _settings.PatternRepeatCount = repeats;
        if (delayOk) _settings.PatternLoopDelayMs = delay;

        MarkValid(RepeatsBox, repeatsOk);
        MarkValid(LoopDelayBox, delayOk);

        if (_patternNumbersValid)
        {
            PatternNumbersError.Visibility = Visibility.Collapsed;
        }
        else
        {
            int max = repeatsOk ? AppSettings.MaxLoopDelayMs : AppSettings.MaxRepeatCount;
            PatternNumbersError.Text = Loc.Instance.Format("InvalidNumber", max);
            PatternNumbersError.Visibility = Visibility.Visible;
        }
    }

    private void MarkValid(TextBox box, bool valid)
    {
        if (valid)
            box.ClearValue(BorderBrushProperty);
        else
            box.BorderBrush = (Brush)FindResource("ErrorBrush");
    }

    // ------------------------------------------------------------------ Helpers

    private void RefreshPatternsUi()
    {
        string recordKey = _settings.PatternHotkey.ToDisplayString();
        PatternsEmptyText.Text = Loc.Instance.Format("PatternsEmpty", recordKey);
        PatternsEmptyText.Visibility = _patterns.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PatternsCard.Visibility = _patterns.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PatternsHintText.Text = Loc.Instance.Format("PatternsHint", _settings.Hotkey.ToDisplayString());
    }

    private void SavePatterns() => PatternStore.Save(_patterns);
}
