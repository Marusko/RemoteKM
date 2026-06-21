using CommunityToolkit.Mvvm.ComponentModel;
using RemoteKm.Client.Services;
using RemoteKm.Shared;

namespace RemoteKm.Client.ViewModels;

/// <summary>
/// Backs the on-screen keyboard. Exposes the host's layout/language and the low-level
/// primitives the keyboard uses: type Unicode text, and real key down / up / press so the
/// keyboard behaves like a physical one (hold to repeat, hold Alt then press Tab, etc.).
/// </summary>
public partial class KeyboardViewModel : ObservableObject
{
    private readonly ConnectionService _connection;

    public KeyboardViewModel(ConnectionService connection)
    {
        _connection = connection;
    }

    /// <summary>Letter arrangement family reported by the host.</summary>
    public KeyboardLayout Layout => _connection.KeyboardLayout;

    /// <summary>Host's two-letter language code (e.g. "sk"), for top-row characters.</summary>
    public string Language => _connection.HostLanguage;

    /// <summary>Raised (off the UI thread) when the host's layout/language changes live.</summary>
    public event Action? LayoutChanged
    {
        add => _connection.LayoutChanged += value;
        remove => _connection.LayoutChanged -= value;
    }

    /// <summary>Types literal text (Unicode — what the key shows is exactly what is typed).</summary>
    public Task TypeAsync(string text)
        => string.IsNullOrEmpty(text) ? Task.CompletedTask : _connection.SendCommandAsync(new TextInput(text));

    /// <summary>Presses a key down and holds it (released later with <see cref="KeyUpAsync"/>).</summary>
    public Task KeyDownAsync(string vk) => _connection.SendCommandAsync(new KeyPress(vk, KeyAction.Down));

    /// <summary>Releases a previously held key.</summary>
    public Task KeyUpAsync(string vk) => _connection.SendCommandAsync(new KeyPress(vk, KeyAction.Up));

    /// <summary>A full down+up press of a key.</summary>
    public Task PressAsync(string vk) => _connection.SendCommandAsync(new KeyPress(vk, KeyAction.Press));
}
