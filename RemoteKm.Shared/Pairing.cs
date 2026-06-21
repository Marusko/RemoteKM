namespace RemoteKm.Shared;

/// <summary>
/// First message a client sends after the WebSocket upgrade. Identifies the device
/// so the host can look it up in its trust store.
/// </summary>
public record PairingRequest(string ClientId, string ClientName, string PublicKeyBase64);

/// <summary>
/// Host's verdict on a pairing request, plus host metadata the client uses for display
/// (real machine name), for choosing the on-screen keyboard family, and the host's
/// two-letter language code (e.g. "sk") so the client can show the right top-row characters.
/// </summary>
public record PairingResponse(
    PairingStatus Status,
    string HostName = "",
    KeyboardLayout KeyboardLayout = KeyboardLayout.Qwerty,
    string Language = "en");
