using System.Text.Json.Serialization;

namespace RemoteKm.Shared;

/// <summary>
/// Base type for all input commands. Polymorphic JSON serialization uses a "$type"
/// discriminator so the host can dispatch on the concrete command type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MouseMove), "MouseMove")]
[JsonDerivedType(typeof(MouseClick), "MouseClick")]
[JsonDerivedType(typeof(MouseButtonHold), "MouseButtonHold")]
[JsonDerivedType(typeof(MouseScroll), "MouseScroll")]
[JsonDerivedType(typeof(KeyPress), "KeyPress")]
[JsonDerivedType(typeof(TextInput), "TextInput")]
[JsonDerivedType(typeof(PingCommand), "Ping")]
[JsonDerivedType(typeof(PongCommand), "Pong")]
[JsonDerivedType(typeof(LayoutChangedCommand), "LayoutChanged")]
public abstract record InputCommand;

/// <summary>Relative pointer movement.</summary>
public record MouseMove(float DeltaX, float DeltaY) : InputCommand;

/// <summary>A mouse button click (single or double).</summary>
public record MouseClick(MouseButton Button, ClickType Type) : InputCommand;

/// <summary>Holds (Down) or releases (Up) a mouse button — used for press-and-hold dragging.</summary>
public record MouseButtonHold(MouseButton Button, bool Down) : InputCommand;

/// <summary>Vertical wheel scroll; positive scrolls up.</summary>
public record MouseScroll(float DeltaY) : InputCommand;

/// <summary>A named virtual key press (down, up, or a full down+up).</summary>
public record KeyPress(string Key, KeyAction Action) : InputCommand;

/// <summary>Unicode text to type verbatim.</summary>
public record TextInput(string Text) : InputCommand;

/// <summary>Keepalive request; the host answers with <see cref="PongCommand"/>.</summary>
public record PingCommand : InputCommand;

/// <summary>Keepalive reply.</summary>
public record PongCommand : InputCommand;

/// <summary>
/// Host → client push: the host's active keyboard layout/language changed (e.g. the user
/// switched input language with Alt+Shift). The client re-renders its on-screen keyboard.
/// </summary>
public record LayoutChangedCommand(KeyboardLayout Layout, string Language) : InputCommand;
