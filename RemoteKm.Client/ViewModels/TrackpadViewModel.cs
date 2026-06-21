using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteKm.Client.Services;
using RemoteKm.Shared;

namespace RemoteKm.Client.ViewModels;

/// <summary>
/// Translates touch gestures and the explicit button bar into input commands.
/// </summary>
public partial class TrackpadViewModel : ObservableObject
{
    private readonly ConnectionService _connection;
    private readonly string _sensitivityKey;

    [ObservableProperty]
    private double _sensitivity;

    public TrackpadViewModel(ConnectionService connection)
    {
        _connection = connection;
        // Remember pointer speed per host (keyed by the PC's name, falling back to its IP).
        var host = !string.IsNullOrWhiteSpace(connection.HostName) ? connection.HostName
                 : !string.IsNullOrWhiteSpace(connection.HostIp) ? connection.HostIp
                 : "default";
        _sensitivityKey = $"trackpad_sensitivity::{host}";
        _sensitivity = Preferences.Default.Get(_sensitivityKey, 1.0);
    }

    partial void OnSensitivityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.5, 3.0);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            Sensitivity = clamped;
            return;
        }
        Preferences.Default.Set(_sensitivityKey, clamped);
    }

    /// <summary>Relative pointer movement from a single-finger pan delta.</summary>
    public Task MoveAsync(double dx, double dy)
        => _connection.SendCommandAsync(new MouseMove((float)(dx * Sensitivity), (float)(dy * Sensitivity)));

    public Task ScrollAsync(double deltaY)
        => _connection.SendCommandAsync(new MouseScroll((float)deltaY));

    [RelayCommand]
    private Task LeftClick() => _connection.SendCommandAsync(new MouseClick(MouseButton.Left, ClickType.Single));

    [RelayCommand]
    private Task RightClick() => _connection.SendCommandAsync(new MouseClick(MouseButton.Right, ClickType.Single));

    [RelayCommand]
    private Task MiddleClick() => _connection.SendCommandAsync(new MouseClick(MouseButton.Middle, ClickType.Single));

    public Task DoubleClickAsync()
        => _connection.SendCommandAsync(new MouseClick(MouseButton.Left, ClickType.Double));

    /// <summary>Holds the left button down (begin a drag).</summary>
    public Task LeftDownAsync()
        => _connection.SendCommandAsync(new MouseButtonHold(MouseButton.Left, true));

    /// <summary>Releases the left button (end a drag).</summary>
    public Task LeftUpAsync()
        => _connection.SendCommandAsync(new MouseButtonHold(MouseButton.Left, false));
}
