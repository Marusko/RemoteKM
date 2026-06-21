using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using RemoteKm.Host.Models;
using RemoteKm.Shared;

namespace RemoteKm.Host.Services;

/// <summary>
/// HttpListener-based WebSocket control server. Handles the pairing handshake and the
/// per-client command loop, dispatching input to <see cref="InputInjector"/>.
/// </summary>
public sealed class WebSocketServer
{
    private readonly SettingsService _settings;
    private readonly TrustStore _trustStore;

    private readonly ConcurrentDictionary<string, ActiveSession> _sessions = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private Task? _layoutWatcher;
    private int _port;

    /// <summary>Raised whenever the set of active sessions changes.</summary>
    public event Action? SessionsChanged;

    /// <summary>Raised with a human-readable message when the server fails to start.</summary>
    public event Action<string>? StartupFailed;

    /// <summary>
    /// Invoked to confirm an unknown client. Returns true to accept. Wired by the App so
    /// this service stays free of any UI dependency. If null, unknown clients are rejected
    /// when confirmation is required.
    /// </summary>
    public Func<PairingRequest, string, Task<bool>>? ConfirmPairing { get; set; }

    public WebSocketServer(SettingsService settings, TrustStore trustStore)
    {
        _settings = settings;
        _trustStore = trustStore;
        _trustStore.ClientRevoked += clientId => _ = DisconnectAsync(clientId);
    }

    /// <summary>The control port actually bound, or the configured port if not yet/failed to bind.</summary>
    public int Port => _port > 0 ? _port : _settings.Current.ControlPort;

    public IReadOnlyCollection<ActiveSession> ActiveSessions => _sessions.Values.ToList();

    public void Start()
    {
        if (_acceptLoop is { IsCompleted: false })
            return;

        // Try the configured port first, then the shared fallback candidates.
        var candidates = new List<int> { _settings.Current.ControlPort };
        candidates.AddRange(Protocol.ControlPorts);
        var ordered = candidates.Distinct().ToList();

        HttpListenerException? lastError = null;
        foreach (var port in ordered)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                _port = port;
                _cts = new CancellationTokenSource();
                _acceptLoop = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
                _layoutWatcher = Task.Run(() => LayoutWatchLoopAsync(_cts.Token));
                AppLog.Info($"Control server listening on http://+:{port}/");
                return;
            }
            catch (HttpListenerException ex)
            {
                lastError = ex;
                try { listener.Close(); } catch { /* ignored */ }
            }
        }

        // Every candidate failed.
        _listener = null;
        _port = 0;
        var hint = lastError?.ErrorCode == 5
            ? $"Access denied binding http://+:{ordered[0]}/. Run as administrator or add a URL " +
              $"reservation:\n  netsh http add urlacl url=http://+:{ordered[0]}/ user=Everyone"
            : $"Could not open any control port [{string.Join(", ", ordered)}]: {lastError?.Message}";
        StartupFailed?.Invoke(hint);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        try { _cts.Cancel(); } catch { /* ignored */ }

        await DisconnectAllAsync().ConfigureAwait(false);

        try { _listener?.Stop(); } catch { /* ignored */ }
        try { _listener?.Close(); } catch { /* ignored */ }

        foreach (var loop in new[] { _acceptLoop, _layoutWatcher })
        {
            if (loop is null) continue;
            try { await loop.ConfigureAwait(false); }
            catch { /* ignored */ }
        }

        _listener = null;
        _cts.Dispose();
        _cts = null;
        _acceptLoop = null;
        _layoutWatcher = null;
    }

    /// <summary>
    /// Polls the host's active keyboard layout and pushes a <see cref="LayoutChangedCommand"/>
    /// to connected clients when it changes (e.g. the user pressed Alt+Shift).
    /// </summary>
    private async Task LayoutWatchLoopAsync(CancellationToken token)
    {
        var idleTimeout = TimeSpan.FromSeconds(8);
        var (lastFamily, lastLang) = KeyboardLayoutInfo.Describe();
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(1000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            // Drop clients that went silent (e.g. lost Wi-Fi) without sending a close frame.
            var now = DateTime.UtcNow;
            foreach (var session in _sessions.Values.ToList())
            {
                if (now - session.LastReceivedUtc > idleTimeout)
                {
                    AppLog.Warn($"Client '{session.ClientName}' ({session.RemoteIp}) timed out — removing.");
                    await DisconnectAsync(session.ClientId).ConfigureAwait(false);
                }
            }

            if (_sessions.IsEmpty)
                continue;

            var (family, lang) = KeyboardLayoutInfo.Describe();
            if (family == lastFamily && string.Equals(lang, lastLang, StringComparison.Ordinal))
                continue;

            lastFamily = family;
            lastLang = lang;
            AppLog.Info($"Host layout changed to {family}/{lang} — notifying {_sessions.Count} client(s).");
            foreach (var session in _sessions.Values.ToList())
                await SendCommandAsync(session, new LayoutChangedCommand(family, lang), token).ConfigureAwait(false);
        }
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break; // listener stopped
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                continue;
            }

            _ = Task.Run(() => HandleConnectionAsync(context, token));
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken serverToken)
    {
        WebSocket socket;
        try
        {
            // KeepAliveInterval makes the OS send WebSocket pings; a dead peer then trips
            // ReceiveAsync. The idle watchdog below is the deterministic backstop.
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null,
                keepAliveInterval: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            socket = wsContext.WebSocket;
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ignored */ }
            return;
        }

        var remoteIp = context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
        var buffer = new byte[Protocol.MaxMessageSize];
        AppLog.Info($"WebSocket connection from {remoteIp}");

        // Capture the active keyboard layout + language now, before any pairing dialog
        // can steal focus.
        var (keyboardLayout, language) = KeyboardLayoutInfo.Describe();

        // ---- Handshake ----
        ActiveSession? session;
        try
        {
            var (firstText, closed) = await ReceiveTextAsync(socket, buffer, serverToken).ConfigureAwait(false);
            if (closed || firstText is null)
            {
                await AbortAsync(socket).ConfigureAwait(false);
                return;
            }

            var request = RemoteJson.Deserialize<PairingRequest>(firstText);
            if (request is null || string.IsNullOrWhiteSpace(request.ClientId))
            {
                await AbortAsync(socket).ConfigureAwait(false);
                return;
            }

            session = await PerformHandshakeAsync(socket, request, remoteIp, keyboardLayout, language, serverToken).ConfigureAwait(false);
            if (session is null)
                return; // rejected; socket already closed
        }
        catch
        {
            await AbortAsync(socket).ConfigureAwait(false);
            return;
        }

        // ---- Command loop ----
        try
        {
            await CommandLoopAsync(session, buffer).ConfigureAwait(false);
        }
        finally
        {
            _sessions.TryRemove(session.ClientId, out _);
            await AbortAsync(socket).ConfigureAwait(false);
            SessionsChanged?.Invoke();
        }
    }

    private async Task<ActiveSession?> PerformHandshakeAsync(
        WebSocket socket, PairingRequest request, string remoteIp,
        KeyboardLayout keyboardLayout, string language, CancellationToken token)
    {
        PairingStatus status;
        AppLog.Info($"Pairing request from '{request.ClientName}' ({remoteIp}), id={request.ClientId[..Math.Min(8, request.ClientId.Length)]}…");

        if (_trustStore.IsTrusted(request.ClientId))
        {
            _trustStore.Touch(request.ClientId);
            status = PairingStatus.AlreadyTrusted;
        }
        else if (!_settings.Current.RequireConfirmation)
        {
            TrustClient(request);
            status = PairingStatus.Accepted;
        }
        else
        {
            var confirm = ConfirmPairing;
            bool accepted = confirm is not null && await confirm(request, remoteIp).ConfigureAwait(false);
            if (accepted)
            {
                TrustClient(request);
                status = PairingStatus.Accepted;
            }
            else
            {
                status = PairingStatus.Rejected;
            }
        }

        var response = new PairingResponse(status, Environment.MachineName, keyboardLayout, language);
        await SendJsonAsync(socket, response, token).ConfigureAwait(false);
        AppLog.Info($"Pairing result for '{request.ClientName}' ({remoteIp}): {status}");

        if (status == PairingStatus.Rejected)
        {
            await CloseAsync(socket, "Rejected").ConfigureAwait(false);
            return null;
        }

        // Drop any pre-existing session for the same client before registering the new one.
        if (_sessions.TryRemove(request.ClientId, out var old))
            await AbortAsync(old.Socket).ConfigureAwait(false);

        var session = new ActiveSession
        {
            ClientId = request.ClientId,
            ClientName = string.IsNullOrWhiteSpace(request.ClientName) ? "Unknown device" : request.ClientName,
            RemoteIp = remoteIp,
            Socket = socket,
        };
        _sessions[request.ClientId] = session;
        SessionsChanged?.Invoke();
        return session;
    }

    private void TrustClient(PairingRequest request)
    {
        var now = DateTime.Now;
        var existing = _trustStore.Get(request.ClientId);
        _trustStore.AddOrUpdate(new TrustedClient(
            ClientId: request.ClientId,
            ClientName: request.ClientName,
            PublicKeyBase64: request.PublicKeyBase64 ?? string.Empty,
            TrustedSince: existing?.TrustedSince ?? now,
            LastSeen: now));
    }

    private async Task CommandLoopAsync(ActiveSession session, byte[] buffer)
    {
        var token = session.Cts.Token;
        while (!token.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
        {
            var (text, closed) = await ReceiveTextAsync(session.Socket, buffer, token).ConfigureAwait(false);
            if (closed)
                break;
            session.MarkReceived();
            if (text is null)
                continue;

            CommandEnvelope? envelope;
            try
            {
                envelope = RemoteJson.Deserialize<CommandEnvelope>(text);
            }
            catch
            {
                continue; // ignore malformed frames
            }

            if (envelope?.Command is null)
                continue;

            if (envelope.Command is PingCommand)
            {
                await SendCommandAsync(session, new PongCommand(), token).ConfigureAwait(false);
                continue;
            }

            try
            {
                InputInjector.Dispatch(envelope.Command);
            }
            catch (Exception ex)
            {
                // A single bad injection shouldn't kill the session.
                AppLog.Error($"Input injection failed for {envelope.Command.GetType().Name}", ex);
            }
        }
    }

    // ---- Session control ----

    public async Task DisconnectAsync(string clientId)
    {
        if (_sessions.TryRemove(clientId, out var session))
        {
            AppLog.Info($"Disconnecting '{session.ClientName}' ({session.RemoteIp}).");
            try { session.Cts.Cancel(); } catch { /* ignored */ }
            await AbortAsync(session.Socket).ConfigureAwait(false);
            SessionsChanged?.Invoke();
        }
    }

    public async Task DisconnectAllAsync()
    {
        var all = _sessions.Values.ToList();
        _sessions.Clear();
        if (all.Count > 0)
            AppLog.Info($"Disconnecting all clients ({all.Count}).");
        foreach (var session in all)
        {
            try { session.Cts.Cancel(); } catch { /* ignored */ }
            await AbortAsync(session.Socket).ConfigureAwait(false);
        }
        if (all.Count > 0)
            SessionsChanged?.Invoke();
    }

    // ---- WebSocket helpers ----

    private static async Task<(string? text, bool closed)> ReceiveTextAsync(
        WebSocket socket, byte[] buffer, CancellationToken token)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (null, true);
            }
            catch (WebSocketException)
            {
                return (null, true);
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return (null, true);

            if (ms.Length + result.Count > Protocol.MaxMessageSize)
            {
                // Oversized frame: drain and ignore.
                if (result.EndOfMessage)
                    return (null, false);
                continue;
            }

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return (Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length), false);
    }

    private static Task SendJsonAsync<T>(WebSocket socket, T value, CancellationToken token)
    {
        var json = RemoteJson.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(
            new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, token);
    }

    private static async Task SendCommandAsync(ActiveSession session, InputCommand command, CancellationToken token)
    {
        try
        {
            await SendJsonAsync(session.Socket, CommandEnvelope.Now(command), token).ConfigureAwait(false);
        }
        catch
        {
            // ignored — receive loop will detect the dead socket
        }
    }

    private static async Task CloseAsync(WebSocket socket, string reason)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            socket.Dispose();
        }
    }

    private static Task AbortAsync(WebSocket socket)
    {
        try { socket.Abort(); } catch { /* ignored */ }
        try { socket.Dispose(); } catch { /* ignored */ }
        return Task.CompletedTask;
    }
}
