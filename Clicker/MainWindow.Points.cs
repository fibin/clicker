using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Clicker.Localization;
using Clicker.Models;

namespace Clicker;

/// <summary>Saved screen points: the auto-clicker moves the cursor to the active one.</summary>
public partial class MainWindow
{
    /// <summary>"Save point" hotkey: stores the cursor position and makes it the active point.</summary>
    private void AddPoint(ScreenPoint position)
    {
        var point = new SavedPoint
        {
            Name = NextName(_points.Select(p => p.Name), "PointName"),
            X = position.X,
            Y = position.Y,
        };

        foreach (SavedPoint other in _points)
            other.IsActive = false;
        point.IsActive = true;

        _points.Add(point);
        SaveSettings();
        ApplyPointTarget();
        RefreshPointsUi();

        // Feedback — the window is usually hidden behind the game.
        SystemSounds.Asterisk.Play();
        if (IsHiddenOrMinimized)
            _tray.ShowBalloon(Loc.Instance["AppTitle"], Loc.Instance.Format("PointSaved", point.Name, point.CoordinatesText));
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
        ApplyPointTarget();
    }

    private void DeletePoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SavedPoint point }) return;

        _points.Remove(point);
        SaveSettings();
        ApplyPointTarget();
        RefreshPointsUi();
    }

    private void PointName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SavedPoint point } textBox) return;

        // Push the typed name into the point first — this handler may run before the binding does.
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        point.Name = string.IsNullOrWhiteSpace(point.Name)
            ? NextName(_points.Where(p => !ReferenceEquals(p, point)).Select(p => p.Name), "PointName")
            : point.Name.Trim();

        SaveSettings();
        UpdateTargetText();
    }

    private void MoveEachClickCheck_Click(object sender, RoutedEventArgs e)
    {
        bool value = MoveEachClickCheck.IsChecked == true;
        _settings.MoveBeforeEachClick = value;
        _engine.MoveBeforeEachClick = value;
        SaveSettings();
    }

    /// <summary>Sends the active point (or none) to the click engine and refreshes the status.</summary>
    private void ApplyPointTarget()
    {
        ScreenPoint? target = _points.FirstOrDefault(p => p.IsActive)?.Position;

        // Only touch the engine when the position really changed: setting Target moves the cursor while running.
        if (_engine.Target != target)
            _engine.Target = target;

        UpdateTargetText();
    }

    private void RefreshPointsUi()
    {
        PointsEmptyText.Text = Loc.Instance.Format("PointsEmpty", _settings.RecordHotkey.ToDisplayString());
        PointsEmptyText.Visibility = _points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>First "Point N" / "Pattern N" name that isn't taken yet.</summary>
    private static string NextName(IEnumerable<string> existing, string formatKey)
    {
        var taken = existing.ToHashSet();
        int number = taken.Count + 1;
        while (taken.Contains(Loc.Instance.Format(formatKey, number)))
            number++;
        return Loc.Instance.Format(formatKey, number);
    }
}
