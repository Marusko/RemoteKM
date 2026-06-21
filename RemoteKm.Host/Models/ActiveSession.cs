using System.Net.WebSockets;
using System.Threading;

namespace RemoteKm.Host.Models;

/// <summary>
/// An in-progress WebSocket control session with a connected client.
/// </summary>
public sealed class ActiveSession
{
    public required string ClientId { get; init; }
    public required string ClientName { get; init; }
    public required string RemoteIp { get; init; }
    public required WebSocket Socket { get; init; }
    public DateTime ConnectedAt { get; } = DateTime.Now;

    /// <summary>Used to tear down this session's receive loop on revoke/disconnect.</summary>
    public CancellationTokenSource Cts { get; } = new();

    // UTC ticks of the last message received from the client. The client pings every ~2s, so
    // a long silence means the client vanished (e.g. lost Wi-Fi) without a close frame.
    private long _lastReceivedTicks = DateTime.UtcNow.Ticks;

    public DateTime LastReceivedUtc => new(Interlocked.Read(ref _lastReceivedTicks), DateTimeKind.Utc);

    public void MarkReceived() => Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
}
