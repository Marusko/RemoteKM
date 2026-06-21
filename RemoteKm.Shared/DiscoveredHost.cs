namespace RemoteKm.Shared;

/// <summary>
/// Client-side model of a host found via UDP discovery. Not serialized over the wire.
/// </summary>
public record DiscoveredHost(string Ip, int Port, string HostName, DateTime LastSeen);
