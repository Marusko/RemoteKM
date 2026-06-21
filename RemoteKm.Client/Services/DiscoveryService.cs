using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteKm.Shared;

namespace RemoteKm.Client.Services;

/// <summary>
/// Broadcasts UDP discovery probes and reports hosts that reply. The caller drives the
/// lifecycle via <see cref="Start"/>/<see cref="StopAsync"/> from page appearing/disappearing.
/// </summary>
public sealed class DiscoveryService
{
    private readonly TimeSpan _broadcastInterval = TimeSpan.FromSeconds(2);

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _sendLoop;
    private Task? _receiveLoop;

    /// <summary>Raised (off the UI thread) whenever a host replies.</summary>
    public event Action<DiscoveredHost>? HostDiscovered;

    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        // Bind to an ephemeral port so we can both send broadcasts and receive replies.
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var token = _cts.Token;
        _sendLoop = Task.Run(() => SendLoopAsync(_udp, token), token);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_udp, token), token);
        AppLog.Info($"[Discovery] Broadcasting on ports {string.Join(",", Protocol.DiscoveryPorts)}");
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        try { _cts.Cancel(); } catch { /* ignored */ }
        try { _udp?.Close(); } catch { /* ignored */ }

        foreach (var loop in new[] { _sendLoop, _receiveLoop })
        {
            if (loop is null) continue;
            try { await loop.ConfigureAwait(false); } catch { /* ignored */ }
        }

        _udp?.Dispose();
        _udp = null;
        _cts.Dispose();
        _cts = null;
        _sendLoop = null;
        _receiveLoop = null;
    }

    private async Task SendLoopAsync(UdpClient udp, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(Protocol.DiscoveryBroadcastPayload);

        while (!token.IsCancellationRequested)
        {
            try
            {
                // The host may have bound any of the candidate discovery ports, so probe all.
                foreach (var port in Protocol.DiscoveryPorts)
                {
                    var broadcast = new IPEndPoint(IPAddress.Broadcast, port);
                    await udp.SendAsync(payload, payload.Length, broadcast).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { /* transient; retry next tick */ }

            try
            {
                await Task.Delay(_broadcastInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (token.IsCancellationRequested) break; else continue; }

            var text = Encoding.UTF8.GetString(result.Buffer);
            if (!text.StartsWith(Protocol.DiscoveryResponsePrefix, StringComparison.Ordinal))
                continue;

            var host = ParseResponse(text, result.RemoteEndPoint.Address.ToString());
            if (host is not null)
                HostDiscovered?.Invoke(host);
        }
    }

    /// <summary>
    /// Parses "REMOTEINPUT_HOST:{HostName}:{Port}". HostName may itself contain colons,
    /// so we split off the trailing port and treat the middle as the name.
    /// </summary>
    private static DiscoveredHost? ParseResponse(string text, string sourceIp)
    {
        var body = text.Substring(Protocol.DiscoveryResponsePrefix.Length);
        int lastColon = body.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == body.Length - 1)
            return null;

        var hostName = body.Substring(0, lastColon);
        var portText = body.Substring(lastColon + 1);
        if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
            return null;

        return new DiscoveredHost(sourceIp, port, hostName, DateTime.Now);
    }
}
