using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Clicker.Models;

/// <summary>A saved screen position shown in the points list. At most one point is active at a time.</summary>
public sealed class SavedPoint : INotifyPropertyChanged
{
    private string _name = "";
    private int _x;
    private int _y;
    private bool _isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? "");
    }

    /// <summary>Physical pixels on the virtual desktop (can be negative on a monitor left of / above the main one).</summary>
    public int X
    {
        get => _x;
        set
        {
            if (Set(ref _x, value)) OnPropertyChanged(nameof(CoordinatesText));
        }
    }

    public int Y
    {
        get => _y;
        set
        {
            if (Set(ref _y, value)) OnPropertyChanged(nameof(CoordinatesText));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    [JsonIgnore]
    public string CoordinatesText => $"{X}, {Y}";

    [JsonIgnore]
    public ScreenPoint Position => new(X, Y);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
