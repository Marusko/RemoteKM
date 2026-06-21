using CommunityToolkit.Mvvm.Messaging.Messages;

namespace RemoteKm.Client.Messages;

/// <summary>
/// Broadcast when the control connection drops (close frame, error, or keepalive timeout).
/// Carries a human-readable reason for display.
/// </summary>
public sealed class DisconnectedMessage : ValueChangedMessage<string>
{
    public DisconnectedMessage(string reason) : base(reason) { }
}

/// <summary>Carries a host endpoint parsed from a scanned "remotekm://ip:port" QR code.</summary>
public sealed class QrScannedMessage
{
    public QrScannedMessage(string ip, int port)
    {
        Ip = ip;
        Port = port;
    }

    public string Ip { get; }
    public int Port { get; }
}
