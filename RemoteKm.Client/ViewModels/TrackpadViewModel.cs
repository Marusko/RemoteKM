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

    // Motion waiting to be sent, coalesced so only one command is ever in flight.
    private readonly object _pendingLock = new();
    private double _pendingDx, _pendingDy, _pendingScroll;
    private bool _sending;

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
    public Task MoveAsync(double dx, double dy) => QueueAsync(dx, dy, 0);

    public Task ScrollAsync(double deltaY) => QueueAsync(0, 0, deltaY);

    /// <summary>
    /// Touch events arrive far faster than the socket can drain them, so pending deltas are
    /// summed while a send is in flight and go out as one command. Nothing is lost and the
    /// cursor never keeps travelling on a backlog after the finger has stopped.
    /// </summary>
    private Task QueueAsync(double dx, double dy, double scroll)
    {
        lock (_pendingLock)
        {
            _pendingDx += dx;
            _pendingDy += dy;
            _pendingScroll += scroll;

            if (_sending)
                return Task.CompletedTask;

            _sending = true;
        }

        return PumpAsync();
    }

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                double dx, dy, scroll;
                lock (_pendingLock)
                {
                    dx = _pendingDx;
                    dy = _pendingDy;
                    scroll = _pendingScroll;
                    _pendingDx = _pendingDy = _pendingScroll = 0;

                    if (dx == 0 && dy == 0 && scroll == 0)
                    {
                        _sending = false;
                        return;
                    }
                }

                if (dx != 0 || dy != 0)
                    await _connection.SendCommandAsync(new MouseMove((float)(dx * Sensitivity), (float)(dy * Sensitivity)));

                if (scroll != 0)
                    await _connection.SendCommandAsync(new MouseScroll((float)scroll));
            }
        }
        catch
        {
            // A send that blew up must not latch the pump shut and kill the trackpad.
            lock (_pendingLock)
                _sending = false;
        }
    }

    [RelayCommand]
    private Task LeftClick() => _connection.SendCommandAsync(new MouseClick(MouseButton.Left, ClickType.Single));

    [RelayCommand]
    private Task RightClick() => _connection.SendCommandAsync(new MouseClick(MouseButton.Right, ClickType.Single));

    [RelayCommand]
    private Task MiddleClick() => _connection.SendCommandAsync(new MouseClick(MouseButton.Middle, ClickType.Single));

    /// <summary>Holds the left button down (begin a drag).</summary>
    public Task LeftDownAsync()
        => _connection.SendCommandAsync(new MouseButtonHold(MouseButton.Left, true));

    /// <summary>Releases the left button (end a drag).</summary>
    public Task LeftUpAsync()
        => _connection.SendCommandAsync(new MouseButtonHold(MouseButton.Left, false));
}
