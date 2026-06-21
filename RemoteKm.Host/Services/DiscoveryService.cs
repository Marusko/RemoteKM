using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteKm.Shared;

namespace RemoteKm.Host.Services;

/// <summary>
/// Listens for UDP discovery broadcasts and replies so clients can find this host.
/// Binds the first available port from <see cref="Protocol.DiscoveryPorts"/>.
/// </summary>
public sealed class DiscoveryService
{
    private readonly Func<int> _getControlPort;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <param name="getControlPort">
    /// Returns the control port the host is actually listening on, so discovery replies
    /// always advertise the live port (which may differ from the configured one if a
    /// fallback was used).
    /// </param>
    public DiscoveryService(Func<int> getControlPort)
    {
        _getControlPort = getControlPort;
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>The UDP port discovery actually bound to, or 0 if not running.</summary>
    public int BoundPort { get; private set; }

    /// <summary>Raised with a human-readable message when discovery fails to start.</summary>
    public event Action<string>? StartupFailed;

    public void Start()
    {
        if (IsRunning)
            return;

        SocketException? lastError = null;
        foreach (var port in Protocol.DiscoveryPorts)
        {
            try
            {
                var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));

                _udp = udp;
                _cts = new CancellationTokenSource();
                BoundPort = port;
                _loop = Task.Run(() => ReceiveLoopAsync(udp, _cts.Token));
                AppLog.Info($"Discovery listening on UDP {port}");
                return;
            }
            catch (SocketException ex)
            {
                lastError = ex;
            }
        }

        // Every candidate failed.
        BoundPort = 0;
        StartupFailed?.Invoke(
            $"Discovery is off: none of the UDP ports [{string.Join(", ", Protocol.DiscoveryPorts)}] " +
            $"could be opened ({lastError?.SocketErrorCode}). Clients can still connect manually by IP.");
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        try { _cts.Cancel(); } catch { /* ignored */ }
        try { _udp?.Close(); } catch { /* ignored */ }

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* ignored */ }
        }

        _udp?.Dispose();
        _udp = null;
        _cts.Dispose();
        _cts = null;
        _loop = null;
        BoundPort = 0;
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
                var payload = Encoding.UTF8.GetString(result.Buffer);

                if (!payload.StartsWith(Protocol.DiscoveryBroadcastPayload, StringComparison.Ordinal))
                    continue;

                var response = Protocol.BuildDiscoveryResponse(Environment.MachineName, _getControlPort());
                var bytes = Encoding.UTF8.GetBytes(response);

                await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (token.IsCancellationRequested)
                    break;
            }
        }
    }
}
