using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Clicker.Localization;

namespace Clicker.Models;

/// <summary>A recorded sequence of mouse/keyboard events that can be replayed in a loop.</summary>
public sealed class Pattern : INotifyPropertyChanged
{
    private string _name = "";
    private bool _isActive;
    private List<PatternEvent> _events = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? "");
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public List<PatternEvent> Events
    {
        get => _events;
        set
        {
            _events = value ?? new List<PatternEvent>();
            OnPropertyChanged(nameof(InfoText));
        }
    }

    [JsonIgnore]
    public int DurationMs => _events.Count == 0 ? 0 : _events[^1].Time;

    /// <summary>"12.4 s · 356 events" in the current UI language.</summary>
    [JsonIgnore]
    public string InfoText => Loc.Instance.Format(
        "PatternInfo",
        (DurationMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture),
        _events.Count);

    /// <summary>Re-evaluates <see cref="InfoText"/> after a language switch.</summary>
    public void RefreshInfo() => OnPropertyChanged(nameof(InfoText));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
