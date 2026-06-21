using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteKm.Shared;

/// <summary>
/// Source-generated JSON metadata for every type that crosses the wire. Using a
/// <see cref="JsonSerializerContext"/> keeps serialization trim- and AOT-safe (important
/// for the trimmed MAUI client) and avoids reflection at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CommandEnvelope))]
[JsonSerializable(typeof(InputCommand))]
[JsonSerializable(typeof(MouseMove))]
[JsonSerializable(typeof(MouseClick))]
[JsonSerializable(typeof(MouseButtonHold))]
[JsonSerializable(typeof(MouseScroll))]
[JsonSerializable(typeof(KeyPress))]
[JsonSerializable(typeof(TextInput))]
[JsonSerializable(typeof(PingCommand))]
[JsonSerializable(typeof(PongCommand))]
[JsonSerializable(typeof(LayoutChangedCommand))]
[JsonSerializable(typeof(PairingRequest))]
[JsonSerializable(typeof(PairingResponse))]
public partial class RemoteJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Centralized serialization helpers used by both host and client so the wire format
/// never drifts between the two ends.
/// </summary>
public static class RemoteJson
{
    public static JsonSerializerOptions Options => RemoteJsonContext.Default.Options;

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, typeof(T), RemoteJsonContext.Default);

    public static T? Deserialize<T>(string json)
        => (T?)JsonSerializer.Deserialize(json, typeof(T), RemoteJsonContext.Default);
}
