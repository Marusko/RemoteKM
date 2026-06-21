namespace RemoteKm.Host.Models;

/// <summary>
/// A client the host has chosen to trust. Persisted to
/// %APPDATA%\RemoteKM\trusted.json as a list.
/// </summary>
public record TrustedClient(
    string ClientId,        // stable GUID generated once by the client
    string ClientName,      // display name (device name)
    string PublicKeyBase64, // reserved for future HMAC verification
    DateTime TrustedSince,
    DateTime LastSeen
);
