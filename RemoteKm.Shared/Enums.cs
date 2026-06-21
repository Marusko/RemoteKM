namespace RemoteKm.Shared;

public enum MouseButton
{
    Left,
    Right,
    Middle
}

public enum ClickType
{
    Single,
    Double
}

public enum KeyAction
{
    Down,
    Up,
    Press
}

public enum PairingStatus
{
    Accepted,
    Rejected,
    AlreadyTrusted
}

/// <summary>
/// On-screen keyboard family the client should render, derived from the host's active
/// keyboard layout/language.
/// </summary>
public enum KeyboardLayout
{
    Qwerty,
    Qwertz,
    Azerty
}
