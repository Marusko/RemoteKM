using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteKm.Host.Services;

/// <summary>
/// Helpers for discovering the host's current LAN IPv4 address.
/// </summary>
public static class NetworkInfo
{
    /// <summary>
    /// Returns the best-guess local LAN IPv4 address, preferring an address on an
    /// "up", non-loopback, non-virtual adapter that has a gateway. Falls back to a
    /// UDP-socket trick, then to the loopback address.
    /// </summary>
    public static string GetLocalIPv4()
    {
        var fromAdapters = FromActiveAdapter();
        if (fromAdapters is not null)
            return fromAdapters;

        var fromSocket = FromUdpSocket();
        if (fromSocket is not null)
            return fromSocket;

        return IPAddress.Loopback.ToString();
    }

    private static string? FromActiveAdapter()
    {
        try
        {
            var candidates =
                from nic in NetworkInterface.GetAllNetworkInterfaces()
                where nic.OperationalStatus == OperationalStatus.Up
                where nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                                                and not NetworkInterfaceType.Tunnel
                let props = nic.GetIPProperties()
                let hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any))
                from ua in props.UnicastAddresses
                where ua.Address.AddressFamily == AddressFamily.InterNetwork
                where !IPAddress.IsLoopback(ua.Address)
                where !IsLinkLocal(ua.Address)
                // Prefer adapters that have a default gateway (i.e. the active LAN/Wi-Fi).
                orderby hasGateway descending
                select ua.Address.ToString();

            return candidates.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FromUdpSocket()
    {
        try
        {
            // No packets are actually sent; this just picks the outbound interface.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                return ep.Address.ToString();
        }
        catch
        {
            // ignored
        }
        return null;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
