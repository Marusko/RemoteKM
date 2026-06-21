namespace RemoteKm.Client.Services;

/// <summary>
/// Stable per-device identity used by the host's trust store. The ClientId is generated
/// once and persisted in SecureStorage so the host recognizes this device across launches.
/// </summary>
public sealed class ClientIdentity
{
    private const string IdKey = "remotekm_client_id";
    private const string NameKey = "remotekm_client_name";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public string ClientId { get; private set; } = string.Empty;
    public string ClientName { get; private set; } = string.Empty;

    /// <summary>Loads the identity once, generating and persisting it on first launch.</summary>
    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            await InitializeAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeAsync()
    {
        var id = await SafeGetAsync(IdKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
            await SafeSetAsync(IdKey, id).ConfigureAwait(false);
        }

        var name = await SafeGetAsync(NameKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(name))
        {
            name = string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "Android device" : DeviceInfo.Name;
            await SafeSetAsync(NameKey, name).ConfigureAwait(false);
        }

        ClientId = id!;
        ClientName = name!;
    }

    private static async Task<string?> SafeGetAsync(string key)
    {
        try { return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false); }
        catch { return null; }
    }

    private static async Task SafeSetAsync(string key, string value)
    {
        try { await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false); }
        catch { /* SecureStorage can fail on some devices; identity still lives for this session. */ }
    }
}
