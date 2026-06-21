using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using RemoteKm.Client.Messages;
using RemoteKm.Shared;

namespace RemoteKm.Client.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    WaitingForApproval,
    Connected
}

public enum ConnectResultKind
{
    Accepted,
    Rejected,
    Timeout,
    Error
}

public readonly record struct ConnectResult(ConnectResultKind Kind, string? Message = null)
{
    public bool IsSuccess => Kind == ConnectResultKind.Accepted;
}

/// <summary>
/// Owns the client's <see cref="ClientWebSocket"/> connection: pairing handshake,
/// command sending, the background receive loop, keepalive ping/pong, and stats.
/// </summary>
public sealed class ConnectionService : INotifyPropertyChanged
{
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(5);

    private readonly ClientIdentity _identity;
    private readonly IMessenger _messenger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _pingLoop;

    private readonly object _statsLock = new();
    private readonly Queue<double> _latencySamples = new();
    private long _lastPingTicks;
    private DateTime _lastPongUtc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConnectionService(ClientIdentity identity, IMessenger messenger)
    {
        _identity = identity;
        _messenger = messenger;
    }

    // ---- Observable state ----

    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _state = value;
            // Keep the phone screen awake for the whole time we're connected.
            SetKeepScreenOn(value == ConnectionState.Connected);
            OnPropertyChanged();
        }
    }

    private static void SetKeepScreenOn(bool on)
    {
        void Apply()
        {
            try { DeviceDisplay.Current.KeepScreenOn = on; }
            catch { /* not supported on some platforms */ }
        }

        if (MainThread.IsMainThread) Apply();
        else MainThread.BeginInvokeOnMainThread(Apply);
    }

    public string HostName { get; private set; } = string.Empty;
    public string HostIp { get; private set; } = string.Empty;
    public int HostPort { get; private set; }
    public DateTime? ConnectedAt { get; private set; }

    /// <summary>On-screen keyboard layout reported by the host (from its PC language).</summary>
    public KeyboardLayout KeyboardLayout { get; private set; } = KeyboardLayout.Qwerty;

    /// <summary>The host's two-letter language code (e.g. "sk"), used to pick top-row characters.</summary>
    public string HostLanguage { get; private set; } = "en";

    /// <summary>Raised when the host pushes a live keyboard layout/language change.</summary>
    public event Action? LayoutChanged;

    private long _bytesSent;
    public long BytesSent { get => Interlocked.Read(ref _bytesSent); }

    private long _bytesReceived;
    public long BytesReceived { get => Interlocked.Read(ref _bytesReceived); }

    public double LatencyMs
    {
        get
        {
            lock (_statsLock)
                return _latencySamples.Count == 0 ? 0 : _latencySamples.Average();
        }
    }

    // ---- Connect / disconnect ----

    public async Task<ConnectResult> ConnectAsync(string ip, int port, string? hostName = null)
    {
        await DisconnectAsync().ConfigureAwait(false);
        await _identity.EnsureInitializedAsync().ConfigureAwait(false);
        AppLog.Info($"[Conn] Connecting to {ip}:{port}…");

        State = ConnectionState.Connecting;
        HostIp = ip;
        HostPort = port;
        HostName = string.IsNullOrWhiteSpace(hostName) ? ip : hostName!;
        ResetStats();

        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        try
        {
            var uri = new Uri($"ws://{ip}:{port}/");
            using (var connectCts = new CancellationTokenSource(PairingTimeout))
                await _ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);

            // Send the pairing request.
            var request = new PairingRequest(_identity.ClientId, _identity.ClientName, PublicKeyBase64: string.Empty);
            await SendJsonAsync(request, _cts.Token).ConfigureAwait(false);

            State = ConnectionState.WaitingForApproval;

            // Await the pairing response (bounded).
            var buffer = new byte[Protocol.MaxMessageSize];
            using var pairingCts = new CancellationTokenSource(PairingTimeout);
            var (text, closed) = await ReceiveTextAsync(buffer, pairingCts.Token).ConfigureAwait(false);
            if (closed || text is null)
                return Fail(ConnectResultKind.Error, "Connection closed during pairing.");

            var response = RemoteJson.Deserialize<PairingResponse>(text);
            if (response is null)
                return Fail(ConnectResultKind.Error, "Invalid pairing response.");

            switch (response.Status)
            {
                case PairingStatus.Accepted:
                case PairingStatus.AlreadyTrusted:
                    // Prefer the real machine name the host reports over the IP/discovery hint.
                    if (!string.IsNullOrWhiteSpace(response.HostName))
                    {
                        HostName = response.HostName;
                        OnPropertyChanged(nameof(HostName));
                    }
                    KeyboardLayout = response.KeyboardLayout;
                    HostLanguage = string.IsNullOrWhiteSpace(response.Language) ? "en" : response.Language;
                    BeginConnectedSession();
                    AppLog.Info($"[Conn] Connected to {HostName} ({ip}:{port}), status={response.Status}, lang={HostLanguage}");
                    return new ConnectResult(ConnectResultKind.Accepted);
                case PairingStatus.Rejected:
                    AppLog.Warn($"[Conn] Pairing rejected by host {ip}:{port}");
                    await DisconnectAsync().ConfigureAwait(false);
                    return new ConnectResult(ConnectResultKind.Rejected, "Connection rejected by the host.");
                default:
                    return Fail(ConnectResultKind.Error, "The host sent an unexpected response.");
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Warn($"[Conn] Timed out connecting to {ip}:{port}");
            await DisconnectAsync().ConfigureAwait(false);
            return new ConnectResult(ConnectResultKind.Timeout, "No response from the host. Check it's running and on the same network.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"[Conn] Failed to connect to {ip}:{port}", ex);
            await DisconnectAsync().ConfigureAwait(false);
            return new ConnectResult(ConnectResultKind.Error, "Couldn't reach the host. Check the IP, port, and Wi-Fi.");
        }
    }

    private ConnectResult Fail(ConnectResultKind kind, string message)
    {
        _ = DisconnectAsync();
        return new ConnectResult(kind, message);
    }

    private void BeginConnectedSession()
    {
        ConnectedAt = DateTime.Now;
        _lastPongUtc = DateTime.UtcNow;
        State = ConnectionState.Connected;

        var token = _cts!.Token;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(token), token);
        _pingLoop = Task.Run(() => PingLoopAsync(token), token);
    }

    public async Task DisconnectAsync()
    {
        var ws = _ws;
        var cts = _cts;
        _ws = null;
        _cts = null;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { /* ignored */ }
        }

        if (ws is not null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token).ConfigureAwait(false);
                }
            }
            catch { /* ignored */ }
            finally { ws.Dispose(); }
        }

        cts?.Dispose();

        if (State != ConnectionState.Disconnected)
        {
            ConnectedAt = null;
            State = ConnectionState.Disconnected;
            AppLog.Info("[Conn] Disconnected.");
        }
    }

    // ---- Sending ----

    public async Task SendCommandAsync(InputCommand command)
    {
        if (State != ConnectionState.Connected || _ws is null || _cts is null)
            return;

        try
        {
            await SendJsonAsync(CommandEnvelope.Now(command), _cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // A failed send means the socket is gone; the receive loop reports the drop.
        }
    }

    private async Task SendJsonAsync<T>(T value, CancellationToken token)
    {
        var ws = _ws;
        if (ws is null) return;

        var json = RemoteJson.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        Interlocked.Add(ref _bytesSent, bytes.Length);
        OnPropertyChanged(nameof(BytesSent));
    }

    // ---- Receive / keepalive ----

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        string reason = "Disconnected from host";
        try
        {
            while (!token.IsCancellationRequested)
            {
                var (text, closed) = await ReceiveTextAsync(buffer, token).ConfigureAwait(false);
                if (closed)
                    break;
                if (text is null)
                    continue;

                var envelope = RemoteJson.Deserialize<CommandEnvelope>(text);
                switch (envelope?.Command)
                {
                    case PongCommand:
                        RecordPong();
                        break;
                    case LayoutChangedCommand layout:
                        KeyboardLayout = layout.Layout;
                        HostLanguage = string.IsNullOrWhiteSpace(layout.Language) ? "en" : layout.Language;
                        AppLog.Info($"[Conn] Host layout changed: {KeyboardLayout}/{HostLanguage}");
                        LayoutChanged?.Invoke();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return; // deliberate disconnect
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or SocketException or ObjectDisposedException)
        {
            // The host closing / the network dropping the socket is expected, not an error.
            AppLog.Info($"[Conn] Connection closed: {ex.GetType().Name}.");
            reason = "Disconnected from the host.";
        }
        catch (Exception ex)
        {
            AppLog.Error("[Conn] Receive loop error", ex);
            reason = "Connection lost.";
        }

        if (!token.IsCancellationRequested)
            await HandleDroppedAsync(reason).ConfigureAwait(false);
    }

    private async Task PingLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _lastPingTicks, Stopwatch.GetTimestamp());
                await SendJsonAsync(CommandEnvelope.Now(new PingCommand()), token).ConfigureAwait(false);

                await Task.Delay(PingInterval, token).ConfigureAwait(false);

                // Consider the link dead if no pong arrived recently.
                if (DateTime.UtcNow - _lastPongUtc > KeepAliveTimeout)
                {
                    await HandleDroppedAsync("Connection timed out").ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        catch
        {
            if (!token.IsCancellationRequested)
                await HandleDroppedAsync("Connection lost").ConfigureAwait(false);
        }
    }

    private void RecordPong()
    {
        _lastPongUtc = DateTime.UtcNow;
        var start = Interlocked.Read(ref _lastPingTicks);
        if (start == 0) return;

        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        lock (_statsLock)
        {
            _latencySamples.Enqueue(ms);
            while (_latencySamples.Count > 5)
                _latencySamples.Dequeue();
        }
        OnPropertyChanged(nameof(LatencyMs));
    }

    private bool _dropHandled;
    private async Task HandleDroppedAsync(string reason)
    {
        // Ensure we only report a drop once per session.
        lock (_statsLock)
        {
            if (_dropHandled) return;
            _dropHandled = true;
        }

        AppLog.Warn($"[Conn] Connection dropped: {reason}");
        await DisconnectAsync().ConfigureAwait(false);
        _messenger.Send(new DisconnectedMessage(reason));
    }

    private async Task<(string? text, bool closed)> ReceiveTextAsync(byte[] buffer, CancellationToken token)
    {
        var ws = _ws;
        if (ws is null) return (null, true);

        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return (null, true);

            ms.Write(buffer, 0, result.Count);
            if (ms.Length > Protocol.MaxMessageSize)
                return (null, false);
        }
        while (!result.EndOfMessage);

        Interlocked.Add(ref _bytesReceived, ms.Length);
        OnPropertyChanged(nameof(BytesReceived));
        return (Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length), false);
    }

    private void ResetStats()
    {
        Interlocked.Exchange(ref _bytesSent, 0);
        Interlocked.Exchange(ref _bytesReceived, 0);
        Interlocked.Exchange(ref _lastPingTicks, 0);
        lock (_statsLock)
        {
            _latencySamples.Clear();
            _dropHandled = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
