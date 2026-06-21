using RemoteKm.Shared;

namespace RemoteKm.Host.Models;

/// <summary>
/// User-configurable host settings, persisted to %APPDATA%\RemoteKM\settings.json.
/// </summary>
public class Settings
{
    /// <summary>Port the WebSocket control server listens on.</summary>
    public int ControlPort { get; set; } = Protocol.ControlPort;

    /// <summary>When true (default), unknown clients trigger a confirmation dialog.</summary>
    public bool RequireConfirmation { get; set; } = true;

    /// <summary>When true, two-finger scroll direction is inverted.</summary>
    public bool ReverseScroll { get; set; } = false;

    /// <summary>
    /// Tracks the user's intent for auto-start. The registry is the source of truth and is
    /// reconciled on the Settings window; this is a convenience mirror only.
    /// </summary>
    public bool AutoStart { get; set; } = false;

    public Settings Clone() => new()
    {
        ControlPort = ControlPort,
        RequireConfirmation = RequireConfirmation,
        ReverseScroll = ReverseScroll,
        AutoStart = AutoStart,
    };
}
