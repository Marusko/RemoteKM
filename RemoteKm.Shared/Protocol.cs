namespace RemoteKm.Shared;

/// <summary>
/// Wire-protocol constants shared by the host and client.
/// These strings are part of the on-the-wire contract and must match on both ends.
/// </summary>
public static class Protocol
{
    /// <summary>
    /// Candidate UDP discovery ports, tried in order. The host binds the first available
    /// one; the client broadcasts to all of them, so the two ends agree without any prior
    /// coordination even if some ports are reserved/occupied on a given PC.
    /// </summary>
    public static readonly int[] DiscoveryPorts = { 45454, 47474, 49464 };

    /// <summary>Primary UDP discovery port (first candidate).</summary>
    public static readonly int DiscoveryPort = DiscoveryPorts[0];

    /// <summary>
    /// Candidate TCP/WebSocket control ports. The host binds the first available one and
    /// advertises the actual port via the discovery reply / QR code.
    /// </summary>
    public static readonly int[] ControlPorts = { 45455, 47475, 49465 };

    /// <summary>Default control port (first candidate).</summary>
    public static readonly int ControlPort = ControlPorts[0];

    /// <summary>Payload a client broadcasts to discover hosts.</summary>
    public const string DiscoveryBroadcastPayload = "REMOTEKM_DISCOVER";

    /// <summary>Prefix of a host's discovery reply: "REMOTEKM_HOST:{HostName}:{ControlPort}".</summary>
    public const string DiscoveryResponsePrefix = "REMOTEKM_HOST:";

    /// <summary>URI scheme used by the QR code / manual connect string: "remotekm://ip:port".</summary>
    public const string UriScheme = "remotekm";

    /// <summary>Maximum allowed control message size in bytes.</summary>
    public const int MaxMessageSize = 4 * 1024;

    /// <summary>Builds a discovery response payload for the given host name and port.</summary>
    public static string BuildDiscoveryResponse(string hostName, int controlPort)
        => $"{DiscoveryResponsePrefix}{hostName}:{controlPort}";
}
