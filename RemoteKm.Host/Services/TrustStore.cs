using System.IO;
using System.Text.Json;
using RemoteKm.Host.Models;

namespace RemoteKm.Host.Services;

/// <summary>
/// Thread-safe persistent store of trusted clients
/// (%APPDATA%\RemoteKM\trusted.json). Auto-saves after every mutation.
/// </summary>
public sealed class TrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _lock = new();
    private readonly string _path;
    private readonly Dictionary<string, TrustedClient> _clients = new();

    /// <summary>
    /// Raised (outside the lock) when a client's trust is revoked, so the
    /// <see cref="WebSocketServer"/> can drop any live session for that client.
    /// </summary>
    public event Action<string>? ClientRevoked;

    /// <summary>Raised whenever the trusted set changes, so the tray can refresh.</summary>
    public event Action? Changed;

    public TrustStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteKM");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "trusted.json");
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<TrustedClient>>(json, JsonOptions);
                if (list is null)
                    return;

                _clients.Clear();
                foreach (var c in list)
                    _clients[c.ClientId] = c;
            }
            catch
            {
                // Corrupt file → start empty rather than crash.
            }
        }
    }

    public bool IsTrusted(string clientId)
    {
        lock (_lock)
            return _clients.ContainsKey(clientId);
    }

    public TrustedClient? Get(string clientId)
    {
        lock (_lock)
            return _clients.TryGetValue(clientId, out var c) ? c : null;
    }

    public void AddOrUpdate(TrustedClient client)
    {
        lock (_lock)
        {
            _clients[client.ClientId] = client;
            SaveLocked();
        }
        AppLog.Info($"Trusted device added/updated: '{client.ClientName}'.");
        Changed?.Invoke();
    }

    /// <summary>Updates the LastSeen timestamp for an already-trusted client.</summary>
    public void Touch(string clientId)
    {
        lock (_lock)
        {
            if (!_clients.TryGetValue(clientId, out var existing))
                return;
            _clients[clientId] = existing with { LastSeen = DateTime.Now };
            SaveLocked();
        }
    }

    public void Revoke(string clientId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _clients.Remove(clientId);
            if (removed)
                SaveLocked();
        }

        if (removed)
        {
            AppLog.Info($"Trust revoked: {clientId[..Math.Min(8, clientId.Length)]}…");
            // Tear down any live session for the now-untrusted client.
            ClientRevoked?.Invoke(clientId);
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<TrustedClient> GetAll()
    {
        lock (_lock)
            return _clients.Values.OrderBy(c => c.ClientName).ToList();
    }

    private void SaveLocked()
    {
        var list = _clients.Values.ToList();
        var json = JsonSerializer.Serialize(list, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
