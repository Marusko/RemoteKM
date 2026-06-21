using CommunityToolkit.Mvvm.ComponentModel;
using RemoteKm.Shared;

namespace RemoteKm.Client.ViewModels;

/// <summary>
/// Observable view of a discovered host with live "last seen" formatting and an
/// active/idle indicator that the UI binds to.
/// </summary>
public partial class HostItem : ObservableObject
{
    public HostItem(DiscoveredHost host)
    {
        Ip = host.Ip;
        Port = host.Port;
        HostName = host.HostName;
        LastSeen = host.LastSeen;
    }

    public string Ip { get; }
    public int Port { get; }

    [ObservableProperty]
    private string _hostName;

    [ObservableProperty]
    private DateTime _lastSeen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSeenText))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private int _secondsAgo;

    public string Endpoint => $"{Ip}:{Port}";

    public string LastSeenText => $"Last seen: {SecondsAgo}s ago";

    /// <summary>True if seen within the last 3 seconds (drives the pulsing green dot).</summary>
    public bool IsActive => SecondsAgo <= 3;

    public void Refresh(DiscoveredHost host)
    {
        HostName = host.HostName;
        LastSeen = host.LastSeen;
    }

    /// <summary>Recomputes the elapsed-time fields; call on a UI timer tick.</summary>
    public void Tick() => SecondsAgo = Math.Max(0, (int)(DateTime.Now - LastSeen).TotalSeconds);
}
