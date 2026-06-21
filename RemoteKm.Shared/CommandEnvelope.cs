namespace RemoteKm.Shared;

/// <summary>
/// Wraps an <see cref="InputCommand"/> with a client-side timestamp (Unix ms).
/// This is the unit sent over the control channel during the command loop.
/// </summary>
public record CommandEnvelope(long Timestamp, InputCommand Command)
{
    /// <summary>Convenience factory that stamps the envelope with the current time.</summary>
    public static CommandEnvelope Now(InputCommand command)
        => new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), command);
}
